using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;
using QuinielaApp.Services;

namespace QuinielaApp.BackgroundServices
{
    /// <summary>
    /// Sincroniza scores con intervalo dinámico: 30s cuando hay partidos en
    /// curso o por empezar dentro de 90min, 5min cuando no hay actividad.
    /// Cuando todos los partidos de una fase terminan → calcula puntos automáticamente.
    /// </summary>
    public class ScoreSyncBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ScoreSyncBackgroundService> _log;

        private const int ActiveIntervalSeconds = 30;
        private const int IdleIntervalSeconds   = 300;

        // Rate-limit por partido: evita llamar GetFixtureDetailAsync más de una
        // vez cada 5 min por partido. Persiste mientras el proceso esté vivo.
        private readonly Dictionary<int, DateTime> _lastFixtureDetailCall = new();

        public ScoreSyncBackgroundService(IServiceProvider services, ILogger<ScoreSyncBackgroundService> log)
        { _services = services; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _log.LogInformation("ScoreSyncBackgroundService iniciado.");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var delaySec = await SyncAsync();
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error en sync automático.");
                    await Task.Delay(TimeSpan.FromSeconds(ActiveIntervalSeconds), ct);
                }
            }
        }

        /// <summary>Ejecuta la sincronización y devuelve el delay (en segundos) hasta el próximo ciclo.</summary>
        private async Task<int> SyncAsync()
        {
            using var scope = _services.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var api         = scope.ServiceProvider.GetRequiredService<ApiFootballService>();
            var predSvc     = scope.ServiceProvider.GetRequiredService<PredictionService>();
            var specialSvc  = scope.ServiceProvider.GetRequiredService<SpecialPredictionService>();
            var now     = DateTime.UtcNow;

            var activeStages = await db.Stages
                .Include(s => s.Matches)
                .Include(s => s.Tournament)
                .Where(s => s.Status == StageStatus.Open || s.Status == StageStatus.InProgress)
                .ToListAsync();

            // OJO: no usar m.Status!="FT" acá — Status puede haber sido forzado a "FT"
            // por NormalizeStatus sin que la API haya confirmado un resultado final
            // real (ver ApplyFixtureData). Si dependiéramos de Status, una fase entera
            // dejaría de sincronizarse para siempre en cuanto ese partido "atascado"
            // fuera el único con MatchDate <= 30 min, sin chance de autocorregirse.
            var stagesToSync = activeStages.Where(s =>
                s.Matches.Any(m =>
                    !ApiFootballService.FinalApiStatuses.Contains(m.ApiStatus ?? "") &&
                    m.MatchDate <= now.AddMinutes(30))
            ).ToList();

            if (stagesToSync.Any())
            {
                _log.LogInformation("Sincronizando {N} fases...", stagesToSync.Count);

                // Llamar GetLiveAsync UNA sola vez por torneo (ApiLeagueId)
                var liveCache = new Dictionary<int, List<ApiFixture>>();
                foreach (var leagueId in stagesToSync.Select(s => s.Tournament.ApiLeagueId).Distinct())
                    liveCache[leagueId] = await api.GetLiveAsync(leagueId);

                foreach (var stage in stagesToSync)
                {
                    var liveFixtures = liveCache.GetValueOrDefault(stage.Tournament.ApiLeagueId, []);
                    await SyncStageAsync(db, api, predSvc, specialSvc, stage, liveFixtures);
                }

                await db.SaveChangesAsync();
            }

            // Recalcular puntos desactualizados: fases activas + fases Finished
            // con partidos actualizados en las últimas 72 h (cubre el caso donde
            // ApiStatus llegó tarde de la API después de que la fase se cerró).
            var recentlyFinishedIds = await db.Stages
                .Where(s => s.Status == StageStatus.Finished &&
                            s.Matches.Any(m => m.LastUpdated > DateTime.UtcNow.AddHours(-72)))
                .Select(s => s.Id)
                .ToListAsync();

            var staleStageIds = activeStages.Select(s => s.Id)
                .Concat(recentlyFinishedIds)
                .Distinct();

            foreach (var sid in staleStageIds)
                await predSvc.CalculateStalePointsAsync(sid);

            // ── Intervalo dinámico para el próximo ciclo ──────
            var allMatches = activeStages.SelectMany(s => s.Matches).ToList();

            // Partido en curso ahora mismo
            bool hayEnCurso = allMatches.Any(m =>
                m.Status == "1H" || m.Status == "HT" ||
                m.Status == "2H" || m.Status == "ET" ||
                m.Status == "P");

            // Partido que empieza en los próximos 90 minutos
            // (para asegurarse de cerrar predicciones a tiempo, 15 min antes del inicio)
            bool porEmpezar = allMatches.Any(m =>
                m.Status == "NS" &&
                m.MatchDate > now &&
                m.MatchDate <= now.AddMinutes(90));

            return (hayEnCurso || porEmpezar) ? ActiveIntervalSeconds : IdleIntervalSeconds;
        }

        private async Task SyncStageAsync(AppDbContext db, ApiFootballService api,
            PredictionService predSvc, SpecialPredictionService specialSvc, Stage stage,
            List<ApiFixture> liveFixtures)
        {
            var now     = DateTime.UtcNow;
            int updated = 0;

            foreach (var match in stage.Matches)
            {
                // OJO: no usar match.Status=="FT" acá — ese campo puede haber sido forzado
                // a "FT" por NormalizeStatus (heurística de partido atascado) sin que la API
                // haya confirmado un resultado final real. Si dependiéramos de Status, un
                // partido de eliminatoria que fue a tiempo extra/penales quedaría congelado
                // para siempre con el marcador del minuto 90 y sin Qualifier.
                if (ApiFootballService.FinalApiStatuses.Contains(match.ApiStatus ?? "")) continue;

                // Partidos "FT" sin ApiStatus (datos históricos previos a que existiera ese
                // campo) y ya terminados hace mucho: cualquier ET/penales real ya habría
                // concluido, así que insistir con el fallback cada 5 min para siempre solo
                // quema requests sin nunca poder cambiar el resultado. Usamos MatchDate (no
                // Status, que puede haber sido forzado) como corte seguro de 24h.
                if (match.Status == "FT" && match.MatchDate < now.AddHours(-24)) continue;

                var live = liveFixtures.FirstOrDefault(f => f.Fixture.Id == match.ApiMatchId);
                if (live != null)
                {
                    var prev      = match.Status;
                    var apiStatus = live.Fixture.Status.Short;
                    ApiFootballService.ApplyFixtureData(match, live);
                    match.Elapsed     = live.Fixture.Status.Elapsed;
                    match.LastUpdated = now;
                    updated++;
                    if (match.Status == "FT" && apiStatus != "FT" && apiStatus != "AET" && apiStatus != "PEN")
                        _log.LogWarning("{H} vs {A}: forzado a FT (API status={S}, elapsed={E})",
                            match.HomeTeam, match.AwayTeam, apiStatus, live.Fixture.Status.Elapsed);
                    if (prev != match.Status)
                        _log.LogInformation("{H} vs {A}: {P}→{N} apiStatus={S} ({Hs}-{As})",
                            match.HomeTeam, match.AwayTeam, prev, match.Status, apiStatus,
                            match.HomeScore, match.AwayScore);
                }
                else
                {
                    // No llamar para partidos que aún no comienzan en los próximos 30 min
                    if (match.Status == "NS" && match.MatchDate > now.AddMinutes(30))
                        continue;

                    // Rate limit: máximo una llamada cada 5 min por partido
                    var lastCall = _lastFixtureDetailCall.GetValueOrDefault(match.ApiMatchId, DateTime.MinValue);
                    if ((now - lastCall).TotalMinutes < 5)
                        continue;

                    _lastFixtureDetailCall[match.ApiMatchId] = now;

                    var detail = await api.GetFixtureDetailAsync(match.ApiMatchId);
                    if (detail != null)
                    {
                        var prev      = match.Status;
                        var apiStatus = detail.Fixture.Status.Short;
                        ApiFootballService.ApplyFixtureData(match, detail);
                        match.Elapsed     = detail.Fixture.Status.Elapsed;
                        match.LastUpdated = now;
                        updated++;
                        if (match.Status == "FT" && apiStatus != "FT" && apiStatus != "AET" && apiStatus != "PEN")
                            _log.LogWarning("{H} vs {A}: forzado a FT (API status={S}, elapsed={E})",
                                match.HomeTeam, match.AwayTeam, apiStatus, detail.Fixture.Status.Elapsed);
                        if (prev != match.Status)
                            _log.LogInformation("{H} vs {A} → {S} apiStatus={Api} ({Hs}-{As})",
                                match.HomeTeam, match.AwayTeam, match.Status, apiStatus,
                                match.HomeScore, match.AwayScore);
                    }
                }
            }

            // Guardar cambios antes de calcular puntos
            await db.SaveChangesAsync();

            var total    = stage.Matches.Count;
            var finished = stage.Matches.Count(m => m.Status == "FT");

            if (total > 0 && finished == total)
            {
                if (stage.Status == StageStatus.Open || stage.Status == StageStatus.InProgress)
                {
                    _log.LogInformation("Fase '{N}' completa — calculando puntos automáticamente...", stage.Name);

                    if (stage.Tournament.ApiLeagueId == 2 && stage.Type == StageType.Final)
                        await specialSvc.CalculateAllAsync(stage.Id);
                    else
                        await predSvc.CalculateStagePointsAsync(stage.Id);

                    _log.LogInformation("Puntos calculados para fase '{N}'.", stage.Name);
                }
            }
            else if (total > 0 && finished > 0)
            {
                // Cálculo parcial: actualizar puntos conforme terminan partidos
                var finishedMatches = stage.Matches.Where(m => m.Status == "FT").ToList();
                await predSvc.CalculatePartialPointsAsync(stage.Id, finishedMatches);

                if (stage.Status == StageStatus.Open)
                    stage.Status = StageStatus.InProgress;
            }

            if (updated > 0)
                _log.LogInformation("Fase '{N}': {U} partidos actualizados.", stage.Name, updated);
        }
    }
}

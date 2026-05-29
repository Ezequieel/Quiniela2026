using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;
using QuinielaApp.Services;

namespace QuinielaApp.BackgroundServices
{
    /// <summary>
    /// Sincroniza scores cada 15s cuando hay partidos activos.
    /// Cuando todos los partidos de una fase terminan → calcula puntos automáticamente.
    /// </summary>
    public class ScoreSyncBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<ScoreSyncBackgroundService> _log;

        private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan IdleInterval   = TimeSpan.FromSeconds(20);

        public ScoreSyncBackgroundService(IServiceProvider services, ILogger<ScoreSyncBackgroundService> log)
        { _services = services; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _log.LogInformation("ScoreSyncBackgroundService iniciado.");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var hasActive = await SyncAsync();
                    await Task.Delay(hasActive ? ActiveInterval : IdleInterval, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error en sync automático.");
                    await Task.Delay(ActiveInterval, ct);
                }
            }
        }

        private async Task<bool> SyncAsync()
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

            var stagesToSync = activeStages.Where(s =>
                s.Matches.Any(m => m.Status != "FT" && m.MatchDate <= now.AddMinutes(30))
            ).ToList();

            if (!stagesToSync.Any()) return false;

            _log.LogInformation("Sincronizando {N} fases...", stagesToSync.Count);

            foreach (var stage in stagesToSync)
                await SyncStageAsync(db, api, predSvc, specialSvc, stage);

            await db.SaveChangesAsync();
            return true;
        }

        private async Task SyncStageAsync(AppDbContext db, ApiFootballService api,
            PredictionService predSvc, SpecialPredictionService specialSvc, Stage stage)
        {
            var now          = DateTime.UtcNow;
            var liveFixtures = await api.GetLiveAsync(stage.Tournament.ApiLeagueId);
            int updated      = 0;

            foreach (var match in stage.Matches)
            {
                if (match.Status == "FT") continue;

                var live = liveFixtures.FirstOrDefault(f => f.Fixture.Id == match.ApiMatchId);
                if (live != null)
                {
                    var prev          = match.Status;
                    match.HomeScore   = live.Goals.Home;
                    match.AwayScore   = live.Goals.Away;
                    match.Status      = live.Fixture.Status.Short;
                    match.Elapsed     = live.Fixture.Status.Elapsed;
                    match.LastUpdated = now;
                    updated++;
                    if (prev != match.Status)
                        _log.LogInformation("{H} vs {A}: {P}→{N} ({Hs}-{As})",
                            match.HomeTeam, match.AwayTeam, prev, match.Status,
                            match.HomeScore, match.AwayScore);
                }
                else if (match.Status != "NS" || match.MatchDate <= now.AddMinutes(-100))
                {
                    var detail = await api.GetFixtureDetailAsync(match.ApiMatchId);
                    if (detail != null)
                    {
                        var prev          = match.Status;
                        match.HomeScore   = detail.Goals.Home;
                        match.AwayScore   = detail.Goals.Away;
                        match.Status      = detail.Fixture.Status.Short;
                        match.Elapsed     = detail.Fixture.Status.Elapsed;
                        match.LastUpdated = now;
                        updated++;
                        if (prev != match.Status)
                            _log.LogInformation("{H} vs {A} → {S} ({Hs}-{As})",
                                match.HomeTeam, match.AwayTeam, match.Status,
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

                    // Champions Final → usar servicio especial con datos de eventos
                    if (stage.Tournament.ApiLeagueId == 2 && stage.Type == StageType.Final)
                        await specialSvc.CalculateAllAsync(stage.Id);
                    else
                        await predSvc.CalculateStagePointsAsync(stage.Id);

                    _log.LogInformation("Puntos calculados para fase '{N}'.", stage.Name);
                }
            }
            else if (total > 0 && finished > 0 && stage.Status == StageStatus.Open)
            {
                stage.Status = StageStatus.InProgress;
            }

            if (updated > 0)
                _log.LogInformation("Fase '{N}': {U} partidos actualizados.", stage.Name, updated);
        }
    }
}

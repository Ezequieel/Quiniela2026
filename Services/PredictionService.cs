using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Helpers;
using QuinielaApp.Models;

namespace QuinielaApp.Services
{
    /// <summary>
    /// Sistema de puntos NO ACUMULATIVO — se otorga el mayor nivel alcanzado:
    ///
    ///   5 pts — Marcador exacto (ambos goles correctos + resultado correcto)
    ///   2 pts — Solo resultado correcto (quién gana/empata)
    ///   0 pts — No acertó nada
    ///
    /// </summary>
    public class PredictionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PredictionService> _log;

        private const int PTS_EXACT   = 5;   // marcador exacto
        private const int PTS_RESULT  = 2;   // solo resultado
        private const int PTS_NOTHING = 0;

        public PredictionService(AppDbContext db, ILogger<PredictionService> log)
        { _db = db; _log = log; }

        // ── Guardar predicción ────────────────────────────
        public async Task<(bool ok, string msg)> SaveAsync(
            int userId, int matchId, string resultPred, int? homeScore, int? awayScore)
        {
            var match = await _db.Matches.Include(m => m.Stage)
                .FirstOrDefaultAsync(m => m.Id == matchId);
            if (match == null) return (false, "Partido no encontrado.");
            // MatchDate ya viene en UTC real (ver ApiFootballService.MapToMatch)
            var matchDeadlineUtc = match.MatchDate.AddMinutes(-15);
            if (DateTime.UtcNow >= matchDeadlineUtc)
                return (false, "Las predicciones para este partido cerraron.");
            if (match.Status == "FT")
                return (false, "Este partido ya finalizó.");
            if (!Enum.TryParse<MatchResult>(resultPred, out var result))
                return (false, "Predicción inválida.");

            // Validar coherencia marcador vs resultado
            if (homeScore.HasValue && awayScore.HasValue)
            {
                if (result == MatchResult.Home && homeScore <= awayScore)
                    return (false, "Si gana el local, su marcador debe ser mayor.");
                if (result == MatchResult.Away && awayScore <= homeScore)
                    return (false, "Si gana el visitante, su marcador debe ser mayor.");
                if (result == MatchResult.Draw && homeScore != awayScore)
                    return (false, "En un empate ambos marcadores deben ser iguales.");
            }

            // Verificar pago — el pago es por TORNEO, no por fase
            var stageId      = match.StageId;
            var tournamentId = match.Stage.TournamentId;
            var paid = await _db.Payments
                .Include(p => p.Stage)
                .AnyAsync(p =>
                    p.UserId == userId &&
                    p.Stage.TournamentId == tournamentId &&
                    p.Status == PaymentStatus.Approved);
            if (!paid) return (false, "Debes inscribirte al torneo antes de predecir.");

            var existing = await _db.Predictions
                .FirstOrDefaultAsync(p => p.UserId == userId && p.MatchId == matchId);
            if (existing != null)
            {
                existing.ResultPrediction = result;
                existing.HomeScorePred    = homeScore;
                existing.AwayScorePred    = awayScore;
                existing.UpdatedAt        = DateTime.UtcNow;
            }
            else
            {
                _db.Predictions.Add(new Prediction
                {
                    UserId           = userId,
                    MatchId          = matchId,
                    ResultPrediction = result,
                    HomeScorePred    = homeScore,
                    AwayScorePred    = awayScore
                });
            }
            await _db.SaveChangesAsync();
            return (true, "Predicción guardada.");
        }

        // ── Calcular puntos parciales (partidos FT individualmente) ──────────
        public async Task CalculatePartialPointsAsync(int stageId, List<Match> finishedMatches)
        {
            var stage = await _db.Stages.FindAsync(stageId);
            if (stage == null) return;
            var tournamentId = stage.TournamentId;

            var participants = await _db.TournamentEntries
                .Where(te => te.TournamentId == tournamentId)
                .Select(te => te.UserId)
                .ToListAsync();

            var allStageIds = await _db.Stages
                .Where(s => s.TournamentId == tournamentId)
                .Select(s => s.Id)
                .ToListAsync();

            foreach (var uid in participants)
            {
                int totalPts = 0, resultHits = 0, scoreHits = 0;

                foreach (var match in finishedMatches)
                {
                    var actual = ApiFootballService.GetResult(match.HomeScore, match.AwayScore);
                    if (actual == null) continue;
                    var pred = await _db.Predictions
                        .FirstOrDefaultAsync(p => p.UserId == uid && p.MatchId == match.Id);
                    if (pred == null) continue;

                    bool resultOk = pred.ResultPrediction == actual;
                    bool scoreOk  = pred.HomeScorePred == match.HomeScore &&
                                    pred.AwayScorePred == match.AwayScore;
                    pred.ResultCorrect = resultOk;
                    pred.ScoreCorrect  = scoreOk;

                    int pts;
                    if (scoreOk)       { pts = PTS_EXACT;  scoreHits++;  resultHits++; }
                    else if (resultOk) { pts = PTS_RESULT; resultHits++; }
                    else               { pts = PTS_NOTHING; }
                    pred.PointsEarned = pts;
                    totalPts += pts;
                }

                // Actualizar / crear StageResult con puntos parciales
                var sr = await _db.StageResults
                    .FirstOrDefaultAsync(r => r.StageId == stageId && r.UserId == uid);
                if (sr == null)
                    _db.StageResults.Add(new StageResult
                    {
                        StageId    = stageId, UserId = uid,
                        Points     = totalPts,
                        ResultHits = resultHits, ScoreHits = scoreHits
                    });
                else
                { sr.Points = totalPts; sr.ResultHits = resultHits; sr.ScoreHits = scoreHits; }

                // Actualizar / crear StageEntry con puntos parciales
                var se = await _db.StageEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.StageId == stageId);
                if (se == null)
                    _db.StageEntries.Add(new StageEntry
                    { UserId = uid, StageId = stageId, IsActive = true, Points = totalPts });
                else
                    se.Points = totalPts;

                // Recalcular total del torneo sumando todos los StageResults del usuario
                var te = await _db.TournamentEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null)
                {
                    var allPts = await _db.StageResults
                        .Where(r => r.UserId == uid && allStageIds.Contains(r.StageId))
                        .SumAsync(r => (int?)r.Points) ?? 0;
                    te.TotalPoints = allPts;
                }
            }

            // Rankings parciales
            var results = await _db.StageResults
                .Where(r => r.StageId == stageId)
                .OrderByDescending(r => r.Points)
                .ToListAsync();
            for (int i = 0; i < results.Count; i++) results[i].Rank = i + 1;

            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos parciales — fase {S}, {N} partidos FT",
                stageId, finishedMatches.Count);
        }

        // ── Calcular puntos ───────────────────────────────
        public async Task CalculateStagePointsAsync(int stageId)
        {
            var stage = await _db.Stages
                .Include(s => s.Matches).ThenInclude(m => m.Predictions)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null || stage.Status == StageStatus.Finished) return;

            // Participantes: todos los que pagaron el torneo (no solo la fase)
            var tournamentId = stage.TournamentId;
            var participants = await _db.TournamentEntries
                .Where(te => te.TournamentId == tournamentId)
                .Select(te => te.UserId)
                .ToListAsync();

            foreach (var uid in participants)
            {
int totalPts = 0, resultHits = 0, scoreHits = 0, total = 0;

                foreach (var match in stage.Matches.Where(m => m.Status == "FT"))
                {
                    var actual = ApiFootballService.GetResult(match.HomeScore, match.AwayScore);
                    if (actual == null) continue;
                    var pred = match.Predictions.FirstOrDefault(p => p.UserId == uid);
                    if (pred == null) continue;
                    total++;

                    bool resultOk = pred.ResultPrediction == actual;
                    bool scoreOk  = pred.HomeScorePred == match.HomeScore &&
                                    pred.AwayScorePred == match.AwayScore;
                    pred.ResultCorrect = resultOk;
                    pred.ScoreCorrect  = scoreOk;

                    // Sistema NO acumulativo: se toma el mayor nivel alcanzado
                    int pts;
                    if (scoreOk)        { pts = PTS_EXACT;   scoreHits++; resultHits++; }
                    else if (resultOk)  { pts = PTS_RESULT;  resultHits++; }
                    else                { pts = PTS_NOTHING; }

                    pred.PointsEarned = pts;
                    totalPts += pts;

                    _log.LogDebug("U{U} - {H}vs{A}: exact={E} result={R} → {P}pts",
                        uid, match.HomeTeam, match.AwayTeam, scoreOk, resultOk, pts);
                }

                // Guardar StageResult
                var sr = await _db.StageResults
                    .FirstOrDefaultAsync(r => r.StageId == stageId && r.UserId == uid);
                if (sr == null)
                    _db.StageResults.Add(new StageResult
                    {
                        StageId = stageId, UserId = uid,
                        Points = totalPts, ResultHits = resultHits, ScoreHits = scoreHits
                    });
                else
                {
                    sr.Points = totalPts; sr.ResultHits = resultHits; sr.ScoreHits = scoreHits;
                }

                // Actualizar puntos totales del torneo
                var te = await _db.TournamentEntries.FirstOrDefaultAsync(e =>
                    e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null) te.TotalPoints += totalPts;

                // Actualizar StageEntry (crear si no existe — pago único da acceso a todas las fases)
                var se = await _db.StageEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.StageId == stageId);
                if (se == null)
                    _db.StageEntries.Add(new StageEntry
                    { UserId = uid, StageId = stageId, IsActive = true, Points = totalPts });
                else
                    se.Points = totalPts;
            }

            // Rankings
            var results = await _db.StageResults.Where(r => r.StageId == stageId)
                .OrderByDescending(r => r.Points).ToListAsync();
            for (int i = 0; i < results.Count; i++) results[i].Rank = i + 1;

            stage.Status = StageStatus.Finished;
            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos calculados para fase {S}", stage.Name);
        }
    }
}

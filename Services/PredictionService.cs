using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Helpers;
using QuinielaApp.Models;

namespace QuinielaApp.Services
{
    public class PredictionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PredictionService> _log;

        public PredictionService(AppDbContext db, ILogger<PredictionService> log)
        { _db = db; _log = log; }

        // ── Guardar predicción ────────────────────────────
        public async Task<(bool ok, string msg)> SaveAsync(
            int userId, int matchId, string resultPred, int? homeScore, int? awayScore)
        {
            var match = await _db.Matches.Include(m => m.Stage)
                .FirstOrDefaultAsync(m => m.Id == matchId);
            if (match == null) return (false, "Partido no encontrado.");
            var matchDeadlineUtc = match.MatchDate.AddMinutes(-15);
            if (DateTime.UtcNow >= matchDeadlineUtc)
                return (false, "Las predicciones para este partido cerraron.");
            if (match.Status == "FT")
                return (false, "Este partido ya finalizó.");
            if (!Enum.TryParse<MatchResult>(resultPred, out var result))
                return (false, "Predicción inválida.");

            if (homeScore.HasValue && awayScore.HasValue)
            {
                if (result == MatchResult.Home && homeScore <= awayScore)
                    return (false, "Si gana el local, su marcador debe ser mayor.");
                if (result == MatchResult.Away && awayScore <= homeScore)
                    return (false, "Si gana el visitante, su marcador debe ser mayor.");
                if (result == MatchResult.Draw && homeScore != awayScore)
                    return (false, "En un empate ambos marcadores deben ser iguales.");
            }

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

            var stageMatchIds = await _db.Matches
                .Where(m => m.StageId == stageId)
                .Select(m => m.Id)
                .ToListAsync();

            bool isKnockout = stage.Type != StageType.League && stage.Type != StageType.GroupStage;
            var matchIds = finishedMatches.Select(m => m.Id).ToList();

            // Fase 1: resetear y recalcular todas las predicciones del batch de una vez
            var allBatchPreds = await _db.Predictions
                .Where(p => matchIds.Contains(p.MatchId))
                .ToListAsync();

            foreach (var pred in allBatchPreds)
            {
                pred.PointsEarned  = 0;
                pred.ResultCorrect = null;
                pred.ScoreCorrect  = null;
            }

            foreach (var match in finishedMatches)
            {
                if (!match.HomeScore.HasValue || !match.AwayScore.HasValue) continue;

                var matchPreds = allBatchPreds.Where(p => p.MatchId == match.Id).ToList();
                foreach (var pred in matchPreds)
                {
                    var r = PointsCalculator.Calculate(
                        match.HomeScore.Value,
                        match.AwayScore.Value,
                        match.ApiStatus,
                        match.Qualifier,
                        isKnockout,
                        pred.ResultPrediction.ToString(),
                        pred.HomeScorePred ?? -1,
                        pred.AwayScorePred ?? -1,
                        pred.QualifierPred,
                        pred.PenaltyPred);

                    pred.PointsEarned  = r.Points;
                    pred.ResultCorrect = r.ResultCorrect;
                    pred.ScoreCorrect  = r.ScoreCorrect;

                    _log.LogInformation(
                        "Partido {Id} U{U}: {Home}-{Away} pred={Pred} pts={Pts}",
                        match.Id, pred.UserId, match.HomeScore, match.AwayScore,
                        pred.ResultPrediction, r.Points);
                }
            }

            // Persistir todas las predicciones del batch antes de sumar totales
            await _db.SaveChangesAsync();

            // Fase 2: recalcular totales por usuario sumando TODAS las predicciones
            // calculadas de la fase (no solo el batch), para evitar bugs de acumulación
            foreach (var uid in participants)
            {
                var stagePreds = await _db.Predictions
                    .Where(p => p.UserId == uid &&
                                stageMatchIds.Contains(p.MatchId) &&
                                p.ResultCorrect != null)
                    .ToListAsync();

                int totalPts   = stagePreds.Sum(p => p.PointsEarned);
                int resultHits = stagePreds.Count(p => p.ResultCorrect == true);
                int scoreHits  = stagePreds.Count(p => p.ScoreCorrect == true);

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

                var se = await _db.StageEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.StageId == stageId);
                if (se == null)
                    _db.StageEntries.Add(new StageEntry
                    { UserId = uid, StageId = stageId, IsActive = true, Points = totalPts });
                else
                    se.Points = totalPts;

                var te = await _db.TournamentEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null)
                {
                    var otherStagesPts = await _db.StageResults
                        .Where(r => r.UserId == uid &&
                                    r.StageId != stageId &&
                                    allStageIds.Contains(r.StageId))
                        .SumAsync(r => (int?)r.Points) ?? 0;
                    te.TotalPoints = otherStagesPts + totalPts;
                }
            }

            var results = await _db.StageResults
                .Where(r => r.StageId == stageId)
                .OrderByDescending(r => r.Points)
                .ToListAsync();
            for (int i = 0; i < results.Count; i++) results[i].Rank = i + 1;

            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos parciales — fase {S}, {N} partidos FT",
                stageId, finishedMatches.Count);
        }

        // ── Recalcular predicciones pendientes (FT con ResultCorrect == null) ──
        public async Task<int> CalculatePendingPointsAsync(int stageId)
        {
            var finishedMatches = await _db.Matches
                .Where(m => m.StageId == stageId &&
                            m.Status == "FT" &&
                            m.HomeScore != null &&
                            m.AwayScore != null)
                .ToListAsync();
            if (finishedMatches.Count == 0) return 0;

            var matchIds = finishedMatches.Select(m => m.Id).ToList();
            var pendingMatchIds = await _db.Predictions
                .Where(p => matchIds.Contains(p.MatchId) && p.ResultCorrect == null)
                .Select(p => p.MatchId)
                .Distinct()
                .ToListAsync();
            if (pendingMatchIds.Count == 0) return 0;

            var matchesToCalculate = finishedMatches
                .Where(m => pendingMatchIds.Contains(m.Id))
                .ToList();

            await CalculatePartialPointsAsync(stageId, matchesToCalculate);
            return matchesToCalculate.Count;
        }

        // ── Calcular puntos de fase completa ───────────────
        public async Task CalculateStagePointsAsync(int stageId)
        {
            var stage = await _db.Stages
                .Include(s => s.Matches)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null || stage.Status == StageStatus.Finished) return;

            var tournamentId = stage.TournamentId;
            var participants = await _db.TournamentEntries
                .Where(te => te.TournamentId == tournamentId)
                .Select(te => te.UserId)
                .ToListAsync();

            var allStageIds = await _db.Stages
                .Where(s => s.TournamentId == tournamentId)
                .Select(s => s.Id)
                .ToListAsync();

            bool isKnockout = stage.Type != StageType.League && stage.Type != StageType.GroupStage;

            var ftMatches = stage.Matches
                .Where(m => m.Status == "FT" && m.HomeScore != null && m.AwayScore != null)
                .ToList();
            var stageMatchIds = stage.Matches.Select(m => m.Id).ToList();

            // Cargar todas las predicciones de la fase en una sola consulta
            var allPreds = await _db.Predictions
                .Where(p => stageMatchIds.Contains(p.MatchId))
                .ToListAsync();

            // Calcular puntos por partido
            foreach (var match in ftMatches)
            {
                var matchPreds = allPreds.Where(p => p.MatchId == match.Id).ToList();
                foreach (var pred in matchPreds)
                {
                    var r = PointsCalculator.Calculate(
                        match.HomeScore!.Value,
                        match.AwayScore!.Value,
                        match.ApiStatus,
                        match.Qualifier,
                        isKnockout,
                        pred.ResultPrediction.ToString(),
                        pred.HomeScorePred ?? -1,
                        pred.AwayScorePred ?? -1,
                        pred.QualifierPred,
                        pred.PenaltyPred);

                    pred.PointsEarned  = r.Points;
                    pred.ResultCorrect = r.ResultCorrect;
                    pred.ScoreCorrect  = r.ScoreCorrect;

                    _log.LogDebug("U{U} - {H}vs{A}: score={E} result={R} → {Pts}pts",
                        pred.UserId, match.HomeTeam, match.AwayTeam,
                        r.ScoreCorrect, r.ResultCorrect, r.Points);
                }
            }

            // Calcular totales por usuario usando predicciones en memoria
            foreach (var uid in participants)
            {
                var ftPreds = allPreds
                    .Where(p => p.UserId == uid &&
                                ftMatches.Any(m => m.Id == p.MatchId))
                    .ToList();

                int totalPts   = ftPreds.Sum(p => p.PointsEarned);
                int resultHits = ftPreds.Count(p => p.ResultCorrect == true);
                int scoreHits  = ftPreds.Count(p => p.ScoreCorrect == true);

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

                var te = await _db.TournamentEntries.FirstOrDefaultAsync(e =>
                    e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null)
                {
                    var otherStagesPts = await _db.StageResults
                        .Where(r => r.UserId == uid &&
                                    r.StageId != stageId &&
                                    allStageIds.Contains(r.StageId))
                        .SumAsync(r => (int?)r.Points) ?? 0;
                    te.TotalPoints = otherStagesPts + totalPts;
                }

                var se = await _db.StageEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.StageId == stageId);
                if (se == null)
                    _db.StageEntries.Add(new StageEntry
                    { UserId = uid, StageId = stageId, IsActive = true, Points = totalPts });
                else
                    se.Points = totalPts;
            }

            // Rankings
            var rankResults = await _db.StageResults
                .Where(r => r.StageId == stageId)
                .OrderByDescending(r => r.Points)
                .ToListAsync();
            for (int i = 0; i < rankResults.Count; i++) rankResults[i].Rank = i + 1;

            // Estado de la fase
            var totalPartidos      = stage.Matches.Count;
            var partidosTerminados = stage.Matches.Count(m => m.Status == "FT");
            if (totalPartidos > 0 && partidosTerminados == totalPartidos)
                stage.Status = StageStatus.Finished;
            else if (partidosTerminados > 0)
                stage.Status = StageStatus.InProgress;

            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos calculados para fase {S}", stage.Name);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Helpers;
using QuinielaApp.Models;

namespace QuinielaApp.Services
{
    /// <summary>
    /// Sistema de puntos ACUMULATIVO:
    ///
    /// Liga/Grupos:
    ///   5 pts — Marcador exacto
    ///   2 pts — Solo resultado correcto
    ///   0 pts — Nada
    ///
    /// Eliminatorias:
    ///   5 pts — Marcador exacto solo
    ///   7 pts — Marcador exacto + clasificado correcto
    ///   8 pts — Marcador exacto + clasificado + penaltis correcto
    ///   2 pts — Solo resultado correcto
    ///   4 pts — Resultado correcto + clasificado correcto
    ///   5 pts — Resultado correcto + clasificado + penaltis correcto
    ///   0 pts — Nada
    ///
    /// Los +2 de clasificado y +1 de penaltis SOLO se suman si el usuario
    /// eligió QualifierPred != null (elección activa). Sin elección, no hay
    /// bonus aunque el resultado sea correcto. Sin clasificado automático.
    /// </summary>
    public class PredictionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PredictionService> _log;

        private const int PTS_EXACT   = 5;  // liga: marcador exacto
        private const int PTS_RESULT  = 2;  // liga: solo resultado
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

            // Resetear predicciones de los partidos a recalcular para que cada
            // corrida parta de cero y no se acumulen puntos entre llamadas.
            var matchIds = finishedMatches.Select(m => m.Id).ToList();
            var predsToReset = await _db.Predictions
                .Where(p => matchIds.Contains(p.MatchId))
                .ToListAsync();
            foreach (var pred in predsToReset)
            {
                pred.PointsEarned  = 0;
                pred.ResultCorrect = null;
                pred.ScoreCorrect  = null;
            }
            await _db.SaveChangesAsync();

            // Otros partidos FT de la fase que no forman parte de este batch:
            // sus puntos ya calculados deben sumarse al total de la fase para
            // no sobrescribir StageResult/StageEntry/TournamentEntry con solo
            // los puntos de los partidos de este batch.
            var otherFtMatchIds = await _db.Matches
                .Where(m => m.StageId == stageId && m.Status == "FT" &&
                            m.HomeScore != null && m.AwayScore != null &&
                            !matchIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync();

            bool isKnockout = stage!.Type != StageType.League && stage.Type != StageType.GroupStage;

            foreach (var uid in participants)
            {
                int totalPts = 0, resultHits = 0, scoreHits = 0;

                foreach (var match in finishedMatches)
                {
                    if (!match.HomeScore.HasValue || !match.AwayScore.HasValue) continue;

                    string actual;
                    if (match.HomeScore > match.AwayScore)      actual = "Home";
                    else if (match.HomeScore < match.AwayScore) actual = "Away";
                    else                                         actual = "Draw";

                    var pred = await _db.Predictions
                        .FirstOrDefaultAsync(p => p.UserId == uid && p.MatchId == match.Id);
                    if (pred == null) continue;

                    bool resultOk = string.Equals(
                        pred.ResultPrediction.ToString().Trim(), actual,
                        StringComparison.OrdinalIgnoreCase);
                    bool scoreOk = pred.HomeScorePred == match.HomeScore &&
                                   pred.AwayScorePred == match.AwayScore;

                    _log.LogInformation(
                        "Partido {Id}: {Home}-{Away} pred={Pred} actual={Actual} ok={Ok}",
                        match.Id, match.HomeScore, match.AwayScore,
                        pred.ResultPrediction, actual, resultOk);

                    bool huboExtension = match.ApiStatus == "AET" || match.ApiStatus == "PEN";
                    bool huboPenaltis  = match.ApiStatus == "PEN";

                    pred.ResultCorrect = resultOk;
                    pred.ScoreCorrect  = scoreOk;

                    int pts = PTS_NOTHING;

                    if (isKnockout)
                    {
                        if (scoreOk)       { pts = 5; scoreHits++; resultHits++; }
                        else if (resultOk) { pts = 2; resultHits++; }

                        if (pts > 0 && huboExtension &&
                            pred.QualifierPred != null && match.Qualifier != null)
                        {
                            bool qualifierOk = string.Equals(
                                pred.QualifierPred.Trim(), match.Qualifier,
                                StringComparison.OrdinalIgnoreCase);
                            if (qualifierOk)
                            {
                                pts += 2;
                                if (huboPenaltis && pred.PenaltyPred == true) pts += 1;
                            }
                        }
                    }
                    else
                    {
                        if (scoreOk)       { pts = PTS_EXACT;   scoreHits++; resultHits++; }
                        else if (resultOk) { pts = PTS_RESULT;  resultHits++; }
                    }

                    pred.PointsEarned = pts;
                    totalPts += pts;
                }

                if (otherFtMatchIds.Count > 0)
                {
                    var otherStats = await _db.Predictions
                        .Where(p => p.UserId == uid && otherFtMatchIds.Contains(p.MatchId))
                        .ToListAsync();
                    totalPts   += otherStats.Sum(p => p.PointsEarned);
                    resultHits += otherStats.Count(p => p.ResultCorrect == true);
                    scoreHits  += otherStats.Count(p => p.ScoreCorrect == true);
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

                // Recalcular total del torneo: otras fases (BD) + esta fase recién calculada.
                // No se usa SumAsync sobre el StageResult de esta fase porque, si "sr" es
                // nuevo, todavía no está persistido y no aparecería en la suma.
                var te = await _db.TournamentEntries
                    .FirstOrDefaultAsync(e => e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null)
                {
                    var otherStagesPts = await _db.StageResults
                        .Where(r => r.UserId == uid && r.StageId != stageId && allStageIds.Contains(r.StageId))
                        .SumAsync(r => (int?)r.Points) ?? 0;
                    te.TotalPoints = otherStagesPts + totalPts;
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

        // ── Recalcular predicciones pendientes (FT con ResultCorrect == null) ──
        // Cubre casos donde un partido pasó a FT sin que se ejecutara el cálculo
        // de puntos (ej. sync manual de partidos). Devuelve la cantidad de
        // partidos recalculados.
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

            var allStageIds = await _db.Stages
                .Where(s => s.TournamentId == tournamentId)
                .Select(s => s.Id)
                .ToListAsync();

            bool stageIsKnockout = stage.Type != StageType.League && stage.Type != StageType.GroupStage;

            foreach (var uid in participants)
            {
                int totalPts = 0, resultHits = 0, scoreHits = 0, total = 0;

                foreach (var match in stage.Matches.Where(m => m.Status == "FT"))
                {
                    if (!match.HomeScore.HasValue || !match.AwayScore.HasValue) continue;

                    string actual;
                    if (match.HomeScore > match.AwayScore)      actual = "Home";
                    else if (match.HomeScore < match.AwayScore) actual = "Away";
                    else                                         actual = "Draw";

                    var pred = match.Predictions.FirstOrDefault(p => p.UserId == uid);
                    if (pred == null) continue;
                    total++;

                    bool resultOk = string.Equals(
                        pred.ResultPrediction.ToString().Trim(), actual,
                        StringComparison.OrdinalIgnoreCase);
                    bool scoreOk = pred.HomeScorePred == match.HomeScore &&
                                   pred.AwayScorePred == match.AwayScore;

                    _log.LogInformation(
                        "Partido {Id}: {Home}-{Away} pred={Pred} actual={Actual} ok={Ok}",
                        match.Id, match.HomeScore, match.AwayScore,
                        pred.ResultPrediction, actual, resultOk);

                    bool huboExtension = match.ApiStatus == "AET" || match.ApiStatus == "PEN";
                    bool huboPenaltis  = match.ApiStatus == "PEN";

                    pred.ResultCorrect = resultOk;
                    pred.ScoreCorrect  = scoreOk;

                    int pts = PTS_NOTHING;

                    if (stageIsKnockout)
                    {
                        if (scoreOk)       { pts = 5; scoreHits++; resultHits++; }
                        else if (resultOk) { pts = 2; resultHits++; }

                        if (pts > 0 && huboExtension &&
                            pred.QualifierPred != null && match.Qualifier != null)
                        {
                            bool qualifierOk = string.Equals(
                                pred.QualifierPred.Trim(), match.Qualifier,
                                StringComparison.OrdinalIgnoreCase);
                            if (qualifierOk)
                            {
                                pts += 2;
                                if (huboPenaltis && pred.PenaltyPred == true) pts += 1;
                            }
                        }
                    }
                    else
                    {
                        if (scoreOk)       { pts = PTS_EXACT;   scoreHits++; resultHits++; }
                        else if (resultOk) { pts = PTS_RESULT;  resultHits++; }
                    }

                    pred.PointsEarned = pts;
                    totalPts += pts;

                    _log.LogDebug("U{U} - {H}vs{A}: exact={E} result={R} ext={X} pen={P} → {Pts}pts",
                        uid, match.HomeTeam, match.AwayTeam, scoreOk, resultOk, huboExtension, huboPenaltis, pts);
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

                // Recalcular total del torneo: otras fases (BD) + esta fase recién calculada.
                var te = await _db.TournamentEntries.FirstOrDefaultAsync(e =>
                    e.UserId == uid && e.TournamentId == tournamentId);
                if (te != null)
                {
                    var otherStagesPts = await _db.StageResults
                        .Where(r => r.UserId == uid && r.StageId != stageId && allStageIds.Contains(r.StageId))
                        .SumAsync(r => (int?)r.Points) ?? 0;
                    te.TotalPoints = otherStagesPts + totalPts;
                }

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

            // Solo marcar como Finished si TODOS los partidos
            // de la fase tienen status FT
            var totalPartidos      = stage.Matches.Count;
            var partidosTerminados = stage.Matches
                .Count(m => m.Status == "FT");

            if (totalPartidos > 0 &&
                partidosTerminados == totalPartidos)
            {
                stage.Status = StageStatus.Finished;
            }
            // Si no han terminado todos dejar en InProgress
            else if (partidosTerminados > 0)
            {
                stage.Status = StageStatus.InProgress;
            }

            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos calculados para fase {S}", stage.Name);
        }
    }
}

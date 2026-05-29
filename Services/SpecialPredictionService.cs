using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;

namespace QuinielaApp.Services
{
    /// <summary>
    /// Sistema de puntos especial para la Final de Champions League.
    /// Solo aplica cuando Stage.Type == Final y Tournament.ApiLeagueId == 2.
    ///
    /// Puntos NO acumulativos por categoría — se suma independientemente:
    ///   5 pts — Resultado + marcador exacto
    ///   3 pts — Solo resultado correcto
    ///   2 pts — Resultado primer tiempo (HT)
    ///   2 pts — Cantidad exacta de tarjetas rojas
    ///   2 pts — Cantidad exacta de tarjetas amarillas
    ///   2 pts — Si habrá penal en tiempo reglamentario (sí/no)
    ///   1 pt  — Primer equipo en anotar
    ///   1 pt  — Ambos equipos marcan (sí/no)
    ///   1 pt  — Suma total de goles
    ///   1 pt  — Diferencia de goles
    /// </summary>
    public class SpecialPredictionService
    {
        private readonly AppDbContext _db;
        private readonly ApiFootballService _api;
        private readonly ILogger<SpecialPredictionService> _log;

        public SpecialPredictionService(AppDbContext db, ApiFootballService api,
            ILogger<SpecialPredictionService> log)
        { _db = db; _api = api; _log = log; }

        /// <summary>
        /// Actualiza los datos del partido desde la API (tarjetas, HT, penales, primer gol).
        /// Llamar después de que el partido termine (FT).
        /// </summary>
        public async Task SyncMatchDataAsync(int matchId)
        {
            var match = await _db.Matches.FindAsync(matchId);
            if (match == null || match.Status != "FT") return;

            var fixture = await _api.GetFixtureDetailAsync(match.ApiMatchId);
            if (fixture == null) return;

            // Marcador primer tiempo
            match.HtHomeScore = fixture.Score.Halftime.Home;
            match.HtAwayScore = fixture.Score.Halftime.Away;

            // Tarjetas
            match.YellowCards = fixture.Events?
                .Count(e => e.Type == "Card" && e.Detail == "Yellow Card") ?? 0;
            match.RedCards = fixture.Events?
                .Count(e => e.Type == "Card" &&
                    (e.Detail == "Red Card" || e.Detail == "Second Yellow card")) ?? 0;

            // Penal en tiempo reglamentario (no penaltis de desempate)
            match.HasPenalty = fixture.Events?
                .Any(e => e.Type == "Goal" && e.Detail == "Penalty" &&
                    e.Time.Elapsed <= 90) ?? false;

            // Primer equipo en anotar
            var firstGoal = fixture.Events?
                .Where(e => e.Type == "Goal" && e.Detail != "Missed Penalty")
                .OrderBy(e => e.Time.Elapsed)
                .ThenBy(e => e.Time.Extra ?? 0)
                .FirstOrDefault();

            if (firstGoal != null)
            {
                var homeTeamId = fixture.Teams.Home.Id;
                match.FirstScorer = firstGoal.Team.Id == homeTeamId ? 1 : 2;
            }

            // Ambos equipos marcaron
            match.BothScored = match.HomeScore > 0 && match.AwayScore > 0;

            await _db.SaveChangesAsync();
            _log.LogInformation(
                "Datos especiales sincronizados: HT {HH}-{HA}, Amarillas={Y}, Rojas={R}, Penal={P}, Primero={F}",
                match.HtHomeScore, match.HtAwayScore,
                match.YellowCards, match.RedCards,
                match.HasPenalty, match.FirstScorer);
        }

        /// <summary>
        /// Calcula todos los puntos especiales para un partido de Champions Final.
        /// </summary>
        public async Task<int> CalculateSpecialPointsAsync(int matchId, int userId)
        {
            var match = await _db.Matches.FindAsync(matchId);
            if (match == null || match.Status != "FT") return 0;

            var pred = await _db.Predictions
                .FirstOrDefaultAsync(p => p.MatchId == matchId && p.UserId == userId);
            if (pred == null) return 0;

            int pts = 0;

            // ── 5 pts: Resultado + marcador exacto ─────────────
            if (pred.HomeScorePred == match.HomeScore &&
                pred.AwayScorePred == match.AwayScore)
            {
                pts += 5;
                pred.ScoreCorrect = true;
                pred.ResultCorrect = true;
            }
            // ── 3 pts: Solo resultado ───────────────────────────
            else
            {
                var actual = ApiFootballService.GetResult(match.HomeScore, match.AwayScore);
                if (actual != null && pred.ResultPrediction == actual)
                {
                    pts += 3;
                    pred.ResultCorrect = true;
                }
                else pred.ResultCorrect = false;
                pred.ScoreCorrect = false;
            }

            // ── 2 pts: Resultado primer tiempo ──────────────────
            if (pred.HtHomePred.HasValue && pred.HtAwayPred.HasValue &&
                match.HtHomeScore.HasValue && match.HtAwayScore.HasValue)
            {
                var predHt  = ApiFootballService.GetResult(pred.HtHomePred, pred.HtAwayPred);
                var actualHt = ApiFootballService.GetResult(match.HtHomeScore, match.HtAwayScore);
                if (predHt != null && predHt == actualHt)
                    pts += 2;
            }

            // ── 2 pts: Cantidad exacta de tarjetas amarillas ─────
            if (pred.YellowCardsPred.HasValue &&
                pred.YellowCardsPred == match.YellowCards)
                pts += 2;

            // ── 2 pts: Cantidad exacta de tarjetas rojas ─────────
            if (pred.RedCardsPred.HasValue &&
                pred.RedCardsPred == match.RedCards)
                pts += 2;

            // ── 2 pts: Habrá penal en tiempo reglamentario ───────
            if (pred.HasPenaltyPred.HasValue &&
                pred.HasPenaltyPred == match.HasPenalty)
                pts += 2;

            // ── 1 pt: Primer equipo en anotar ────────────────────
            if (pred.FirstScorerPred.HasValue && match.FirstScorer.HasValue &&
                pred.FirstScorerPred == match.FirstScorer)
                pts += 1;

            // ── 1 pt: Ambos equipos marcan ───────────────────────
            if (pred.BothScoredPred.HasValue &&
                pred.BothScoredPred == match.BothScored)
                pts += 1;

            // ── 1 pt: Suma total de goles ─────────────────────────
            if (pred.TotalGoalsPred.HasValue &&
                match.HomeScore.HasValue && match.AwayScore.HasValue &&
                pred.TotalGoalsPred == (match.HomeScore + match.AwayScore))
                pts += 1;

            // ── 1 pt: Diferencia de goles ─────────────────────────
            if (pred.GoalDiffPred.HasValue &&
                match.HomeScore.HasValue && match.AwayScore.HasValue &&
                pred.GoalDiffPred == (match.HomeScore - match.AwayScore))
                pts += 1;

            pred.PointsEarned  = pts;
            pred.SpecialPoints = pts;
            await _db.SaveChangesAsync();

            _log.LogInformation("Usuario {U} — Final UCL — {P} puntos", userId, pts);
            return pts;
        }

        /// <summary>Calcula puntos para todos los participantes de la fase.</summary>
        public async Task CalculateAllAsync(int stageId)
        {
            var stage = await _db.Stages
                .Include(s => s.Matches)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return;

            foreach (var match in stage.Matches.Where(m => m.Status == "FT"))
            {
                // Sincronizar datos especiales desde API
                await SyncMatchDataAsync(match.Id);

                // Calcular para cada participante
                var userIds = await _db.Predictions
                    .Where(p => p.MatchId == match.Id)
                    .Select(p => p.UserId)
                    .Distinct().ToListAsync();

                int total = 0;
                foreach (var uid in userIds)
                {
                    var pts = await CalculateSpecialPointsAsync(match.Id, uid);
                    total += pts;

                    // Actualizar StageResult
                    var sr = await _db.StageResults
                        .FirstOrDefaultAsync(r => r.StageId == stageId && r.UserId == uid);
                    if (sr == null)
                        _db.StageResults.Add(new StageResult
                        { StageId = stageId, UserId = uid, Points = pts });
                    else
                        sr.Points = pts;

                    // Actualizar TournamentEntry
                    var te = await _db.TournamentEntries
                        .FirstOrDefaultAsync(e =>
                            e.UserId == uid && e.TournamentId == stage.TournamentId);
                    if (te != null) te.TotalPoints = pts;
                }
            }

            // Rankings
            var results = await _db.StageResults
                .Where(r => r.StageId == stageId)
                .OrderByDescending(r => r.Points).ToListAsync();
            for (int i = 0; i < results.Count; i++) results[i].Rank = i + 1;

            stage.Status = StageStatus.Finished;
            await _db.SaveChangesAsync();
            _log.LogInformation("Puntos especiales calculados para fase {S}", stage.Name);
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;
using QuinielaApp.Services;
using QuinielaApp.ViewModels;
using QuinielaApp.Helpers;

namespace QuinielaApp.Controllers
{
    // ════════════════════════════════════════════════════
    //  ACCOUNT
    // ════════════════════════════════════════════════════
    public class AccountController : Controller
    {
        private readonly AuthService _auth;
        private readonly AppDbContext _db;
        public AccountController(AuthService auth, AppDbContext db)
        { _auth = auth; _db = db; }

        [HttpGet] public IActionResult Login(string? returnUrl = null)
        { ViewBag.ReturnUrl = returnUrl; return View(); }

        [HttpPost] public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(vm);
            var (ok, msg, user) = await _auth.LoginAsync(vm);
            if (!ok) { ModelState.AddModelError("", msg); return View(vm); }
            await SignInAsync(user!);
            return LocalRedirect(returnUrl ?? "/");
        }

        [HttpGet] public IActionResult Register() => View();

        [HttpPost] public async Task<IActionResult> Register(RegisterVm vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var (ok, msg, user) = await _auth.RegisterAsync(vm);
            if (!ok) { ModelState.AddModelError("", msg); return View(vm); }
            await SignInAsync(user!);
            return RedirectToAction("Index", "Tournament");
        }

        [HttpPost, Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user != null)
            {
                // Invalidar tokens anteriores
                var oldTokens = await _db.PasswordResetTokens
                    .Where(t => t.UserId == user.Id && !t.Used)
                    .ToListAsync();
                foreach (var t in oldTokens) t.Used = true;

                // Crear token nuevo
                var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                _db.PasswordResetTokens.Add(new PasswordResetToken
                {
                    UserId    = user.Id,
                    Token     = token,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30)
                });
                await _db.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "Account", new { token }, Request.Scheme)!;

                var emailSvc = HttpContext.RequestServices.GetRequiredService<EmailService>();
                await emailSvc.SendPasswordResetAsync(user.Email, user.FullName, resetLink);
            }

            // Mismo mensaje siempre para no revelar si el email existe o no
            TempData["Info"] = "Si tu correo está registrado recibirás un enlace para restablecer tu contraseña.";
            return RedirectToAction("ForgotPassword");
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token)
        {
            var t = await _db.PasswordResetTokens
                .FirstOrDefaultAsync(x => x.Token == token && !x.Used && x.ExpiresAt > DateTime.UtcNow);

            if (t == null)
            {
                TempData["Error"] = "El enlace es inválido o ya expiró.";
                return RedirectToAction("Login");
            }
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                TempData["Error"] = "Las contraseñas no coinciden.";
                ViewBag.Token = token;
                return View();
            }

            var t = await _db.PasswordResetTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Token == token && !x.Used && x.ExpiresAt > DateTime.UtcNow);

            if (t == null)
            {
                TempData["Error"] = "El enlace es inválido o ya expiró.";
                return RedirectToAction("Login");
            }

            t.User.PasswordHash = newPassword;
            t.Used = true;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Contraseña actualizada. Ya puedes iniciar sesión.";
            return RedirectToAction("Login");
        }

        private async Task SignInAsync(User u)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
                new Claim(ClaimTypes.Name,           u.Username),
                new Claim(ClaimTypes.Email,          u.Email),
                new Claim(ClaimTypes.GivenName,      u.FullName),
                new Claim(ClaimTypes.Role,           u.Role)
            };
            var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(id),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
        }
    }

    // ════════════════════════════════════════════════════
    //  TOURNAMENT  (vista principal del jugador)
    // ════════════════════════════════════════════════════
    [Authorize]
    public class TournamentController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PaymentService _paymentSvc;
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public TournamentController(AppDbContext db, PaymentService paymentSvc)
        { _db = db; _paymentSvc = paymentSvc; }

        public async Task<IActionResult> Index()
        {
            var uid  = Uid;

            // Banner "partidos de hoy" — disponible a partir de las 11pm hora de El Salvador (UTC-6)
            var ahoraSv     = DateTime.UtcNow.AddHours(-6);
            var son11pm     = ahoraSv.Hour >= 23;
            var fechaBuscar = son11pm ? ahoraSv.Date : ahoraSv.Date.AddDays(-1);

            var tournaments = await _db.Tournaments
                .Include(t => t.Stages)
                .Include(t => t.Entries)
                .Where(t => t.Status == TournamentStatus.Active)
                .OrderByDescending(t => t.Id)
                .ToListAsync();

            var vm = new HomeVm
            {
                UserName = User.FindFirstValue(ClaimTypes.GivenName),
                Tournaments = new()
            };

            foreach (var t in tournaments)
            {
                var myEntry = t.Entries.FirstOrDefault(e => e.UserId == uid);
                var card    = new TournamentCardVm
                {
                    Id            = t.Id,
                    Name          = t.Name,
                    Description   = t.Description,
                    Status        = t.Status.ToString(),
                    LogoUrl       = t.LogoUrl,
                    TotalPlayers  = t.Entries.Count,
                    IsJoined      = myEntry != null,
                    MyTotalPoints = myEntry?.TotalPoints ?? 0,
                    ApiLeagueId   = t.ApiLeagueId
                };

                var paidTournament = await _db.Payments
                    .Include(p => p.Stage)
                    .AnyAsync(p =>
                        p.UserId == uid &&
                        p.Stage.TournamentId == t.Id &&
                        p.Status == PaymentStatus.Approved);

                var bolsaTotal = await _db.Payments
                    .Include(p => p.Stage)
                    .Where(p => p.Stage.TournamentId == t.Id
                             && p.Status == PaymentStatus.Approved)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0;
                card.BolsaTotal = bolsaTotal;
                card.Premio1    = Math.Round(bolsaTotal * 0.60m, 2);
                card.Premio2    = Math.Round(bolsaTotal * 0.30m, 2);
                card.Premio3    = Math.Round(bolsaTotal * 0.10m, 2);

                // Determinar líder de cada grupo de pago
                var groupLeaders = new HashSet<int>();
                foreach (var g in t.Stages
                    .Where(s => !string.IsNullOrEmpty(s.GroupKey))
                    .GroupBy(s => s.GroupKey))
                {
                    groupLeaders.Add(g.OrderBy(s => s.OrderNum).First().Id);
                }
                foreach (var s in t.Stages.Where(s => string.IsNullOrEmpty(s.GroupKey)))
                    groupLeaders.Add(s.Id);

                // Estado de pago por GroupKey
                var groupApprovedKeys = new HashSet<string>();
                var groupPendingKeys  = new HashSet<string>();
                foreach (var grp in t.Stages
                    .Where(s => !string.IsNullOrEmpty(s.GroupKey))
                    .GroupBy(s => s.GroupKey!))
                {
                    var ids = grp.Select(s => s.Id).ToList();
                    var hasApproved = await _db.Payments.AnyAsync(p =>
                        p.UserId == uid && ids.Contains(p.StageId) && p.Status == PaymentStatus.Approved);
                    if (hasApproved)
                        groupApprovedKeys.Add(grp.Key);
                    else if (await _db.Payments.AnyAsync(p =>
                        p.UserId == uid && ids.Contains(p.StageId) && p.Status == PaymentStatus.Pending))
                        groupPendingKeys.Add(grp.Key);
                }

                foreach (var s in t.Stages.OrderBy(s => s.OrderNum))
                {
                    var paid  = paidTournament;
                    var sr    = await _db.StageResults
                        .FirstOrDefaultAsync(r => r.UserId == uid && r.StageId == s.Id);
                    var total = await _db.StageEntries.CountAsync(e => e.StageId == s.Id);
                    var stageMatches = await _db.Matches
                        .Where(m => m.StageId == s.Id)
                        .Select(m => new { m.MatchDate, m.Status })
                        .ToListAsync();
                    var matchCount     = stageMatches.Count;
                    var nowStage       = DateTime.UtcNow;
                    var openMatches    = stageMatches.Count(m => m.Status == "NS" && nowStage < m.MatchDate.AddMinutes(-15));
                    var closedMatches  = stageMatches.Count(m => m.Status == "NS" && nowStage >= m.MatchDate.AddMinutes(-15));
                    var partidosDia    = stageMatches
                        .Where(m => m.Status == "FT" && m.MatchDate.AddHours(-6).Date == fechaBuscar)
                        .ToList();
                    var hasPending = !paid && await _paymentSvc.UserHasPendingPaymentAsync(uid, s.Id);

                    var hasGroupApproved = !string.IsNullOrEmpty(s.GroupKey) && groupApprovedKeys.Contains(s.GroupKey!);
                    var hasGroupPending  = !string.IsNullOrEmpty(s.GroupKey)
                        ? groupPendingKeys.Contains(s.GroupKey!)
                        : hasPending;

                    card.Stages.Add(new StageCardVm
                    {
                        Id                      = s.Id,
                        Name                    = s.Name,
                        Type                    = s.Type.ToString(),
                        Status                  = s.Status.ToString(),
                        EntryFee                = s.EntryFee,
                        Deadline                = s.PredictionDeadline,
                        TotalMatches            = matchCount,
                        OpenMatchesCount        = openMatches,
                        ClosedMatchesCount      = closedMatches,
                        IsPaid                  = paid,
                        HasPendingPayment       = hasPending,
                        IsGroupPaymentLeader    = groupLeaders.Contains(s.Id),
                        HasGroupApprovedPayment = hasGroupApproved,
                        HasGroupPendingPayment  = hasGroupPending,
                        GroupKey                = s.GroupKey,
                        BannerImagePath         = s.BannerImagePath,
                        CanPredict              = (paid || hasGroupApproved) && s.Status != StageStatus.Finished
                                                && openMatches > 0,
                        MyPoints                = sr?.Points ?? 0,
                        MyRank                  = sr?.Rank ?? 0,
                        TotalParticipants       = total,
                        HasPartidosHoy          = partidosDia.Any(),
                        PartidosHoyCount        = partidosDia.Count
                    });
                }
                vm.Tournaments.Add(card);
            }

            var totalPts = await _db.TournamentEntries
                .Where(e => e.UserId == uid)
                .SumAsync(e => (int?)e.TotalPoints) ?? 0;
            var bestRankRaw = await _db.StageResults
                .Where(r => r.UserId == uid)
                .MinAsync(r => (int?)r.Rank);

            ViewBag.UserName          = vm.UserName ?? "Participante";
            ViewBag.TotalPoints       = totalPts;
            ViewBag.BestRank          = bestRankRaw.HasValue ? $"#{bestRankRaw}" : "—";
            ViewBag.ActiveTournaments = await _db.TournamentEntries
                .CountAsync(e => e.UserId == uid);

            return View(vm);
        }
    }

    // ════════════════════════════════════════════════════
    //  PAYMENT
    // ════════════════════════════════════════════════════
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PaymentService _paymentSvc;
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public PaymentController(AppDbContext db, PaymentService paymentSvc)
        { _db = db; _paymentSvc = paymentSvc; }

        [HttpGet]
        public async Task<IActionResult> Pay(int stageId)
        {
            var stage = await _db.Stages
                .Include(s => s.Tournament).ThenInclude(t => t.Stages)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return NotFound();

            if (await _paymentSvc.UserPaidStageAsync(Uid, stageId))
                return RedirectToAction("Stage", "Prediction", new { stageId });

            // Si viene del redirect post-submit, mostrar pantalla de confirmación
            if (TempData["Info"] is string confirmMsg)
            {
                ViewBag.ConfirmMsg = confirmMsg;
                return View(new PaymentVm { StageId = stageId, StageName = stage.Name });
            }

            if (await _paymentSvc.UserHasPendingPaymentAsync(Uid, stageId))
            {
                TempData["Info"] = "Tu comprobante está en revisión. El administrador te dará acceso en breve.";
                return RedirectToAction("Index", "Tournament");
            }

            var relatedStages = string.IsNullOrEmpty(stage.GroupKey)
                ? new List<string> { stage.Name }
                : stage.Tournament.Stages
                    .Where(s => s.GroupKey == stage.GroupKey)
                    .OrderBy(s => s.OrderNum)
                    .Select(s => s.Name)
                    .ToList();

            return View(new PaymentVm
            {
                StageId           = stageId,
                StageName         = stage.Name,
                EntryFee          = stage.EntryFee,
                PaymentQrPath     = stage.Tournament.PaymentQrPath,
                PaymentInfo       = stage.Tournament.PaymentInfo,
                PaymentUrl        = stage.Tournament.PaymentUrl,
                RelatedStageNames = relatedStages
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(PaymentVm vm)
        {
            if (!ModelState.IsValid)
            {
                var stage = await _db.Stages
                    .Include(s => s.Tournament).ThenInclude(t => t.Stages)
                    .FirstOrDefaultAsync(s => s.Id == vm.StageId);
                vm.PaymentQrPath     = stage?.Tournament.PaymentQrPath;
                vm.PaymentInfo       = stage?.Tournament.PaymentInfo;
                vm.PaymentUrl        = stage?.Tournament.PaymentUrl;
                vm.RelatedStageNames = stage == null ? new() :
                    string.IsNullOrEmpty(stage.GroupKey)
                        ? new List<string> { stage.Name }
                        : stage.Tournament.Stages
                            .Where(s => s.GroupKey == stage.GroupKey)
                            .OrderBy(s => s.OrderNum).Select(s => s.Name).ToList();
                return View(vm);
            }
            var (ok, msg) = await _paymentSvc.SubmitAsync(Uid, vm);
            if (!ok) { ModelState.AddModelError("", msg); return View(vm); }
            TempData["Info"] = msg;
            return RedirectToAction("Pay", new { stageId = vm.StageId });
        }
    }

    // ════════════════════════════════════════════════════
    //  PREDICTION
    // ════════════════════════════════════════════════════
    [Authorize]
    public class PredictionController : Controller
    {
        private readonly AppDbContext _db;
        private readonly PredictionService _predSvc;
        private readonly PaymentService _paymentSvc;
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public PredictionController(AppDbContext db, PredictionService predSvc, PaymentService paymentSvc)
        { _db = db; _predSvc = predSvc; _paymentSvc = paymentSvc; }

        public async Task<IActionResult> Stage(int stageId)
        {
            var uid  = Uid;
            var stage = await _db.Stages.Include(s => s.Tournament)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return NotFound();

            bool paid = await _paymentSvc.UserPaidStageAsync(uid, stageId);
            if (!paid) return RedirectToAction("Pay", "Payment", new { stageId });

            var matches = await _db.Matches
                .Include(m => m.Predictions.Where(p => p.UserId == uid))
                .Where(m => m.StageId == stageId)
                .OrderBy(m => m.MatchDate)
                .ToListAsync();

            var now = DateTime.UtcNow;

            // Batch query: estadísticas del grupo para partidos cerrados
            var closedIds = matches
                .Where(m => now >= m.MatchDate.AddMinutes(-15))
                .Select(m => m.Id).ToList();

            var statsByMatch = new Dictionary<int, GroupStats>();
            if (closedIds.Any())
            {
                var predsFlat = await _db.Predictions
                    .Where(p => closedIds.Contains(p.MatchId))
                    .Select(p => new { p.MatchId, p.ResultPrediction })
                    .ToListAsync();

                foreach (var grp in predsFlat.GroupBy(p => p.MatchId))
                {
                    var tot = grp.Count();
                    statsByMatch[grp.Key] = new GroupStats
                    {
                        Total   = tot,
                        HomePct = (int)Math.Round(grp.Count(p => p.ResultPrediction == MatchResult.Home) * 100.0 / tot),
                        DrawPct = (int)Math.Round(grp.Count(p => p.ResultPrediction == MatchResult.Draw) * 100.0 / tot),
                        AwayPct = (int)Math.Round(grp.Count(p => p.ResultPrediction == MatchResult.Away) * 100.0 / tot),
                    };
                }
            }

            var cards = matches.Select(m => {
                var pred        = m.Predictions.FirstOrDefault();
                // MatchDate ya viene en UTC real (ver ApiFootballService.MapToMatch)
                var matchDlUtc  = m.MatchDate.AddMinutes(-15);
                var canPredThis = now < matchDlUtc;

                statsByMatch.TryGetValue(m.Id, out var groupStats);
                return new MatchPredVm
                {
                    Id            = m.Id, ApiMatchId = m.ApiMatchId,
                    HomeTeam      = m.HomeTeam, AwayTeam = m.AwayTeam,
                    HomeTeamLogo  = m.HomeTeamLogo, AwayTeamLogo = m.AwayTeamLogo,
                    MatchDate     = m.MatchDate,
                    MatchDeadline = matchDlUtc,
                    MatchDateSv   = TimeHelper.ToSvTime(m.MatchDate),
                    CanPredict    = canPredThis,
                    HomeScore = m.HomeScore, AwayScore = m.AwayScore,
                    Status = m.Status, Elapsed = m.Elapsed,
                    UserResult        = pred?.ResultPrediction.ToString(),
                    UserHomeScore     = pred?.HomeScorePred,
                    UserAwayScore     = pred?.AwayScorePred,
                    ResultCorrect     = pred?.ResultCorrect,
                    ScoreCorrect      = pred?.ScoreCorrect,
                    PointsEarned      = pred?.PointsEarned ?? 0,
                    SpecialPoints     = pred?.SpecialPoints ?? 0,
                    IsKnockout      = stage.Type != StageType.League && stage.Type != StageType.GroupStage,
                    UserQualifier   = pred?.QualifierPred,
                    UserPenaltyPred = pred?.PenaltyPred,
                    MatchQualifier  = m.Qualifier,
                    HasExtension    = m.ApiStatus == "AET" || m.ApiStatus == "PEN",
                    HuboPenaltis    = m.ApiStatus == "PEN",
                    GroupStats      = groupStats,
                    Special       = pred == null ? null : new SpecialPredVm
                    {
                        MatchId         = m.Id,
                        HomeTeam        = m.HomeTeam,
                        AwayTeam        = m.AwayTeam,
                        HomeTeamLogo    = m.HomeTeamLogo,
                        AwayTeamLogo    = m.AwayTeamLogo,
                        CanPredict      = canPredThis,
                        HtHomePred      = pred.HtHomePred,
                        HtAwayPred      = pred.HtAwayPred,
                        YellowCardsPred = pred.YellowCardsPred,
                        RedCardsPred    = pred.RedCardsPred,
                        HasPenaltyPred  = pred.HasPenaltyPred,
                        FirstScorerPred = pred.FirstScorerPred,
                        BothScoredPred  = pred.BothScoredPred,
                        TotalGoalsPred  = pred.TotalGoalsPred,
                        GoalDiffPred    = pred.GoalDiffPred,
                        HtHomeScore     = m.HtHomeScore,
                        HtAwayScore     = m.HtAwayScore,
                        YellowCards     = m.YellowCards,
                        RedCards        = m.RedCards,
                        HasPenalty      = m.HasPenalty,
                        FirstScorer     = m.FirstScorer,
                        BothScored      = m.BothScored,
                        TotalGoals      = m.HomeScore.HasValue ? m.HomeScore + m.AwayScore : null,
                        GoalDiff        = m.HomeScore.HasValue ? m.HomeScore - m.AwayScore : null,
                        SpecialPoints   = pred.SpecialPoints,
                        IsFinished      = m.Status == "FT"
                    }
                };
            }).ToList();

            ViewBag.IsMundial = stage.Tournament.ApiLeagueId == 1;
            ViewBag.IsUcl     = stage.Tournament.ApiLeagueId == 2;
            var isUclFinal    = stage.Tournament.ApiLeagueId == 2 && stage.Type == StageType.Final;

            // Comparación con el líder del torneo (motivacional) — solo lectura, no afecta puntos
            var entries = await _db.TournamentEntries
                .Include(e => e.User)
                .Where(e => e.TournamentId == stage.TournamentId)
                .OrderByDescending(e => e.TotalPoints)
                .ToListAsync();

            bool hasStandings = false;
            bool isLeader = false;
            int myPoints = 0, leaderPoints = 0;
            string leaderName = string.Empty;
            if (entries.Any())
            {
                hasStandings = true;
                var leader = entries.First();
                var mine   = entries.FirstOrDefault(e => e.UserId == uid);
                leaderPoints = leader.TotalPoints;
                leaderName   = leader.User.FullName;
                myPoints     = mine?.TotalPoints ?? 0;
                isLeader     = mine != null && mine.UserId == leader.UserId;
            }

            return View(isUclFinal ? "ChampionsFinal" : "Stage", new StageMatchesVm
            {
                StageId        = stageId,
                StageName      = stage.Name,
                TournamentId   = stage.TournamentId,
                TournamentName = stage.Tournament.Name,
                Deadline       = stage.PredictionDeadline,
                CanPredict     = stage.Status != StageStatus.Finished
                               && cards.Any(c => c.CanPredict),
                IsPaid         = paid,
                IsKnockout     = stage.Type != StageType.League && stage.Type != StageType.GroupStage,
                Matches        = cards,
                HasStandings   = hasStandings,
                IsLeader       = isLeader,
                MyPoints       = myPoints,
                LeaderPoints   = leaderPoints,
                LeaderName     = leaderName
            });
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SavePredictionVm vm)
        {
            var (ok, msg) = await _predSvc.SaveAsync(
                Uid, vm.MatchId, vm.ResultPred, vm.HomeScorePred, vm.AwayScorePred);
            return Json(new { ok, msg });
        }

        [HttpPost]
        public async Task<IActionResult> SaveQualifier([FromBody] SaveQualifierRequest req)
        {
            var uid  = Uid;
            var pred = await _db.Predictions
                .FirstOrDefaultAsync(p => p.UserId == uid && p.MatchId == req.MatchId);

            if (pred == null)
                return Json(new { ok = false, msg = "Registra primero tu predicción." });

            pred.QualifierPred = req.Qualifier;
            pred.UpdatedAt     = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> SavePenaltyPred([FromBody] SavePenaltyPredRequest req)
        {
            var uid  = Uid;
            var pred = await _db.Predictions
                .FirstOrDefaultAsync(p => p.UserId == uid && p.MatchId == req.MatchId);

            if (pred == null)
                return Json(new { ok = false, msg = "Registra primero tu predicción." });

            pred.PenaltyPred = req.PenaltyPred;
            pred.UpdatedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Json(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> SaveSpecial([FromBody] SpecialPredDto dto)
        {
            var uid   = Uid;
            var match = await _db.Matches.Include(m => m.Stage)
                .FirstOrDefaultAsync(m => m.Id == dto.MatchId);
            if (match == null) return Json(new { ok = false, msg = "Partido no encontrado." });
            if (DateTime.UtcNow >= match.MatchDate.AddMinutes(-15))
                return Json(new { ok = false, msg = "Las predicciones para este partido cerraron." });

            // Verificar que el usuario está inscrito
            var paid = await _db.Payments.Include(p => p.Stage)
                .AnyAsync(p => p.UserId == uid &&
                    p.Stage.TournamentId == match.Stage.TournamentId &&
                    p.Status == PaymentStatus.Approved);
            if (!paid) return Json(new { ok = false, msg = "Debes inscribirte primero." });

            // Obtener o crear predicción
            var pred = await _db.Predictions
                .FirstOrDefaultAsync(p => p.MatchId == dto.MatchId && p.UserId == uid);
            if (pred == null)
            {
                pred = new Prediction { UserId = uid, MatchId = dto.MatchId };
                _db.Predictions.Add(pred);
            }

            pred.HtHomePred      = dto.HtHomePred;
            pred.HtAwayPred      = dto.HtAwayPred;
            pred.YellowCardsPred = dto.YellowCardsPred;
            pred.RedCardsPred    = dto.RedCardsPred;
            pred.HasPenaltyPred  = dto.HasPenaltyPred;
            pred.FirstScorerPred = dto.FirstScorerPred;
            pred.BothScoredPred  = dto.BothScoredPred;
            pred.TotalGoalsPred  = dto.TotalGoalsPred;
            pred.GoalDiffPred    = dto.GoalDiffPred;
            pred.UpdatedAt       = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Json(new { ok = true, msg = "Predicciones especiales guardadas." });
        }
    }

    // ════════════════════════════════════════════════════
    //  QUALIFIER REQUEST — eliminatorias
    // ════════════════════════════════════════════════════
    public class SaveQualifierRequest
    {
        public int    MatchId   { get; set; }
        public string Qualifier { get; set; } = string.Empty;
    }

    public class SavePenaltyPredRequest
    {
        public int  MatchId     { get; set; }
        public bool PenaltyPred { get; set; }
    }

    // ════════════════════════════════════════════════════
    //  SPECIAL PREDICTION — DTO
    // ════════════════════════════════════════════════════
    public record SpecialPredDto(
        int   MatchId,
        int?  HtHomePred,
        int?  HtAwayPred,
        int?  YellowCardsPred,
        int?  RedCardsPred,
        bool? HasPenaltyPred,
        int?  FirstScorerPred,
        bool? BothScoredPred,
        int?  TotalGoalsPred,
        int?  GoalDiffPred
    );

    // ════════════════════════════════════════════════════
    //  LEADERBOARD
    // ════════════════════════════════════════════════════
    [Authorize]
    public class LeaderboardController : Controller
    {
        private readonly AppDbContext _db;
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public LeaderboardController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index(int tournamentId, int? stageId = null)
        {
            var uid = Uid;
            var tournament = await _db.Tournaments.Include(t => t.Stages.OrderBy(s => s.OrderNum))
                .FirstOrDefaultAsync(t => t.Id == tournamentId);
            if (tournament == null) return NotFound();
            ViewBag.IsMundial = tournament.ApiLeagueId == 1;

            // Mostrar botón de registro solo si no hay fases con deadline activo (o es admin)
            var hayFaseAbiertaLb = await _db.Stages.AnyAsync(s =>
                s.TournamentId == tournamentId &&
                s.Status != StageStatus.Finished &&
                s.PredictionDeadline > DateTime.UtcNow);
            ViewBag.LogVisible = !hayFaseAbiertaLb || User.IsInRole("Admin");

            var vm = new LeaderboardVm
            {
                TournamentId   = tournamentId,
                TournamentName = tournament.Name,
                CurrentUserId  = uid,
                Stages = tournament.Stages.Select(s => new StageTabVm
                { Id = s.Id, Name = s.Name, Active = s.Id == stageId }).ToList()
            };

            if (stageId.HasValue)
            {
                var stage = tournament.Stages.FirstOrDefault(s => s.Id == stageId);
                vm.StageId     = stageId;
                vm.StageName   = stage?.Name;
                vm.IsStageView = true;
                vm.IsPartial   = stage?.Status == StageStatus.InProgress;

                var results = await _db.StageResults
                    .Include(r => r.User)
                    .Where(r => r.StageId == stageId)
                    .OrderBy(r => r.Rank)
                    .ToListAsync();

                if (results.Any())
                {
                    vm.Rows = results.Select(r => new LeaderboardRowVm
                    {
                        Rank = r.Rank, UserId = r.UserId,
                        Username = r.User.Username, FullName = r.User.FullName,
                        TotalPoints = r.Points, ResultHits = r.ResultHits, ScoreHits = r.ScoreHits,
                        IsCurrentUser = r.UserId == uid
                    }).ToList();
                }
                else
                {
                    // Aún no terminó ningún partido — mostrar participantes inscritos con 0 pts
                    var enrolled = await _db.TournamentEntries
                        .Include(e => e.User)
                        .Where(e => e.TournamentId == tournamentId)
                        .OrderBy(e => e.User.FullName)
                        .ToListAsync();
                    vm.Rows = enrolled.Select((e, i) => new LeaderboardRowVm
                    {
                        Rank = i + 1, UserId = e.UserId,
                        Username = e.User.Username, FullName = e.User.FullName,
                        TotalPoints = 0,
                        IsCurrentUser = e.UserId == uid
                    }).ToList();
                }
            }
            else
            {
                vm.IsStageView = false;
                var entries = await _db.TournamentEntries
                    .Include(e => e.User)
                    .Where(e => e.TournamentId == tournamentId)
                    .OrderByDescending(e => e.TotalPoints)
                    .ToListAsync();

                vm.Rows = entries.Select((e, i) => new LeaderboardRowVm
                {
                    Rank = i + 1, UserId = e.UserId,
                    Username = e.User.Username, FullName = e.User.FullName,
                    TotalPoints = e.TotalPoints,
                    IsCurrentUser = e.UserId == uid
                }).ToList();
            }

            await FillStreakAndMovementAsync(vm, tournament, stageId);

            return View(vm);
        }

        // Calcula, para cada fila ya armada, la racha actual de aciertos consecutivos
        // y el movimiento de posición respecto a la última ronda de partidos finalizados.
        // Solo lectura sobre datos ya persistidos (PointsEarned, ResultCorrect) — no recalcula puntos.
        private async Task FillStreakAndMovementAsync(LeaderboardVm vm, Tournament tournament, int? stageId)
        {
            if (!vm.Rows.Any()) return;

            var scopeStageIds = stageId.HasValue
                ? new List<int> { stageId.Value }
                : tournament.Stages.Select(s => s.Id).ToList();

            var scopeMatches = await _db.Matches
                .Where(m => scopeStageIds.Contains(m.StageId))
                .Select(m => new { m.Id, m.MatchDate, m.Status })
                .ToListAsync();

            var finishedMatches = scopeMatches.Where(m => m.Status == "FT").ToList();
            if (!finishedMatches.Any()) return;

            var finishedMatchIds = finishedMatches.Select(m => m.Id).ToHashSet();
            var matchDateById    = finishedMatches.ToDictionary(m => m.Id, m => m.MatchDate);
            var lastRoundDate     = finishedMatches.Max(m => m.MatchDate);
            var lastRoundMatchIds = finishedMatches
                .Where(m => m.MatchDate == lastRoundDate)
                .Select(m => m.Id).ToHashSet();

            var scopePreds = await _db.Predictions
                .Where(p => finishedMatchIds.Contains(p.MatchId))
                .Select(p => new { p.UserId, p.MatchId, p.PointsEarned, p.ResultCorrect })
                .ToListAsync();

            var streakByUser        = new Dictionary<int, int>();
            var lastRoundPtsByUser  = new Dictionary<int, int>();
            foreach (var g in scopePreds.GroupBy(p => p.UserId))
            {
                var ordered = g.OrderBy(p => matchDateById[p.MatchId]).ToList();
                int running = 0;
                foreach (var p in ordered)
                    running = p.ResultCorrect == true ? running + 1 : 0;
                streakByUser[g.Key] = running;

                lastRoundPtsByUser[g.Key] = ordered
                    .Where(p => lastRoundMatchIds.Contains(p.MatchId))
                    .Sum(p => p.PointsEarned);
            }

            var prevRankByUser = vm.Rows
                .Select(r => new { r.UserId, PrevPoints = r.TotalPoints - lastRoundPtsByUser.GetValueOrDefault(r.UserId, 0) })
                .OrderByDescending(x => x.PrevPoints)
                .Select((x, i) => new { x.UserId, Rank = i + 1 })
                .ToDictionary(x => x.UserId, x => x.Rank);

            foreach (var r in vm.Rows)
            {
                r.CurrentStreak = streakByUser.GetValueOrDefault(r.UserId, 0);
                if (prevRankByUser.TryGetValue(r.UserId, out var prevRank))
                {
                    r.HasMovementData = true;
                    r.RankMovement    = prevRank - r.Rank;
                }
            }
        }
    }

    // ════════════════════════════════════════════════════
    //  HISTORY — Registro de predicciones (log/auditoría)
    // ════════════════════════════════════════════════════
    [Authorize]
    public class HistoryController : Controller
    {
        private readonly AppDbContext _db;
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public HistoryController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Log(int tournamentId, int? userId = null, int? matchId = null, int? stageId = null)
        {
            var tournament = await _db.Tournaments.FindAsync(tournamentId);
            if (tournament == null) return NotFound();

            // Historial acumulativo y progresivo: un partido entra al registro
            // cuando termina (FT) y ya pasaron las 11pm hora SV del día en que
            // se jugó. Antes de eso simplemente no aparece — no hace falta
            // bloquear la página completa.
            var ahoraSv = DateTime.UtcNow.AddHours(-6);

            // Base query: predicciones de partidos visibles del torneo
            var query = _db.Predictions
                .Include(p => p.User)
                .Include(p => p.Match).ThenInclude(m => m.Stage)
                .Where(p =>
                    p.Match.Stage.TournamentId == tournamentId &&
                    p.Match.Status == "FT" &&
                    (
                        p.Match.MatchDate.AddHours(-6).Date < ahoraSv.Date
                        ||
                        (p.Match.MatchDate.AddHours(-6).Date == ahoraSv.Date
                         && ahoraSv.Hour >= 23)
                    ))
                .AsQueryable();

            // Aplicar filtros
            if (userId.HasValue)   query = query.Where(p => p.UserId  == userId.Value);
            if (matchId.HasValue)  query = query.Where(p => p.MatchId == matchId.Value);
            if (stageId.HasValue)  query = query.Where(p => p.Match.StageId == stageId.Value);

            var predictions = await query
                .OrderBy(p => p.Match.MatchDate)
                .ThenByDescending(p => p.PointsEarned)
                .ToListAsync();

            // Partidos que entran en el filtro actual (para mensajes de estado vacío)
            var matchesEnFiltroQuery = _db.Matches
                .Where(m => m.Stage.TournamentId == tournamentId)
                .AsQueryable();
            if (matchId.HasValue) matchesEnFiltroQuery = matchesEnFiltroQuery.Where(m => m.Id == matchId.Value);
            if (stageId.HasValue) matchesEnFiltroQuery = matchesEnFiltroQuery.Where(m => m.StageId == stageId.Value);

            var matchesEnFiltro = await matchesEnFiltroQuery
                .Select(m => new { m.MatchDate, m.Status })
                .ToListAsync();

            var tienePartidosFuturos = matchesEnFiltro
                .Any(m => m.MatchDate.AddHours(-6) > ahoraSv && m.Status == "NS");

            var tienePartidosHoyNoVisibles = matchesEnFiltro
                .Any(m => m.Status == "FT" &&
                       m.MatchDate.AddHours(-6).Date == ahoraSv.Date
                       && ahoraSv.Hour < 23);

            ViewBag.TienePartidosFuturos       = tienePartidosFuturos;
            ViewBag.TienePartidosHoyNoVisibles = tienePartidosHoyNoVisibles;

            // Usuarios del torneo para el dropdown
            var users = await _db.TournamentEntries
                .Include(e => e.User)
                .Where(e => e.TournamentId == tournamentId)
                .Select(e => new LogUserOptionVm
                {
                    UserId   = e.UserId,
                    Username = e.User.Username,
                    FullName = e.User.FullName
                })
                .OrderBy(u => u.FullName)
                .ToListAsync();

            // Partidos del torneo para el dropdown
            var matches = await _db.Matches
                .Include(m => m.Stage)
                .Where(m => m.Stage.TournamentId == tournamentId)
                .OrderBy(m => m.Stage.OrderNum).ThenBy(m => m.MatchDate)
                .Select(m => new LogMatchOptionVm
                {
                    MatchId = m.Id,
                    Label   = m.HomeTeam + " vs " + m.AwayTeam + " (" + m.Stage.Name + ")"
                })
                .ToListAsync();

            // Nombre del filtro activo
            var filterUserName  = userId.HasValue
                ? users.FirstOrDefault(u => u.UserId == userId)?.FullName ?? ""
                : string.Empty;
            var filterMatchName = matchId.HasValue
                ? matches.FirstOrDefault(m => m.MatchId == matchId)?.Label ?? ""
                : string.Empty;

            // Estadísticas personales — independientes del filtro de partido,
            // siempre sobre todas las predicciones finalizadas del usuario en el torneo
            UserStatsVm? stats = null;
            if (userId.HasValue)
            {
                var userPreds = await _db.Predictions
                    .Include(p => p.Match).ThenInclude(m => m.Stage)
                    .Where(p => p.UserId == userId.Value &&
                                p.Match.Stage.TournamentId == tournamentId &&
                                p.Match.Status == "FT")
                    .OrderBy(p => p.Match.MatchDate)
                    .ToListAsync();

                if (userPreds.Any())
                {
                    int best = 0, running = 0;
                    foreach (var p in userPreds)
                    {
                        if (p.ResultCorrect == true) { running++; if (running > best) best = running; }
                        else running = 0;
                    }

                    var bestMatch = userPreds.OrderByDescending(p => p.PointsEarned).First();

                    stats = new UserStatsVm
                    {
                        TotalFinished = userPreds.Count,
                        ResultHits    = userPreds.Count(p => p.ResultCorrect == true),
                        ScoreHits     = userPreds.Count(p => p.ScoreCorrect  == true),
                        CurrentStreak = running,
                        BestStreak    = best
                    };

                    if (bestMatch.PointsEarned > 0)
                    {
                        stats.BestMatchHomeTeam = bestMatch.Match.HomeTeam;
                        stats.BestMatchAwayTeam = bestMatch.Match.AwayTeam;
                        stats.BestMatchStage    = bestMatch.Match.Stage.Name;
                        stats.BestMatchPoints   = bestMatch.PointsEarned;
                    }
                }
            }

            var vm = new PredictionLogVm
            {
                TournamentId    = tournamentId,
                TournamentName  = tournament.Name,
                FilterUserId    = userId,
                FilterMatchId   = matchId,
                FilterUserName  = filterUserName,
                FilterMatchName = filterMatchName,
                Users           = users,
                Matches         = matches,
                TotalRows       = predictions.Count,
                Stats           = stats,
                Rows = predictions.Select(p => new PredictionLogRowVm
                {
                    PredictionId  = p.Id,
                    UserId        = p.UserId,
                    Username      = p.User.Username,
                    FullName      = p.User.FullName,
                    MatchId       = p.MatchId,
                    HomeTeam      = p.Match.HomeTeam,
                    AwayTeam      = p.Match.AwayTeam,
                    HomeTeamLogo  = p.Match.HomeTeamLogo,
                    AwayTeamLogo  = p.Match.AwayTeamLogo,
                    StageName     = p.Match.Stage.Name,
                    MatchDate     = p.Match.MatchDate,
                    HomeScore     = p.Match.HomeScore,
                    AwayScore     = p.Match.AwayScore,
                    MatchStatus   = p.Match.Status,
                    ResultPred    = p.ResultPrediction.ToString(),
                    HomePred      = p.HomeScorePred,
                    AwayPred      = p.AwayScorePred,
                    ResultCorrect = p.ResultCorrect,
                    ScoreCorrect  = p.ScoreCorrect,
                    PointsEarned  = p.PointsEarned,
                    UpdatedAt     = p.UpdatedAt,
                    ApiStatus     = p.Match.ApiStatus,
                    Qualifier     = p.Match.Qualifier,
                    IsKnockout    = p.Match.Stage.Type != StageType.League &&
                                     p.Match.Stage.Type != StageType.GroupStage
                }).ToList()
            };

            return View(vm);
        }
    }

    // ════════════════════════════════════════════════════
    //  ADMIN
    // ════════════════════════════════════════════════════
    [Authorize(Roles = "Admin")]
    [Route("Admin/[action]/{id?}")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly TournamentService _tourSvc;
        private readonly MatchSyncService _syncSvc;
        private readonly PredictionService _predSvc;
        private readonly PaymentService _paymentSvc;
        private readonly ApiFootballService _api;
        private readonly IWebHostEnvironment _env;

        public AdminController(AppDbContext db, TournamentService tourSvc,
            MatchSyncService syncSvc, PredictionService predSvc, PaymentService paymentSvc,
            ApiFootballService api, IWebHostEnvironment env)
        { _db = db; _tourSvc = tourSvc; _syncSvc = syncSvc; _predSvc = predSvc;
          _paymentSvc = paymentSvc; _api = api; _env = env; }

        // Dashboard
        public async Task<IActionResult> Index()
        {
            ViewBag.AdminBolsa = await _db.Payments
                .Where(p => p.Status == PaymentStatus.Approved)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            return View(new AdminDashboardVm
            {
                TotalUsers        = await _db.Users.CountAsync(u => u.Role == "Player"),
                ActiveTournaments = await _db.Tournaments.CountAsync(t => t.Status == TournamentStatus.Active),
                TotalPredictions  = await _db.Predictions.CountAsync(),
                PendingPayments   = await _db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending),
                Tournaments = await _db.Tournaments.Include(t => t.Stages)
                    .OrderByDescending(t => t.Id).Take(10).ToListAsync(),
                RecentPayments = await _db.Payments.Include(p => p.User).Include(p => p.Stage)
                    .OrderByDescending(p => p.CreatedAt).Take(20).ToListAsync()
            });
        }

        // ── Torneo ────────────────────────────────────────
        [HttpGet] public IActionResult CreateTournament() => View(new CreateTournamentVm());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTournament(CreateTournamentVm vm)
        {
            if (!ModelState.IsValid) return View(vm);
            await _tourSvc.CreateAsync(vm);
            TempData["Success"] = "Torneo creado correctamente.";
            return RedirectToAction("Index");
        }

        // ── Fase ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> CreateStage(int id) // id = tournamentId
        {
            var t = await _db.Tournaments.FindAsync(id);
            if (t == null) return NotFound();
            var rounds = await _syncSvc.GetRoundsAsync(t.ApiLeagueId, t.ApiSeason);
            return View(new CreateStageVm
            {
                TournamentId     = id,
                TournamentName   = t.Name,
                AvailableRounds  = rounds,
                PredictionDeadline = DateTime.Today.AddDays(3),
                EntryFee         = 20m
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStage(CreateStageVm vm)
        {
            if (!ModelState.IsValid)
            {
                var t = await _db.Tournaments.FindAsync(vm.TournamentId);
                vm.AvailableRounds = await _syncSvc.GetRoundsAsync(t!.ApiLeagueId, t.ApiSeason);
                vm.TournamentName  = t.Name;
                return View(vm);
            }
            Enum.TryParse<StageType>(vm.Type, out var stageType);
            var stage = new Stage
            {
                TournamentId        = vm.TournamentId,
                Name                = vm.Name,
                ApiRound            = vm.ApiRound,
                Type                = stageType,
                EntryFee            = vm.EntryFee,
                PredictionDeadline  = TimeHelper.ToUtc(vm.PredictionDeadline),
                OrderNum            = vm.OrderNum,
                GroupKey            = string.IsNullOrWhiteSpace(vm.GroupKey) ? null : vm.GroupKey.Trim(),
                Status              = StageStatus.Open
            };
            if (vm.BannerFile != null) stage.BannerImagePath = await SaveFileAsync(vm.BannerFile, "banners");
            _db.Stages.Add(stage);
            await _db.SaveChangesAsync();

            // Sincronizar partidos automáticamente
            await _syncSvc.SyncAsync(stage.Id);
            TempData["Success"] = $"Fase '{stage.Name}' creada y partidos cargados.";
            return RedirectToAction("Index");
        }

        // ── Sincronizar partidos manualmente ──────────────
        [HttpPost]
        public async Task<IActionResult> SyncMatches(int id) // id = stageId
        {
            var (ok, msg, _) = await _syncSvc.SyncAsync(id);
            if (ok)
            {
                await _predSvc.CalculatePendingPointsAsync(id);
                TempData["Success"] = "Sincronización y puntos actualizados.";
            }
            else
            {
                TempData["Error"] = msg;
            }
            return RedirectToAction("Index");
        }

        // ── Calcular puntos ───────────────────────────────
        // Idempotente: limpia los resultados de la fase y resetea las
        // predicciones de los partidos finalizados antes de recalcular,
        // así se puede presionar varias veces sin duplicar TotalPoints.
        [HttpPost]
        public async Task<IActionResult> CalculatePoints(int id) // id = stageId
        {
            // 1. Limpiar StageResults de esta fase
            var oldResults = await _db.StageResults
                .Where(r => r.StageId == id)
                .ToListAsync();
            _db.StageResults.RemoveRange(oldResults);

            // 2. Resetear predicciones de partidos finalizados de esta fase
            var matchIds = await _db.Matches
                .Where(m => m.StageId == id && m.Status == "FT")
                .Select(m => m.Id)
                .ToListAsync();
            var predsToReset = await _db.Predictions
                .Where(p => matchIds.Contains(p.MatchId))
                .ToListAsync();
            foreach (var p in predsToReset)
            {
                p.PointsEarned  = 0;
                p.ResultCorrect = null;
                p.ScoreCorrect  = null;
            }

            // Permitir recalcular aunque la fase ya esté marcada como Finished
            var stage = await _db.Stages.FindAsync(id);
            if (stage != null && stage.Status == StageStatus.Finished)
                stage.Status = StageStatus.InProgress;

            await _db.SaveChangesAsync();

            // 3. Recalcular limpio
            await _predSvc.CalculateStagePointsAsync(id);

            TempData["Success"] = "Puntos calculados correctamente.";
            return RedirectToAction("Index");
        }

        // ── Recalcular predicciones pendientes ────────────
        // Busca partidos FT de la fase cuyas predicciones quedaron sin
        // calcular (ResultCorrect == null) y las recalcula, sin tocar
        // las predicciones de partidos ya calculados.
        [HttpPost]
        public async Task<IActionResult> RecalcularPendientes(int id) // id = stageId
        {
            var count = await _predSvc.CalculatePendingPointsAsync(id);
            if (count > 0)
                TempData["Success"] = $"Calculadas predicciones pendientes de {count} partido(s).";
            else
                TempData["Info"] = "No hay predicciones pendientes de calcular.";
            return RedirectToAction("Index");
        }

        // ── Recálculo masivo de puntos (corrección histórica) ────────────────
        // Recorre TODAS las fases de un torneo, recalcula PointsEarned,
        // ResultCorrect, ScoreCorrect de cada predicción usando PointsCalculator,
        // luego recalcula StageResults y TournamentEntries desde cero.
        [HttpPost]
        public async Task<IActionResult> RecalcularTodo(int id) // id = tournamentId
        {
            var stages = await _db.Stages
                .Include(s => s.Matches)
                .Where(s => s.TournamentId == id)
                .ToListAsync();

            if (!stages.Any())
            {
                TempData["Error"] = "No se encontraron fases para este torneo.";
                return RedirectToAction("Index");
            }

            int totalPrediciones   = 0;
            int predCorregidas     = 0;
            int etapasRecalculadas = 0;

            foreach (var stage in stages)
            {
                var ftMatches = stage.Matches
                    .Where(m => m.Status == "FT" && m.HomeScore != null && m.AwayScore != null)
                    .ToList();
                if (!ftMatches.Any()) continue;

                // Permitir recalcular aunque la fase ya esté marcada Finished
                var prevStatus = stage.Status;
                if (stage.Status == StageStatus.Finished)
                    stage.Status = StageStatus.InProgress;

                // Limpiar StageResults de esta fase para que CalculateStagePointsAsync los recree
                var oldResults = await _db.StageResults
                    .Where(r => r.StageId == stage.Id)
                    .ToListAsync();
                _db.StageResults.RemoveRange(oldResults);

                // Resetear predicciones de partidos FT de esta fase
                var stageMatchIds = ftMatches.Select(m => m.Id).ToList();
                var predsToReset  = await _db.Predictions
                    .Where(p => stageMatchIds.Contains(p.MatchId))
                    .ToListAsync();

                bool isKnockout = stage.Type != StageType.League && stage.Type != StageType.GroupStage;
                totalPrediciones += predsToReset.Count;

                foreach (var pred in predsToReset)
                {
                    var match = ftMatches.First(m => m.Id == pred.MatchId);
                    var antes = pred.PointsEarned;

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

                    if (pred.PointsEarned != r.Points ||
                        pred.ResultCorrect != r.ResultCorrect ||
                        pred.ScoreCorrect  != r.ScoreCorrect)
                        predCorregidas++;

                    pred.PointsEarned  = r.Points;
                    pred.ResultCorrect = r.ResultCorrect;
                    pred.ScoreCorrect  = r.ScoreCorrect;
                }

                // Marcar todos los partidos FT como recalculados ahora
                var recalcStamp = DateTime.UtcNow;
                foreach (var m in ftMatches)
                    m.PointsCalculatedAt = recalcStamp;

                await _db.SaveChangesAsync();

                // Recalcular StageResults y TournamentEntries usando el servicio
                stage.Status = StageStatus.InProgress; // permite que CalculateStagePointsAsync entre
                await _db.SaveChangesAsync();
                await _predSvc.CalculateStagePointsAsync(stage.Id);

                etapasRecalculadas++;
            }

            // Recalcular TotalPoints de TournamentEntries desde cero
            var allStageIds = stages.Select(s => s.Id).ToList();
            var entries = await _db.TournamentEntries
                .Where(e => e.TournamentId == id)
                .ToListAsync();

            foreach (var entry in entries)
            {
                entry.TotalPoints = await _db.StageResults
                    .Where(r => r.UserId == entry.UserId && allStageIds.Contains(r.StageId))
                    .SumAsync(r => (int?)r.Points) ?? 0;
            }
            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"Recálculo completo: {etapasRecalculadas} fase(s), " +
                $"{totalPrediciones} predicciones revisadas, " +
                $"{predCorregidas} corregidas.";
            return RedirectToAction("Index");
        }

        // ── Ver pagos ─────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Payments(int? stageId = null, string? status = null)
        {
            var stages = await _db.Stages
                .Include(s => s.Tournament)
                .OrderBy(s => s.TournamentId)
                .ThenBy(s => s.OrderNum)
                .Select(s => new StageFilterVm
                {
                    Id             = s.Id,
                    Name           = s.Name,
                    TournamentName = s.Tournament.Name,
                    Type           = s.Type.ToString()
                })
                .ToListAsync();

            var query = _db.Payments
                .Include(p => p.User)
                .Include(p => p.Stage).ThenInclude(s => s.Tournament)
                .AsQueryable();

            if (stageId.HasValue)
                query = query.Where(p => p.StageId == stageId);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PaymentStatus>(status, out var statusEnum))
                query = query.Where(p => p.Status == statusEnum);

            var payments = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

            ViewBag.Stages         = stages;
            ViewBag.SelectedStage  = stageId;
            ViewBag.SelectedStatus = status;
            ViewBag.TotalPagos     = payments.Count;
            ViewBag.Pendientes     = payments.Count(p => p.Status == PaymentStatus.Pending);
            ViewBag.Aprobados      = payments.Count(p => p.Status == PaymentStatus.Approved);
            ViewBag.BolsaFiltrada  = payments
                .Where(p => p.Status == PaymentStatus.Approved)
                .Sum(p => p.Amount);

            return View(payments);
        }

        // ── Aprobar pago ──────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePayment(int id)
        {
            var (ok, msg) = await _paymentSvc.ApproveAsync(id);
            TempData[ok ? "Success" : "Error"] = msg;
            return RedirectToAction("Payments");
        }

        // ── Rechazar pago ─────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPayment(int id)
        {
            var (ok, msg) = await _paymentSvc.RejectAsync(id);
            TempData[ok ? "Success" : "Error"] = msg;
            return RedirectToAction("Payments");
        }

        // ── Editar torneo ─────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditTournament(int id)
        {
            var t = await _db.Tournaments.FindAsync(id);
            if (t == null) return NotFound();
            return View(new EditTournamentVm
            {
                Id = t.Id, Name = t.Name, Description = t.Description,
                PaymentInfo = t.PaymentInfo ?? string.Empty,
                PaymentUrl  = t.PaymentUrl,
                ExistingLogo = t.LogoUrl, ExistingQr = t.PaymentQrPath,
                Status = t.Status.ToString()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTournament(EditTournamentVm vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var t = await _db.Tournaments.FindAsync(vm.Id);
            if (t == null) return NotFound();

            t.Name = vm.Name;
            t.Description = vm.Description;
            t.PaymentInfo = vm.PaymentInfo;
            if (Enum.TryParse<TournamentStatus>(vm.Status, out var st)) t.Status = st;

            if (vm.LogoFile != null) t.LogoUrl      = await SaveFileAsync(vm.LogoFile, "logos");
            if (vm.QrFile   != null) t.PaymentQrPath = await SaveFileAsync(vm.QrFile,   "qr");

            await _db.SaveChangesAsync();
            TempData["Success"] = "Torneo actualizado.";
            return RedirectToAction("Index");
        }

        // ── Eliminar torneo ───────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTournament(int id)
        {
            var t = await _db.Tournaments.Include(x => x.Stages).ThenInclude(s => s.Matches)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();

            // Borrar en cascada manualmente (Payments tiene restricción)
            var stageIds  = t.Stages.Select(s => s.Id).ToList();
            var matchIds  = t.Stages.SelectMany(s => s.Matches).Select(m => m.Id).ToList();

            await _db.Predictions.Where(p => matchIds.Contains(p.MatchId)).ExecuteDeleteAsync();
            await _db.Payments.Where(p => stageIds.Contains(p.StageId)).ExecuteDeleteAsync();
            await _db.StageResults.Where(r => stageIds.Contains(r.StageId)).ExecuteDeleteAsync();
            await _db.StageEntries.Where(e => stageIds.Contains(e.StageId)).ExecuteDeleteAsync();
            await _db.TournamentEntries.Where(e => e.TournamentId == id).ExecuteDeleteAsync();
            _db.Tournaments.Remove(t);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Torneo {t.Name} eliminado correctamente.";
            return RedirectToAction("Index");
        }

        // ── Editar fase ───────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditStage(int id)
        {
            var s = await _db.Stages.Include(x => x.Tournament).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();
            return View(new EditStageVm
            {
                Id = s.Id, TournamentId = s.TournamentId, TournamentName = s.Tournament.Name,
                Name = s.Name, Type = s.Type.ToString(), EntryFee = s.EntryFee,
                PredictionDeadline = TimeHelper.ToSvTime(s.PredictionDeadline),
                OrderNum = s.OrderNum, Status = s.Status.ToString(),
                GroupKey = s.GroupKey,
                ExistingBanner = s.BannerImagePath
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStage(EditStageVm vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var s = await _db.Stages.FindAsync(vm.Id);
            if (s == null) return NotFound();

            s.Name = vm.Name;
            s.EntryFee = vm.EntryFee;
            s.PredictionDeadline = TimeHelper.ToUtc(vm.PredictionDeadline);
            s.OrderNum = vm.OrderNum;
            s.GroupKey = string.IsNullOrWhiteSpace(vm.GroupKey) ? null : vm.GroupKey.Trim();
            if (Enum.TryParse<StageType>(vm.Type, out var st))     s.Type   = st;
            if (Enum.TryParse<StageStatus>(vm.Status, out var ss)) s.Status = ss;
            if (vm.BannerFile != null) s.BannerImagePath = await SaveFileAsync(vm.BannerFile, "banners");

            await _db.SaveChangesAsync();
            TempData["Success"] = "Fase actualizada.";
            return RedirectToAction("Index");
        }

        // ── Eliminar fase ─────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStage(int id)
        {
            var s = await _db.Stages.Include(x => x.Matches).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound();

            var matchIds = s.Matches.Select(m => m.Id).ToList();
            await _db.Predictions.Where(p => matchIds.Contains(p.MatchId)).ExecuteDeleteAsync();
            await _db.Payments.Where(p => p.StageId == id).ExecuteDeleteAsync();
            await _db.StageResults.Where(r => r.StageId == id).ExecuteDeleteAsync();
            await _db.StageEntries.Where(e => e.StageId == id).ExecuteDeleteAsync();
            _db.Stages.Remove(s);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Fase {s.Name} eliminada correctamente.";
            return RedirectToAction("Index");
        }

        // ── Historial de usuarios ─────────────────────────
        [HttpGet]
        public async Task<IActionResult> Usuarios()
        {
            var usuarios = await _db.Users
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new UsuarioAdminVm
                {
                    Id        = u.Id,
                    FullName  = u.FullName,
                    Username  = u.Username,
                    Email     = u.Email,
                    Role      = u.Role,
                    CreatedAt = u.CreatedAt,
                    Torneos   = _db.TournamentEntries
                                    .Count(te => te.UserId == u.Id),
                    PagoTotal = _db.Payments
                                    .Where(p => p.UserId == u.Id
                                        && p.Status == PaymentStatus.Approved)
                                    .Sum(p => (decimal?)p.Amount) ?? 0
                })
                .ToListAsync();

            ViewBag.Total   = usuarios.Count;
            ViewBag.Players = usuarios.Count(u => u.Role == "Player");
            ViewBag.Admins  = usuarios.Count(u => u.Role == "Admin");
            ViewBag.Bolsa   = usuarios.Sum(u => u.PagoTotal);

            return View(usuarios);
        }

        // ── Helper guardar archivo ────────────────────────
        private async Task<string> SaveFileAsync(IFormFile file, string folder)
        {
            var dir  = Path.Combine(_env.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            await using var fs = new FileStream(Path.Combine(dir, name), FileMode.Create);
            await file.CopyToAsync(fs);
            return $"/uploads/{folder}/{name}";
        }

        // ── Test de API (temporal) ────────────────────────
        [HttpGet]
        public async Task<IActionResult> TestApi(int tournamentId)
        {
            var t = await _db.Tournaments.FindAsync(tournamentId);
            if (t == null) return Content("Torneo no encontrado");
            var rounds = await _syncSvc.GetRoundsAsync(t.ApiLeagueId, t.ApiSeason);
            return Content($"League:{t.ApiLeagueId} Season:{t.ApiSeason} Rounds:{rounds.Count} -> {string.Join(", ", rounds)}");
        }

        // ── Stats en vivo (API) ───────────────────────────
        [HttpGet]
        public async Task<IActionResult> MatchStats(int fixtureId)
        {
            var fixture = await _api.GetFixtureDetailAsync(fixtureId);
            if (fixture == null) return Json(new { ok = false });

            var homeStats = fixture.Statistics?
                .FirstOrDefault(s => s.Team.Id == fixture.Teams.Home.Id)?.Statistics;
            var awayStats = fixture.Statistics?
                .FirstOrDefault(s => s.Team.Id == fixture.Teams.Away.Id)?.Statistics;

            static object S(List<ApiStatItem>? st) => new {
                shotsTotal      = ApiFootballService.GetStat(st, "Total Shots"),
                shotsOnGoal     = ApiFootballService.GetStat(st, "Shots on Goal"),
                possession      = ApiFootballService.GetStat(st, "Ball Possession"),
                passesTotal     = ApiFootballService.GetStat(st, "Total passes"),
                passAccuracy    = ApiFootballService.GetStat(st, "Passes %"),
                fouls           = ApiFootballService.GetStat(st, "Fouls"),
                corners         = ApiFootballService.GetStat(st, "Corner Kicks"),
                offsides        = ApiFootballService.GetStat(st, "Offsides"),
                yellowCards     = ApiFootballService.GetStat(st, "Yellow Cards"),
                redCards        = ApiFootballService.GetStat(st, "Red Cards"),
                saves           = ApiFootballService.GetStat(st, "Goalkeeper Saves"),
                expectedGoals   = ApiFootballService.GetStat(st, "expected_goals"),
            };

            return Json(new {
                ok        = true,
                homeScore = fixture.Goals.Home,
                awayScore = fixture.Goals.Away,
                status    = fixture.Fixture.Status.Short,
                elapsed   = fixture.Fixture.Status.Elapsed,
                venue     = fixture.Fixture.Venue?.Name,
                events    = fixture.Events?.Where(e => e.Type != "subst").Select(e => new {
                    min    = e.Time.Elapsed,
                    extra  = e.Time.Extra,
                    team   = e.Team.Name,
                    player = e.Player.Name,
                    assist = e.Assist?.Name,
                    type   = e.Type,
                    detail = e.Detail
                }),
                homeStats = S(homeStats),
                awayStats = S(awayStats)
            });
        }
    }
}

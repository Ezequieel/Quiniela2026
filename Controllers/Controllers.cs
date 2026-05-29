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
        public AccountController(AuthService auth) => _auth = auth;

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
        private int Uid => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public TournamentController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var uid  = Uid;
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

                // Verificar si el usuario pagó el torneo (cualquier fase del torneo)
                var paidTournament = await _db.Payments
                    .Include(p => p.Stage)
                    .AnyAsync(p =>
                        p.UserId == uid &&
                        p.Stage.TournamentId == t.Id &&
                        p.Status == PaymentStatus.Approved);

                foreach (var s in t.Stages.OrderBy(s => s.OrderNum))
                {
                    var paid = paidTournament;
                    var se   = await _db.StageEntries
                        .FirstOrDefaultAsync(e => e.UserId == uid && e.StageId == s.Id);
                    var rank = se != null ? await _db.StageEntries
                        .CountAsync(e => e.StageId == s.Id && e.Points > se.Points) + 1 : 0;
                    var total = await _db.StageEntries.CountAsync(e => e.StageId == s.Id);
                    var matchCount = await _db.Matches.CountAsync(m => m.StageId == s.Id);

                    card.Stages.Add(new StageCardVm
                    {
                        Id          = s.Id,
                        Name        = s.Name,
                        Type        = s.Type.ToString(),
                        Status      = s.Status.ToString(),
                        EntryFee    = s.EntryFee,
                        Deadline    = s.PredictionDeadline,
                        TotalMatches= matchCount,
                        IsPaid      = paid,
                        CanPredict  = paid && s.Status != StageStatus.Finished
                                   && s.PredictionDeadline > DateTime.UtcNow,
                        MyPoints    = se?.Points ?? 0,
                        MyRank      = rank,
                        TotalParticipants = total
                    });
                }
                vm.Tournaments.Add(card);
            }
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
            var stage = await _db.Stages.Include(s => s.Tournament)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return NotFound();

            // Si ya pagó, redirigir a predicciones
            if (await _paymentSvc.UserPaidStageAsync(Uid, stageId))
                return RedirectToAction("Stage", "Prediction", new { stageId });

            return View(new PaymentVm
            {
                StageId       = stageId,
                StageName     = stage.Name,
                EntryFee      = stage.EntryFee,
                PaymentQrPath = stage.Tournament.PaymentQrPath,
                PaymentInfo   = stage.Tournament.PaymentInfo,
                PaymentUrl    = stage.Tournament.PaymentUrl
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(PaymentVm vm)
        {
            if (!ModelState.IsValid)
            {
                var stage = await _db.Stages.Include(s => s.Tournament)
                    .FirstOrDefaultAsync(s => s.Id == vm.StageId);
                vm.PaymentQrPath = stage?.Tournament.PaymentQrPath;
                vm.PaymentInfo   = stage?.Tournament.PaymentInfo;
                vm.PaymentUrl    = stage?.Tournament.PaymentUrl;
                return View(vm);
            }
            var (ok, msg) = await _paymentSvc.SubmitAsync(Uid, vm);
            if (!ok) { ModelState.AddModelError("", msg); return View(vm); }
            TempData["Success"] = msg;
            return RedirectToAction("Stage", "Prediction", new { vm.StageId });
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

            var cards = matches.Select(m => {
                var pred = m.Predictions.FirstOrDefault();
                return new MatchPredVm
                {
                    Id = m.Id, ApiMatchId = m.ApiMatchId,
                    HomeTeam = m.HomeTeam, AwayTeam = m.AwayTeam,
                    HomeTeamLogo = m.HomeTeamLogo, AwayTeamLogo = m.AwayTeamLogo,
                    MatchDate = m.MatchDate, HomeScore = m.HomeScore, AwayScore = m.AwayScore,
                    Status = m.Status, Elapsed = m.Elapsed,
                    UserResult    = pred?.ResultPrediction.ToString(),
                    UserHomeScore = pred?.HomeScorePred,
                    UserAwayScore = pred?.AwayScorePred,
                    ResultCorrect = pred?.ResultCorrect,
                    ScoreCorrect  = pred?.ScoreCorrect,
                    PointsEarned  = pred?.PointsEarned ?? 0,
                    SpecialPoints = pred?.SpecialPoints ?? 0,
                    Special       = pred == null ? null : new SpecialPredVm
                    {
                        MatchId         = m.Id,
                        HomeTeam        = m.HomeTeam,
                        AwayTeam        = m.AwayTeam,
                        HomeTeamLogo    = m.HomeTeamLogo,
                        AwayTeamLogo    = m.AwayTeamLogo,
                        CanPredict      = stage.PredictionDeadline > DateTime.UtcNow,
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
            return View(isUclFinal ? "ChampionsFinal" : "Stage", new StageMatchesVm
            {
                StageId        = stageId,
                StageName      = stage.Name,
                TournamentId   = stage.TournamentId,
                TournamentName = stage.Tournament.Name,
                Deadline       = stage.PredictionDeadline,
                CanPredict     = stage.Status != StageStatus.Finished
                               && stage.PredictionDeadline > DateTime.UtcNow,
                IsPaid         = paid,
                Matches        = cards
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
        public async Task<IActionResult> SaveSpecial([FromBody] SpecialPredDto dto)
        {
            var uid   = Uid;
            var match = await _db.Matches.Include(m => m.Stage)
                .FirstOrDefaultAsync(m => m.Id == dto.MatchId);
            if (match == null) return Json(new { ok = false, msg = "Partido no encontrado." });
            if (match.Stage.PredictionDeadline < DateTime.UtcNow)
                return Json(new { ok = false, msg = "El plazo de predicciones cerró." });

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
                vm.StageId   = stageId;
                vm.StageName = stage?.Name;
                vm.IsStageView = true;

                var results = await _db.StageResults
                    .Include(r => r.User)
                    .Where(r => r.StageId == stageId)
                    .OrderBy(r => r.Rank)
                    .ToListAsync();

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
            return View(vm);
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

        public async Task<IActionResult> Log(int tournamentId, int? userId = null, int? matchId = null)
        {
            var tournament = await _db.Tournaments.FindAsync(tournamentId);
            if (tournament == null) return NotFound();

            // Solo mostrar el registro si TODAS las fases tienen el deadline cerrado
            // o están finalizadas — para que nadie copie predicciones de otros
            var uid = Uid;
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin)
            {
                var hayFaseAbierta = await _db.Stages.AnyAsync(s =>
                    s.TournamentId == tournamentId &&
                    s.Status != StageStatus.Finished &&
                    s.PredictionDeadline > DateTime.UtcNow);

                if (hayFaseAbierta)
                {
                    TempData["Error"] = "El registro de predicciones estará disponible cuando cierren todas las fases.";
                    return RedirectToAction("Index", "Tournament");
                }
            }

            // Base query: predicciones del torneo
            var query = _db.Predictions
                .Include(p => p.User)
                .Include(p => p.Match).ThenInclude(m => m.Stage)
                .Where(p => p.Match.Stage.TournamentId == tournamentId)
                .AsQueryable();

            // Aplicar filtros
            if (userId.HasValue)   query = query.Where(p => p.UserId  == userId.Value);
            if (matchId.HasValue)  query = query.Where(p => p.MatchId == matchId.Value);

            var predictions = await query
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync();

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
                    UpdatedAt     = p.UpdatedAt
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
        private readonly ApiFootballService _api;
        private readonly IWebHostEnvironment _env;

        public AdminController(AppDbContext db, TournamentService tourSvc,
            MatchSyncService syncSvc, PredictionService predSvc, ApiFootballService api,
            IWebHostEnvironment env)
        { _db = db; _tourSvc = tourSvc; _syncSvc = syncSvc; _predSvc = predSvc; _api = api; _env = env; }

        // Dashboard
        public async Task<IActionResult> Index() => View(new AdminDashboardVm
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
                Status              = StageStatus.Open
            };
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
            TempData[ok ? "Success" : "Error"] = msg;
            return RedirectToAction("Index");
        }

        // ── Calcular puntos ───────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CalculatePoints(int id) // id = stageId
        {
            await _predSvc.CalculateStagePointsAsync(id);
            TempData["Success"] = "Puntos calculados correctamente.";
            return RedirectToAction("Index");
        }

        // ── Ver pagos ─────────────────────────────────────
        public async Task<IActionResult> Payments(int? stageId = null)
        {
            var query = _db.Payments.Include(p => p.User).Include(p => p.Stage)
                .AsQueryable();
            if (stageId.HasValue) query = query.Where(p => p.StageId == stageId);
            var payments = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
            ViewBag.StageId = stageId;
            return View(payments);
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
                GroupKey = s.GroupKey
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

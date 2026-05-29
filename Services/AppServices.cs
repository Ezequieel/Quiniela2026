using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;
using QuinielaApp.ViewModels;

namespace QuinielaApp.Services
{
    // ════════════════════════════════════════════════════
    //  AUTH SERVICE
    // ════════════════════════════════════════════════════
    public class AuthService
    {
        private readonly AppDbContext _db;
        public AuthService(AppDbContext db) => _db = db;

        public async Task<(bool ok, string msg, User? user)> RegisterAsync(RegisterVm vm)
        {
            if (await _db.Users.AnyAsync(u => u.Email == vm.Email))
                return (false, "El correo ya está registrado.", null);
            if (await _db.Users.AnyAsync(u => u.Username == vm.Username))
                return (false, "El nombre de usuario ya existe.", null);
            var user = new User
            {
                FullName     = vm.FullName,
                Username     = vm.Username,
                Email        = vm.Email,
                PasswordHash = vm.Password,
                Role         = "Player"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return (true, "Registro exitoso.", user);
        }

        public async Task<(bool ok, string msg, User? user)> LoginAsync(LoginVm vm)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == vm.Email);
            if (user == null || user.PasswordHash != vm.Password)
                return (false, "Correo o contraseña incorrectos.", null);
            return (true, "OK", user);
        }
    }

    // ════════════════════════════════════════════════════
    //  PAYMENT SERVICE
    //  Pago ÚNICO por torneo → acceso a TODAS sus fases
    // ════════════════════════════════════════════════════
    public class PaymentService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PaymentService> _log;

        public PaymentService(AppDbContext db, IWebHostEnvironment env, ILogger<PaymentService> log)
        { _db = db; _env = env; _log = log; }

        /// <summary>
        /// Un solo pago inscribe al usuario en TODAS las fases del torneo.
        /// </summary>
        public async Task<(bool ok, string msg)> SubmitAsync(int userId, PaymentVm vm)
        {
            if (vm.Voucher == null) return (false, "Debes subir el comprobante.");

            var stage = await _db.Stages
                .Include(s => s.Tournament)
                    .ThenInclude(t => t.Stages)
                .FirstOrDefaultAsync(s => s.Id == vm.StageId);
            if (stage == null) return (false, "Fase no encontrada.");

            var tournamentId = stage.TournamentId;

            // Determinar qué fases cubre este pago
            // Si la fase tiene GroupKey, el pago cubre todas las del mismo grupo
            // Si no tiene GroupKey, el pago cubre solo esta fase
            var fasesACubrir = string.IsNullOrEmpty(stage.GroupKey)
                ? new List<Stage> { stage }
                : stage.Tournament.Stages
                    .Where(s => s.GroupKey == stage.GroupKey)
                    .ToList();

            // Verificar que no haya pagado ya alguna fase de este grupo
            var stageIdsCubiertos = fasesACubrir.Select(s => s.Id).ToList();
            var yaInscrito = await _db.Payments
                .AnyAsync(p =>
                    p.UserId == userId &&
                    stageIdsCubiertos.Contains(p.StageId) &&
                    p.Status == PaymentStatus.Approved);
            if (yaInscrito) return (false, "Ya estás inscrito en este grupo de fases.");

            // Validar archivo
            var ext     = Path.GetExtension(vm.Voucher.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
            if (!allowed.Contains(ext)) return (false, "Solo se aceptan JPG, PNG o PDF.");

            // Guardar archivo
            var folder   = Path.Combine(_env.WebRootPath, "uploads", "payments");
            Directory.CreateDirectory(folder);
            var fileName = $"{userId}_{vm.StageId}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            await using (var fs = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
                await vm.Voucher.CopyToAsync(fs);

            // Registrar el pago
            _db.Payments.Add(new Payment
            {
                UserId      = userId,
                StageId     = vm.StageId,
                Amount      = stage.EntryFee,
                VoucherPath = $"/uploads/payments/{fileName}",
                Notes       = vm.Notes,
                Status      = PaymentStatus.Approved,
                ApprovedAt  = DateTime.UtcNow
            });

            // Entrada al torneo
            if (!await _db.TournamentEntries.AnyAsync(te =>
                te.UserId == userId && te.TournamentId == tournamentId))
                _db.TournamentEntries.Add(new TournamentEntry
                { UserId = userId, TournamentId = tournamentId });

            // Crear StageEntry para todas las fases cubiertas por este pago
            foreach (var s in fasesACubrir)
            {
                if (!await _db.StageEntries.AnyAsync(se =>
                    se.UserId == userId && se.StageId == s.Id))
                    _db.StageEntries.Add(new StageEntry
                    { UserId = userId, StageId = s.Id, IsActive = true });
            }

            await _db.SaveChangesAsync();

            var msg = fasesACubrir.Count > 1
                ? $"Inscripción confirmada. Tienes acceso a {fasesACubrir.Count} fases del torneo."
                : "Inscripción confirmada. Ya puedes hacer tus predicciones.";

            _log.LogInformation("Pago aprobado: usuario {U}, {N} fases cubiertas",
                userId, fasesACubrir.Count);
            return (true, msg);
        }

        /// <summary>
        /// Verifica si el usuario pagó esta fase o cualquier fase del mismo GroupKey.
        /// </summary>
        public async Task<bool> UserPaidStageAsync(int userId, int stageId)
        {
            var stage = await _db.Stages
                .Include(s => s.Tournament).ThenInclude(t => t.Stages)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return false;

            // Si tiene GroupKey, verificar si pagó cualquier fase del mismo grupo
            if (!string.IsNullOrEmpty(stage.GroupKey))
            {
                var groupStageIds = stage.Tournament.Stages
                    .Where(s => s.GroupKey == stage.GroupKey)
                    .Select(s => s.Id).ToList();
                return await _db.Payments.AnyAsync(p =>
                    p.UserId == userId &&
                    groupStageIds.Contains(p.StageId) &&
                    p.Status == PaymentStatus.Approved);
            }

            // Sin GroupKey: pago individual por fase
            return await _db.Payments.AnyAsync(p =>
                p.UserId == userId &&
                p.StageId == stageId &&
                p.Status == PaymentStatus.Approved);
        }
    }

    // ════════════════════════════════════════════════════
    //  TOURNAMENT SERVICE
    // ════════════════════════════════════════════════════
    public class TournamentService
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public TournamentService(AppDbContext db, IWebHostEnvironment env) { _db = db; _env = env; }

        public async Task<Tournament> CreateAsync(CreateTournamentVm vm)
        {
            string? logoPath = await SaveImageAsync(vm.LogoFile, "logos");
            string? qrPath   = await SaveImageAsync(vm.QrFile,   "qr");
            var t = new Tournament
            {
                Name          = vm.Name,
                Description   = vm.Description,
                ApiLeagueId   = vm.ApiLeagueId,
                ApiSeason     = vm.ApiSeason,
                LogoUrl       = logoPath,
                PaymentQrPath = qrPath,
                PaymentInfo   = vm.PaymentInfo,
                PaymentUrl    = vm.PaymentUrl,
                Status        = TournamentStatus.Active
            };
            _db.Tournaments.Add(t);
            await _db.SaveChangesAsync();
            return t;
        }

        private async Task<string?> SaveImageAsync(IFormFile? file, string folder)
        {
            if (file == null) return null;
            var dir  = Path.Combine(_env.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            await using var fs = new FileStream(Path.Combine(dir, name), FileMode.Create);
            await file.CopyToAsync(fs);
            return $"/uploads/{folder}/{name}";
        }
    }

    // ════════════════════════════════════════════════════
    //  MATCH SYNC SERVICE
    // ════════════════════════════════════════════════════
    public class MatchSyncService
    {
        private readonly AppDbContext _db;
        private readonly ApiFootballService _api;
        private readonly ILogger<MatchSyncService> _log;

        public MatchSyncService(AppDbContext db, ApiFootballService api, ILogger<MatchSyncService> log)
        { _db = db; _api = api; _log = log; }

        public async Task<List<string>> GetRoundsAsync(int leagueId, int season) =>
            await _api.GetRoundsAsync(leagueId, season);

        public async Task<(bool ok, string msg, int count)> SyncAsync(int stageId)
        {
            var stage = await _db.Stages.Include(s => s.Tournament)
                .FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return (false, "Fase no encontrada.", 0);
            if (string.IsNullOrEmpty(stage.ApiRound))
                return (false, "Esta fase no tiene jornada configurada.", 0);

            var fixtures = await _api.GetFixturesByRoundAsync(
                stage.Tournament.ApiLeagueId, stage.Tournament.ApiSeason, stage.ApiRound);
            if (!fixtures.Any()) return (false, "No se encontraron partidos en la API.", 0);

            int created = 0, updated = 0;
            foreach (var f in fixtures)
            {
                var existing = await _db.Matches.FirstOrDefaultAsync(m => m.ApiMatchId == f.Fixture.Id);
                if (existing != null)
                {
                    existing.StageId      = stageId;
                    existing.HomeScore    = f.Goals.Home;
                    existing.AwayScore    = f.Goals.Away;
                    existing.Status       = f.Fixture.Status.Short;
                    existing.LastUpdated  = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    _db.Matches.Add(ApiFootballService.MapToMatch(f, stageId));
                    created++;
                }
            }
            await _db.SaveChangesAsync();
            _log.LogInformation("Fase {S}: {C} creados, {U} actualizados", stage.Name, created, updated);
            return (true, $"{created} partidos nuevos, {updated} actualizados.", created + updated);
        }

        public async Task SyncLiveScoresAsync(int stageId)
        {
            var stage = await _db.Stages.Include(s => s.Matches)
                .Include(s => s.Tournament).FirstOrDefaultAsync(s => s.Id == stageId);
            if (stage == null) return;
            var live = await _api.GetLiveAsync(stage.Tournament.ApiLeagueId);
            foreach (var f in live)
            {
                var match = stage.Matches.FirstOrDefault(m => m.ApiMatchId == f.Fixture.Id);
                if (match == null) continue;
                match.HomeScore   = f.Goals.Home;
                match.AwayScore   = f.Goals.Away;
                match.Status      = f.Fixture.Status.Short;
                match.Elapsed     = f.Fixture.Status.Elapsed;
                match.LastUpdated = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }
    }
}

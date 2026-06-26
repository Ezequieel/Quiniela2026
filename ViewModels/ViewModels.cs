using System.ComponentModel.DataAnnotations;
using QuinielaApp.Models;
using QuinielaApp.Helpers;

namespace QuinielaApp.ViewModels
{
    // ── Auth ──────────────────────────────────────────────
    public class LoginVm
    {
        [Required(ErrorMessage = "El correo es obligatorio")]
        [EmailAddress(ErrorMessage = "Correo inválido")]
        public string Email    { get; set; } = string.Empty;
        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterVm
    {
        [Required(ErrorMessage = "Tu nombre completo es obligatorio")]
        public string FullName { get; set; } = string.Empty;
        [Required(ErrorMessage = "Elige un nombre de usuario")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Entre 3 y 30 caracteres")]
        public string Username { get; set; } = string.Empty;
        [Required][EmailAddress(ErrorMessage = "Correo inválido")]
        public string Email    { get; set; } = string.Empty;
        [Required][StringLength(100, MinimumLength = 6, ErrorMessage = "Mínimo 6 caracteres")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    // ── Home / torneos disponibles ────────────────────────
    public class HomeVm
    {
        public List<TournamentCardVm> Tournaments { get; set; } = new();
        public string? UserName { get; set; }
    }

    public class TournamentCardVm
    {
        public int     Id           { get; set; }
        public string  Name         { get; set; } = string.Empty;
        public string  Description  { get; set; } = string.Empty;
        public string  Status       { get; set; } = string.Empty;
        public string? LogoUrl      { get; set; }
        public int     TotalPlayers { get; set; }
        public bool    IsJoined     { get; set; }
        public int     MyTotalPoints{ get; set; }
        public int     ApiLeagueId  { get; set; }
        public decimal BolsaTotal   { get; set; }
        public decimal Premio1      { get; set; }
        public decimal Premio2      { get; set; }
        public decimal Premio3      { get; set; }
        public List<StageCardVm> Stages { get; set; } = new();
    }

    public class StageCardVm
    {
        public int      Id          { get; set; }
        public string   Name        { get; set; } = string.Empty;
        public string   Type        { get; set; } = string.Empty;
        public string   Status      { get; set; } = string.Empty;
        public decimal  EntryFee    { get; set; }
        public DateTime Deadline    { get; set; }
        public int      TotalMatches      { get; set; }
        public int      OpenMatchesCount  { get; set; }
        public int      ClosedMatchesCount{ get; set; }
        public bool     IsPaid              { get; set; }
        public bool     HasPendingPayment   { get; set; }
        public bool     IsGroupPaymentLeader{ get; set; }
        public bool     HasGroupPendingPayment  { get; set; }
        public bool     HasGroupApprovedPayment { get; set; }
        public string?  GroupKey            { get; set; }
        public string?  BannerImagePath     { get; set; }
        public bool     CanPredict  { get; set; }
        public int      MyPoints    { get; set; }
        public int      MyRank      { get; set; }
        public int      TotalParticipants { get; set; }
        public bool     HasPartidosHoy   { get; set; }
        public int      PartidosHoyCount { get; set; }
    }

    // ── Pago / comprobante ────────────────────────────────
    public class PaymentVm
    {
        public int     StageId     { get; set; }
        public string  StageName   { get; set; } = string.Empty;
        public decimal EntryFee    { get; set; }
        public string? PaymentQrPath { get; set; }
        public string? PaymentInfo   { get; set; }
        public string? PaymentUrl    { get; set; }
        // Fases cubiertas por este pago (GroupKey o solo esta fase)
        public List<string> RelatedStageNames { get; set; } = new();
        // Upload
        [Required(ErrorMessage = "Sube tu comprobante de pago")]
        public IFormFile? Voucher  { get; set; }
        public string? Notes       { get; set; }
    }

    // ── Filtro de fases (admin pagos) ────────────────────
    public class StageFilterVm
    {
        public int    Id             { get; set; }
        public string Name           { get; set; } = "";
        public string TournamentName { get; set; } = "";
        public string Type           { get; set; } = "";
    }

    // ── Predicciones ──────────────────────────────────────
    public class StageMatchesVm
    {
        public int      StageId    { get; set; }
        public string   StageName  { get; set; } = string.Empty;
        public int      TournamentId { get; set; }
        public string   TournamentName { get; set; } = string.Empty;
        public DateTime Deadline   { get; set; }
        public bool     CanPredict { get; set; }
        public bool     IsPaid     { get; set; }
        public bool     IsKnockout { get; set; }
        public List<MatchPredVm> Matches { get; set; } = new();
    }

    // ── Predicciones especiales Champions Final ───────────
    public class SpecialPredVm
    {
        public int  MatchId       { get; set; }
        public string HomeTeam    { get; set; } = string.Empty;
        public string AwayTeam    { get; set; } = string.Empty;
        public string HomeTeamLogo{ get; set; } = string.Empty;
        public string AwayTeamLogo{ get; set; } = string.Empty;
        public bool CanPredict    { get; set; }

        // Predicciones guardadas
        public int?  HtHomePred      { get; set; }
        public int?  HtAwayPred      { get; set; }
        public int?  YellowCardsPred { get; set; }
        public int?  RedCardsPred    { get; set; }
        public bool? HasPenaltyPred  { get; set; }
        public int?  FirstScorerPred { get; set; }
        public bool? BothScoredPred  { get; set; }
        public int?  TotalGoalsPred  { get; set; }
        public int?  GoalDiffPred    { get; set; }

        // Resultados reales (para mostrar después del partido)
        public int?  HtHomeScore  { get; set; }
        public int?  HtAwayScore  { get; set; }
        public int   YellowCards  { get; set; }
        public int   RedCards     { get; set; }
        public bool  HasPenalty   { get; set; }
        public int?  FirstScorer  { get; set; }
        public bool  BothScored   { get; set; }
        public int?  TotalGoals   { get; set; }
        public int?  GoalDiff     { get; set; }
        public int   SpecialPoints{ get; set; }
        public bool  IsFinished   { get; set; }
    }

    public class MatchPredVm
    {
        public int      Id           { get; set; }
        public int      ApiMatchId   { get; set; }
        public string   HomeTeam     { get; set; } = string.Empty;
        public string   AwayTeam     { get; set; } = string.Empty;
        public string   HomeTeamLogo { get; set; } = string.Empty;
        public string   AwayTeamLogo { get; set; } = string.Empty;
        public DateTime MatchDate    { get; set; }
        public DateTime MatchDeadline{ get; set; }
        public DateTime MatchDateSv  { get; set; }
        public bool     CanPredict   { get; set; }
        public int?     HomeScore    { get; set; }
        public int?     AwayScore    { get; set; }
        public string   Status       { get; set; } = "NS";
        public int?     Elapsed      { get; set; }
        // Predicción del usuario
        public string?  UserResult        { get; set; }  // "Home"|"Draw"|"Away"
        public int?     UserHomeScore     { get; set; }
        public int?     UserAwayScore     { get; set; }
        public bool?    ResultCorrect     { get; set; }
        public bool?    ScoreCorrect      { get; set; }
        public int      PointsEarned      { get; set; }
        public int      SpecialPoints     { get; set; }
        // Eliminatorias
        public bool     IsKnockout      { get; set; }
        public string?  UserQualifier   { get; set; }  // "Home" | "Away" — predicción del usuario
        public bool?    UserPenaltyPred { get; set; }  // true = predijo penaltis, false = ET, null = sin pred
        public string?  MatchQualifier  { get; set; }  // "Home" | "Away" — quién clasificó de verdad
        public bool     HasExtension    { get; set; }  // true si ApiStatus == "AET" o "PEN"
        public bool     HuboPenaltis    { get; set; }  // true si ApiStatus == "PEN"
        public SpecialPredVm? Special    { get; set; }

        public bool IsLive     => Status is "1H" or "HT" or "2H" or "ET" or "P";
        public bool IsFinished => Status == "FT";
        public bool NotStarted => Status == "NS";
        public string ScoreDisplay => IsLive || IsFinished
            ? $"{HomeScore ?? 0} – {AwayScore ?? 0}" : "vs";
        public string StatusLabel => Status switch {
            "1H"  => $"{Elapsed}'",
            "HT"  => "Medio tiempo",
            "2H"  => $"{Elapsed}'",
            "FT"  => "Finalizado",
            "NS"  => TimeHelper.Format(MatchDate, "dd/MM HH:mm"),
            _     => Status
        };
    }

    public class SavePredictionVm
    {
        public int    MatchId       { get; set; }
        public string ResultPred    { get; set; } = string.Empty; // Home|Draw|Away
        public int?   HomeScorePred { get; set; }
        public int?   AwayScorePred { get; set; }
    }

    // ── Tabla de clasificación ────────────────────────────
    public class LeaderboardVm
    {
        public int    TournamentId   { get; set; }
        public string TournamentName { get; set; } = string.Empty;
        public int?   StageId        { get; set; }
        public string? StageName     { get; set; }
        public bool   IsStageView    { get; set; }
        public bool   IsPartial      { get; set; }
        public int    CurrentUserId  { get; set; }
        public List<LeaderboardRowVm> Rows { get; set; } = new();
        public List<StageTabVm> Stages { get; set; } = new();
    }

    public class LeaderboardRowVm
    {
        public int     Rank              { get; set; }
        public int     UserId            { get; set; }
        public string  Username          { get; set; } = string.Empty;
        public string  FullName          { get; set; } = string.Empty;
        public int     TotalPoints       { get; set; }
        public int     ResultHits        { get; set; }
        public int     ScoreHits         { get; set; }
        public int     TotalPredictions  { get; set; }
        public bool    IsCurrentUser     { get; set; }
        public string  Medal             => Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => "" };
    }

    public class StageTabVm
    {
        public int    Id     { get; set; }
        public string Name   { get; set; } = string.Empty;
        public bool   Active { get; set; }
    }

    // ── Admin: editar torneo ─────────────────────────────
    public class EditTournamentVm
    {
        public int    Id          { get; set; }
        [Required] public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PaymentInfo { get; set; } = string.Empty;
        public string? PaymentUrl   { get; set; }
        public string? ExistingLogo { get; set; }
        public string? ExistingQr   { get; set; }
        public IFormFile? LogoFile  { get; set; }
        public IFormFile? QrFile    { get; set; }
        public string Status        { get; set; } = "Active";
    }

    // ── Admin: editar fase ────────────────────────────────
    public class EditStageVm
    {
        public int      Id           { get; set; }
        public int      TournamentId { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        public string   Type         { get; set; } = "League";
        [Required][Range(0.01, 9999)] public decimal EntryFee { get; set; }
        [Required] public DateTime PredictionDeadline { get; set; }
        public int      OrderNum     { get; set; } = 1;
        public string   TournamentName { get; set; } = string.Empty;
        public string   Status       { get; set; } = "Open";
        public string?  GroupKey     { get; set; }
        public IFormFile? BannerFile     { get; set; }
        public string?    ExistingBanner { get; set; }
    }

    // ── Admin: crear torneo ───────────────────────────────
    public class CreateTournamentVm
    {
        [Required(ErrorMessage = "El nombre es obligatorio")]
        public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        [Required] public int ApiLeagueId  { get; set; } = 140;
        [Required] public int ApiSeason    { get; set; } = 2024;
        public IFormFile? LogoFile { get; set; }
        public string? PaymentUrl  { get; set; }
        [Required(ErrorMessage = "Sube el QR de pago")]
        public IFormFile? QrFile   { get; set; }
        [Required(ErrorMessage = "Incluye instrucciones de pago")]
        public string PaymentInfo  { get; set; } = string.Empty;
    }

    public class CreateStageVm
    {
        public int    TournamentId { get; set; }
        [Required] public string   Name      { get; set; } = string.Empty;
        [Required] public string   ApiRound  { get; set; } = string.Empty;
        public string   Type       { get; set; } = "League";
        [Required][Range(0.01, 9999)] public decimal EntryFee  { get; set; } = 20m;
        [Required] public DateTime PredictionDeadline { get; set; }
        public int OrderNum        { get; set; } = 1;
        public string? GroupKey    { get; set; }
        public IFormFile? BannerFile { get; set; }
        // Jornadas disponibles para mostrar en el select
        public List<string> AvailableRounds { get; set; } = new();
        public string TournamentName { get; set; } = string.Empty;
    }

    // ── Admin: dashboard ──────────────────────────────────
    public class AdminDashboardVm
    {
        public int     TotalUsers        { get; set; }
        public int     ActiveTournaments { get; set; }
        public int     TotalPredictions  { get; set; }
        public int     PendingPayments   { get; set; }
        public List<Tournament> Tournaments { get; set; } = new();
        public List<Payment>    RecentPayments { get; set; } = new();
    }

    // ── Registro de predicciones (log/auditoría) ─────────
    public class PredictionLogVm
    {
        public int    TournamentId   { get; set; }
        public string TournamentName { get; set; } = string.Empty;
        // Filtros activos
        public int?   FilterUserId   { get; set; }
        public int?   FilterMatchId  { get; set; }
        public string FilterUserName { get; set; } = string.Empty;
        public string FilterMatchName{ get; set; } = string.Empty;
        // Datos para los dropdowns
        public List<LogUserOptionVm>  Users   { get; set; } = new();
        public List<LogMatchOptionVm> Matches { get; set; } = new();
        // Registros
        public List<PredictionLogRowVm> Rows  { get; set; } = new();
        public int TotalRows { get; set; }
    }

    public class PredictionLogRowVm
    {
        public int      PredictionId  { get; set; }
        public int      UserId        { get; set; }
        public string   Username      { get; set; } = string.Empty;
        public string   FullName      { get; set; } = string.Empty;
        public int      MatchId       { get; set; }
        public string   HomeTeam      { get; set; } = string.Empty;
        public string   AwayTeam      { get; set; } = string.Empty;
        public string   HomeTeamLogo  { get; set; } = string.Empty;
        public string   AwayTeamLogo  { get; set; } = string.Empty;
        public string   StageName     { get; set; } = string.Empty;
        public DateTime MatchDate     { get; set; }
        public int?     HomeScore     { get; set; }
        public int?     AwayScore     { get; set; }
        public string   MatchStatus   { get; set; } = "NS";
        // Predicción
        public string   ResultPred    { get; set; } = string.Empty;
        public int?     HomePred      { get; set; }
        public int?     AwayPred      { get; set; }
        public bool?    ResultCorrect { get; set; }
        public bool?    ScoreCorrect  { get; set; }
        public int      PointsEarned  { get; set; }
        public DateTime UpdatedAt     { get; set; }

        public bool IsFinished => MatchStatus == "FT";
        public string ResultLabel => ResultPred switch {
            "Home" => $"{HomeTeam}",
            "Draw" => "Empate",
            "Away" => $"{AwayTeam}",
            _       => "—"
        };
        public string ScorePred => HomePred.HasValue ? $"{HomePred} – {AwayPred}" : "—";
        public string ScoreReal => IsFinished ? $"{HomeScore ?? 0} – {AwayScore ?? 0}" : "En curso";
    }

    public class LogUserOptionVm
    {
        public int    UserId   { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class LogMatchOptionVm
    {
        public int    MatchId  { get; set; }
        public string Label    { get; set; } = string.Empty;
    }

    // ── Admin: historial de usuarios ──────────────────────
    public class UsuarioAdminVm
    {
        public int      Id        { get; set; }
        public string   FullName  { get; set; } = string.Empty;
        public string   Username  { get; set; } = string.Empty;
        public string   Email     { get; set; } = string.Empty;
        public string   Role      { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int      Torneos   { get; set; }
        public decimal  PagoTotal { get; set; }
    }

}
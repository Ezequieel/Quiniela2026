namespace QuinielaApp.Models
{
    // ══════════════════════════════════════════════════════
    //  USUARIO
    // ══════════════════════════════════════════════════════
    public class User
    {
        public int      Id           { get; set; }
        public string   FullName     { get; set; } = string.Empty;
        public string   Username     { get; set; } = string.Empty;
        public string   Email        { get; set; } = string.Empty;
        public string   PasswordHash { get; set; } = string.Empty;
        public string   Role         { get; set; } = "Player";   // Player | Admin
        public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;

        public ICollection<TournamentEntry> Entries     { get; set; } = new List<TournamentEntry>();
        public ICollection<Prediction>      Predictions { get; set; } = new List<Prediction>();
        public ICollection<Payment>         Payments    { get; set; } = new List<Payment>();
    }

    // ══════════════════════════════════════════════════════
    //  TORNEO  (Mundial, Liga, etc.)
    // ══════════════════════════════════════════════════════
    public class Tournament
    {
        public int            Id          { get; set; }
        public string         Name        { get; set; } = string.Empty;  // "Quiniela Mundial 2026"
        public string         Description { get; set; } = string.Empty;
        public TournamentType Type        { get; set; } = TournamentType.League;
        public int            ApiLeagueId { get; set; }                  // 140 = La Liga
        public int            ApiSeason   { get; set; }                  // 2024
        public string?        LogoUrl     { get; set; }
        // QR de pago (imagen subida por el admin)
        public string?        PaymentQrPath { get; set; }
        public string?        PaymentInfo   { get; set; }  // Banco, cuenta, instrucciones
        public string?        PaymentUrl    { get; set; }  // URL alternativa al QR
        public TournamentStatus Status { get; set; } = TournamentStatus.Draft;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Stage>           Stages  { get; set; } = new List<Stage>();
        public ICollection<TournamentEntry> Entries { get; set; } = new List<TournamentEntry>();
    }

    public enum TournamentType   { League, WorldCup }
    public enum TournamentStatus { Draft, Active, Finished }

    // ══════════════════════════════════════════════════════
    //  FASE / ETAPA  (Jornada, Grupos, Octavos, etc.)
    // ══════════════════════════════════════════════════════
    public class Stage
    {
        public int          Id           { get; set; }
        public int          TournamentId { get; set; }
        public Tournament   Tournament   { get; set; } = null!;
        public string       Name         { get; set; } = string.Empty; // "Jornada 38" | "Octavos"
        public string       ApiRound     { get; set; } = string.Empty; // "Regular Season - 38"
        public StageType    Type         { get; set; } = StageType.League;
        public StageStatus  Status       { get; set; } = StageStatus.Pending;
        public decimal      EntryFee     { get; set; } = 20m;          // cuota para esta fase
        public int          OrderNum     { get; set; } = 1;            // orden dentro del torneo
        public DateTime     PredictionDeadline { get; set; }           // hasta cuándo predecir
        public DateTime     CreatedAt    { get; set; } = DateTime.UtcNow;
        /// Clave de agrupación para pago compartido. Fases con la misma clave comparten un solo pago.
        public string?      GroupKey         { get; set; }
        public string?      BannerImagePath  { get; set; }

        public ICollection<Match>        Matches  { get; set; } = new List<Match>();
        public ICollection<StageEntry>   Entries  { get; set; } = new List<StageEntry>();
        public ICollection<StageResult>  Results  { get; set; } = new List<StageResult>();
    }

    public enum StageType   { League, GroupStage, RoundOf32, RoundOf16, QuarterFinal, SemiFinal, Final }
    public enum StageStatus { Pending, Open, InProgress, Finished }

    // ══════════════════════════════════════════════════════
    //  PARTIDO
    // ══════════════════════════════════════════════════════
    public class Match
    {
        public int      Id           { get; set; }
        public int      StageId      { get; set; }
        public Stage    Stage        { get; set; } = null!;
        public int      ApiMatchId   { get; set; }
        public string   HomeTeam     { get; set; } = string.Empty;
        public string   AwayTeam     { get; set; } = string.Empty;
        public string   HomeTeamLogo { get; set; } = string.Empty;
        public string   AwayTeamLogo { get; set; } = string.Empty;
        public DateTime MatchDate    { get; set; }
        public int?     HomeScore    { get; set; }
        public int?     AwayScore    { get; set; }
        public string   Status       { get; set; } = "NS"; // NS 1H HT 2H FT
        public int?     Elapsed      { get; set; }
        public DateTime? LastUpdated        { get; set; }
        public DateTime? PointsCalculatedAt { get; set; }

        // Datos extras para predicciones especiales (Champions Final)
        public int?  HtHomeScore  { get; set; }   // Marcador HT local
        public int?  HtAwayScore  { get; set; }   // Marcador HT visitante
        public int   YellowCards  { get; set; } = 0;
        public int   RedCards     { get; set; } = 0;
        public bool  HasPenalty   { get; set; } = false;  // Penal en tiempo reglamentario
        public int?  FirstScorer  { get; set; }   // 1=Home, 2=Away (primer equipo en anotar)
        public bool  BothScored   { get; set; } = false;
        // Status real de la API: "FT", "AET" o "PEN" — para saber si hubo ET/penaltis
        public string? ApiStatus  { get; set; }
        // Eliminatorias: quién clasificó al final (incluye ET y penaltis). "Home" | "Away"
        public string? Qualifier  { get; set; }

        public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
    }

    // ══════════════════════════════════════════════════════
    //  PARTICIPACIÓN EN TORNEO Y FASE
    // ══════════════════════════════════════════════════════
    public class TournamentEntry
    {
        public int        Id           { get; set; }
        public int        UserId       { get; set; }
        public User       User         { get; set; } = null!;
        public int        TournamentId { get; set; }
        public Tournament Tournament   { get; set; } = null!;
        public int        TotalPoints  { get; set; } = 0;
        public DateTime   JoinedAt     { get; set; } = DateTime.UtcNow;
    }

    public class StageEntry
    {
        public int     Id        { get; set; }
        public int     UserId    { get; set; }
        public User    User      { get; set; } = null!;
        public int     StageId   { get; set; }
        public Stage   Stage     { get; set; } = null!;
        public bool    IsActive  { get; set; } = true;
        public int     Points    { get; set; } = 0;
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    // ══════════════════════════════════════════════════════
    //  PAGO / COMPROBANTE
    // ══════════════════════════════════════════════════════
    public class Payment
    {
        public int           Id           { get; set; }
        public int           UserId       { get; set; }
        public User          User         { get; set; } = null!;
        public int           StageId      { get; set; }
        public Stage         Stage        { get; set; } = null!;
        public decimal       Amount       { get; set; }
        public string        VoucherPath  { get; set; } = string.Empty; // ruta del comprobante
        public string?       Notes        { get; set; }
        public PaymentStatus Status       { get; set; } = PaymentStatus.Approved; // auto-aprobado
        public DateTime      CreatedAt    { get; set; } = DateTime.UtcNow;
        public DateTime?     ApprovedAt   { get; set; }
    }

    public enum PaymentStatus { Pending, Approved, Rejected }

    // ══════════════════════════════════════════════════════
    //  TOKEN DE RECUPERACIÓN DE CONTRASEÑA
    // ══════════════════════════════════════════════════════
    public class PasswordResetToken
    {
        public int      Id        { get; set; }
        public int      UserId    { get; set; }
        public User     User      { get; set; } = null!;
        public string   Token     { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool     Used      { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ══════════════════════════════════════════════════════
    //  PREDICCIÓN
    // ══════════════════════════════════════════════════════
    public class Prediction
    {
        public int     Id         { get; set; }
        public int     UserId     { get; set; }
        public User    User       { get; set; } = null!;
        public int     MatchId    { get; set; }
        public Match   Match      { get; set; } = null!;

        // Predicción básica: quién gana
        public MatchResult ResultPrediction { get; set; }   // Home | Draw | Away

        // Predicción de marcador (opcional, da puntos extra)
        public int?  HomeScorePred { get; set; }
        public int?  AwayScorePred { get; set; }

        // ── Predicciones especiales (Champions Final) ──
        public int?  HtHomePred     { get; set; }   // Marcador 1er tiempo local
        public int?  HtAwayPred     { get; set; }   // Marcador 1er tiempo visitante
        public int?  YellowCardsPred{ get; set; }   // Cantidad tarjetas amarillas
        public int?  RedCardsPred   { get; set; }   // Cantidad tarjetas rojas
        public bool? HasPenaltyPred { get; set; }   // Habrá penal en tiempo reglamentario
        public int?  FirstScorerPred{ get; set; }   // 1=Home, 2=Away, 0=Nadie
        public bool? BothScoredPred { get; set; }   // Ambos equipos marcan
        public int?  TotalGoalsPred { get; set; }   // Suma total de goles
        public int?  GoalDiffPred   { get; set; }   // Diferencia de goles (signed)

        // Predicción eliminatoria: quién clasifica cuando hay empate al 90' ("Home" | "Away")
        public string? QualifierPred { get; set; }

        // Predicción eliminatoria: si se definirá en penaltis (solo aplica si eligió empate)
        // true = penaltis | false = tiempo extra | null = sin predicción
        public bool? PenaltyPred { get; set; }

        // Resultado calculado
        public int   PointsEarned { get; set; } = 0;
        public bool? ResultCorrect { get; set; }
        public bool? ScoreCorrect  { get; set; }
        // Puntos por categoría (para mostrar detalle)
        public int   SpecialPoints { get; set; } = 0;

        public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;
    }

    public enum MatchResult { Home, Draw, Away }

    // ══════════════════════════════════════════════════════
    //  RESULTADO DE FASE (tabla de puntos por fase)
    // ══════════════════════════════════════════════════════
    public class StageResult
    {
        public int   Id         { get; set; }
        public int   StageId    { get; set; }
        public Stage Stage      { get; set; } = null!;
        public int   UserId     { get; set; }
        public User  User       { get; set; } = null!;
        public int   Points     { get; set; } = 0;
        public int   ResultHits { get; set; } = 0;  // cuántos resultados acertó
        public int   ScoreHits  { get; set; } = 0;  // cuántos marcadores exactos acertó
        public int   Rank       { get; set; } = 0;
    }
}

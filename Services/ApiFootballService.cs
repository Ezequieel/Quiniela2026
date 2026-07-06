using System.Text.Json;
using System.Text.Json.Serialization;
using QuinielaApp.Models;

namespace QuinielaApp.Services
{
    public class ApiFootballService
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<ApiFootballService> _log;
        private readonly string _apiKey;
        private const string BaseUrl = "https://v3.football.api-sports.io/";

        public ApiFootballService(IHttpClientFactory factory, IConfiguration cfg, ILogger<ApiFootballService> log)
        {
            _factory = factory;
            _log     = log;
            _apiKey  = cfg["ApiFootball:ApiKey"] ?? "";
        }

        // ── Jornadas disponibles ──────────────────────────
        public async Task<List<string>> GetRoundsAsync(int leagueId, int season)
        {
            try   { return await CallAsync<string>($"fixtures/rounds?league={leagueId}&season={season}"); }
            catch (Exception ex) { _log.LogError(ex, "Error GetRoundsAsync {L} {S}", leagueId, season); return []; }
        }

        // ── Partidos de una jornada ───────────────────────
        public async Task<List<ApiFixture>> GetFixturesByRoundAsync(int leagueId, int season, string round)
        {
            try   { return await CallAsync<ApiFixture>($"fixtures?league={leagueId}&season={season}&round={Uri.EscapeDataString(round)}"); }
            catch { return []; }
        }

        // ── Partidos en vivo ──────────────────────────────
        public async Task<List<ApiFixture>> GetLiveAsync(int leagueId)
        {
            try   { return await CallAsync<ApiFixture>($"fixtures?live=all&league={leagueId}"); }
            catch { return []; }
        }

        // ── Detalle de un partido ─────────────────────────
        public async Task<ApiFixture?> GetFixtureDetailAsync(int fixtureId)
        {
            try
            {
                var list = await CallAsync<ApiFixture>($"fixtures?id={fixtureId}&statistics=true&events=true");
                return list.FirstOrDefault();
            }
            catch { return null; }
        }

        // ── Tabla de posiciones ───────────────────────────
        public async Task<List<StandingEntry>> GetStandingsAsync(int leagueId, int season)
        {
            try
            {
                var list = await CallAsync<StandingsWrapper>($"standings?league={leagueId}&season={season}");
                return list.FirstOrDefault()?.League?.Standings?.FirstOrDefault() ?? [];
            }
            catch { return []; }
        }

        // ── Mapear fixture a Match ────────────────────────
        public static Match MapToMatch(ApiFixture f, int stageId) => new()
        {
            ApiMatchId   = f.Fixture.Id,
            StageId      = stageId,
            HomeTeam     = f.Teams.Home.Name,
            AwayTeam     = f.Teams.Away.Name,
            HomeTeamLogo = f.Teams.Home.Logo,
            AwayTeamLogo = f.Teams.Away.Logo,
            MatchDate    = f.Fixture.Date.UtcDateTime,
            Status       = f.Fixture.Status.Short
        };

        // ── Aplicar datos de un fixture a un Match existente ─────────────────
        // Único punto de verdad para mapear score, ApiStatus, Status normalizado
        // y Qualifier desde cualquier respuesta de la API (live, detail o batch).
        public static void ApplyFixtureData(Match match, ApiFixture f)
        {
            var rawStatus = f.Fixture.Status.Short;

            if (rawStatus == "AET" || rawStatus == "PEN")
            {
                // Usar marcador al 90' (fulltime), no el marcador de la prórroga
                match.HomeScore = f.Score?.Fulltime?.Home ?? f.Goals.Home;
                match.AwayScore = f.Score?.Fulltime?.Away ?? f.Goals.Away;
                match.Qualifier = f.Teams?.Home?.Winner == true ? "Home"
                                : f.Teams?.Away?.Winner == true ? "Away"
                                : null;
            }
            else
            {
                match.HomeScore = f.Goals.Home;
                match.AwayScore = f.Goals.Away;
                // Un partido que no terminó en AET/PEN no tiene clasificador por penales/ET.
                match.Qualifier = null;
            }

            match.ApiStatus = rawStatus;
            match.Status    = NormalizeStatus(
                rawStatus, f.Fixture.Status.Elapsed,
                match.MatchDate, match.HomeScore, match.AwayScore);
        }

        // ── Resultado del partido ─────────────────────────
        public static MatchResult? GetResult(int? home, int? away)
        {
            if (home == null || away == null) return null;
            if (home > away) return MatchResult.Home;
            if (away > home) return MatchResult.Away;
            return MatchResult.Draw;
        }

        public static string GetStat(List<ApiStatItem>? stats, string type) =>
            stats?.FirstOrDefault(s => s.Type == type)?.Value?.ToString() ?? "0";

        // ── Status finales reales de la API (AET = After Extra Time, PEN = Penalties) ──
        public static readonly HashSet<string> FinalApiStatuses = new()
        { "FT", "AET", "PEN", "AWD", "WO" };

        // Status que indican que el partido puede seguir yendo a tiempo extra/penales.
        // Nunca hay que forzar FT interno mientras la API reporte alguno de estos,
        // aunque hayan pasado más de 130 min desde el kickoff.
        private static readonly HashSet<string> ExtensionStatuses = new()
        { "ET", "BT", "P" };

        // ── Forzar FT cuando la API se queda atascada con un status
        // intermedio aunque el partido ya terminó ────────────────
        public static string NormalizeStatus(string apiStatus, int? elapsed, DateTime matchDate, int? homeScore, int? awayScore)
        {
            if (FinalApiStatuses.Contains(apiStatus))
                return "FT";

            // Nunca forzar FT mientras el partido esté en tiempo extra/penales:
            // ahí es normal y esperado superar los 130 min desde el kickoff.
            if (ExtensionStatuses.Contains(apiStatus))
                return apiStatus;

            // NOTA: existía una "Regla 1" que forzaba FT en "2H" con elapsed>=95,
            // pensada para APIs atascadas. Se eliminó porque un 2H con 5+ min de
            // descuento (habitual con VAR) es un partido real en curso, no atascado:
            // forzaba FT con el marcador a mitad de la jugada, sin Qualifier, y si
            // era el último partido de la fase la marcaba Finished antes de tiempo.
            // La Regla 2 (130 min de margen desde el kickoff) ya cubre el caso de
            // un status realmente atascado sin generar falsos positivos en vivo.

            // Regla: más de 130 min desde el inicio con marcador definido
            // (solo para status que la API nunca actualizó — no puede ser un
            // partido legítimamente en curso, ya filtrado arriba)
            // NOTA: "2H" también queda excluido — un partido puede arrancar tarde
            // o sufrir una demora (clima, lesión, VAR) y seguir genuinamente en el
            // segundo tiempo mucho después de los 130 min desde el MatchDate
            // guardado (visto en vivo: México-Inglaterra, kickoff tardío, 139 min
            // transcurridos con el partido recién en el minuto 48 del 2T).
            if (matchDate < DateTime.UtcNow.AddMinutes(-130) &&
                apiStatus != "NS" &&
                apiStatus != "1H" &&
                apiStatus != "HT" &&
                apiStatus != "2H" &&
                homeScore.HasValue && awayScore.HasValue)
                return "FT";

            return apiStatus;
        }

        // ── Llamada HTTP con URL completa ─────────────────
        private async Task<List<T>> CallAsync<T>(string endpoint)
        {
            var url = BaseUrl + endpoint;
            _log.LogInformation("→ API-Football GET {U}", url);

            using var client  = _factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-apisports-key", _apiKey);

            var res  = await client.SendAsync(request);
            var json = await res.Content.ReadAsStringAsync();
            _log.LogInformation("← {S} | {B} bytes", (int)res.StatusCode, json.Length);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Error {S}: {Body}", res.StatusCode, json);
                return [];
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var obj  = JsonSerializer.Deserialize<ApiResponse<T>>(json, opts);
            return obj?.Response ?? [];
        }
    }

    // ════════════════════════════════════════════════════
    //  DTOs de API-Football
    // ════════════════════════════════════════════════════
    public class ApiResponse<T>
    {
        [JsonPropertyName("response")] public List<T> Response { get; set; } = [];
        [JsonPropertyName("results")]  public int     Results  { get; set; }
    }

    public class ApiFixture
    {
        [JsonPropertyName("fixture")]    public ApiFixtureInfo     Fixture    { get; set; } = null!;
        [JsonPropertyName("league")]     public ApiLeagueInfo      League     { get; set; } = null!;
        [JsonPropertyName("teams")]      public ApiTeamsInfo        Teams      { get; set; } = null!;
        [JsonPropertyName("goals")]      public ApiGoalsInfo        Goals      { get; set; } = null!;
        [JsonPropertyName("score")]      public ApiScoreInfo        Score      { get; set; } = null!;
        [JsonPropertyName("events")]     public List<ApiEvent>?     Events     { get; set; }
        [JsonPropertyName("statistics")] public List<ApiTeamStat>?  Statistics { get; set; }
    }

    public class ApiFixtureInfo
    {
        [JsonPropertyName("id")]     public int            Id     { get; set; }
        [JsonPropertyName("date")]   public DateTimeOffset Date   { get; set; }
        [JsonPropertyName("venue")]  public ApiVenue?  Venue  { get; set; }
        [JsonPropertyName("status")] public ApiStatus  Status { get; set; } = null!;
    }

    public class ApiStatus
    {
        [JsonPropertyName("short")]   public string Short   { get; set; } = "NS";
        [JsonPropertyName("elapsed")] public int?   Elapsed { get; set; }
    }

    public class ApiVenue
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
    }

    public class ApiLeagueInfo
    {
        [JsonPropertyName("id")]    public int    Id    { get; set; }
        [JsonPropertyName("round")] public string Round { get; set; } = string.Empty;
    }

    public class ApiTeamsInfo
    {
        [JsonPropertyName("home")] public ApiTeam Home { get; set; } = null!;
        [JsonPropertyName("away")] public ApiTeam Away { get; set; } = null!;
    }

    public class ApiTeam
    {
        [JsonPropertyName("id")]     public int    Id     { get; set; }
        [JsonPropertyName("name")]   public string Name   { get; set; } = string.Empty;
        [JsonPropertyName("logo")]   public string Logo   { get; set; } = string.Empty;
        [JsonPropertyName("winner")] public bool?  Winner { get; set; }
    }

    public class ApiGoalsInfo
    {
        [JsonPropertyName("home")] public int? Home { get; set; }
        [JsonPropertyName("away")] public int? Away { get; set; }
    }

    public class ApiScoreInfo
    {
        [JsonPropertyName("halftime")] public ApiGoalsInfo Halftime { get; set; } = null!;
        [JsonPropertyName("fulltime")] public ApiGoalsInfo Fulltime { get; set; } = null!;
    }

    public class ApiEvent
    {
        [JsonPropertyName("time")]   public ApiEvTime  Time   { get; set; } = null!;
        [JsonPropertyName("team")]   public ApiTeam    Team   { get; set; } = null!;
        [JsonPropertyName("player")] public ApiPlayer  Player { get; set; } = null!;
        [JsonPropertyName("assist")] public ApiPlayer? Assist { get; set; }
        [JsonPropertyName("type")]   public string     Type   { get; set; } = string.Empty;
        [JsonPropertyName("detail")] public string     Detail { get; set; } = string.Empty;
    }

    public class ApiEvTime
    {
        [JsonPropertyName("elapsed")] public int  Elapsed { get; set; }
        [JsonPropertyName("extra")]   public int? Extra   { get; set; }
    }

    public class ApiPlayer
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    public class ApiTeamStat
    {
        [JsonPropertyName("team")]       public ApiTeam           Team       { get; set; } = null!;
        [JsonPropertyName("statistics")] public List<ApiStatItem> Statistics { get; set; } = [];
    }

    public class ApiStatItem
    {
        [JsonPropertyName("type")]  public string  Type  { get; set; } = string.Empty;
        [JsonPropertyName("value")] public object? Value { get; set; }
    }

    public class StandingsWrapper
    {
        [JsonPropertyName("league")] public StandingsLeague League { get; set; } = null!;
    }

    public class StandingsLeague
    {
        [JsonPropertyName("standings")] public List<List<StandingEntry>> Standings { get; set; } = [];
    }

    public class StandingEntry
    {
        [JsonPropertyName("rank")]      public int      Rank      { get; set; }
        [JsonPropertyName("team")]      public ApiTeam  Team      { get; set; } = null!;
        [JsonPropertyName("points")]    public int      Points    { get; set; }
        [JsonPropertyName("goalsDiff")] public int      GoalsDiff { get; set; }
        [JsonPropertyName("form")]      public string?  Form      { get; set; }
        [JsonPropertyName("all")]       public StandingStats All  { get; set; } = null!;
    }

    public class StandingStats
    {
        [JsonPropertyName("played")] public int          Played { get; set; }
        [JsonPropertyName("win")]    public int          Win    { get; set; }
        [JsonPropertyName("draw")]   public int          Draw   { get; set; }
        [JsonPropertyName("lose")]   public int          Lose   { get; set; }
        [JsonPropertyName("goals")]  public StandingGoals Goals { get; set; } = null!;
    }

    public class StandingGoals
    {
        [JsonPropertyName("for")]     public int For     { get; set; }
        [JsonPropertyName("against")] public int Against { get; set; }
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;

namespace KnowBall.Infrastructure.Sources;

public sealed class NhlApiOptions
{
    /// <summary>The NHL's stats API, which answers whole seasons in pages of 100 rows. Must end with a slash.</summary>
    public string StatsUrl { get; set; } = "https://api.nhle.com/stats/rest/en/";
    /// <summary>The NHL's site API: one player's whole record, and each team's roster today. Must end with a slash.</summary>
    public string WebUrl { get; set; } = "https://api-web.nhle.com/v1/";
    /// <summary>
    /// Read the answers from this folder instead, as the files <see cref="NhlApiFiles"/> names (players/8471214.json,
    /// stats/skaters.json). Used by tests and offline runs.
    /// </summary>
    public string? Directory { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>The first season read, by the year it starts: 1979-80 is the first after the WHA merger.</summary>
    public int FirstSeason { get; set; } = 1979;
    /// <summary>
    /// Players with fewer regular-season games than this since <see cref="FirstSeason"/> are left out, unless they have
    /// played this season: a call-up with a dozen games is nobody's guess, and leaving thousands out keeps the import small.
    /// </summary>
    public int MinGames { get; set; } = 50;
    /// <summary>How many player records are downloaded at once.</summary>
    public int Parallelism { get; set; } = 4;
    /// <summary>
    /// The most requests a second to the NHL's sites, from both sources together. The site answers 429 to a client that
    /// asks too fast for too long, so a full load of about 4,500 player records is spread over a quarter of an hour.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 5;
    /// <summary>How long to wait before asking once more when the site is still turning requests away (0 = don't).</summary>
    public int RetryPauseSeconds { get; set; } = 60;
    /// <summary>The load fails when more than this share of the player records still cannot be read.</summary>
    public double MaxFailedShare { get; set; } = 0.02;
}

/// <summary>
/// One pace for every request to the NHL's sites, shared by both sources: each request waits for a token, so the
/// player records, reports and rosters together stay under <see cref="NhlApiOptions.RequestsPerSecond"/>.
/// </summary>
public sealed class NhlThrottle(IOptions<NhlApiOptions> options) : IDisposable
{
    private readonly TokenBucketRateLimiter limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = Math.Max(1, options.Value.Parallelism),
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1 / Math.Max(0.1, options.Value.RequestsPerSecond)),
        QueueLimit = int.MaxValue,
        AutoReplenishment = true,
    });

    public ValueTask<RateLimitLease> WaitAsync(CancellationToken ct) => limiter.AcquireAsync(1, ct);

    public void Dispose() => limiter.Dispose();
}

/// <summary>Holds each request to the NHL, retries included, to the shared <see cref="NhlThrottle"/>.</summary>
public sealed class NhlThrottleHandler(NhlThrottle throttle) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (await throttle.WaitAsync(cancellationToken)) { }
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>NHL dates: which season a day belongs to.</summary>
public static class NhlCalendar
{
    /// <summary>The newest season that has started: games start in October.</summary>
    public static int LatestPlayedSeason(DateTimeOffset now) => now.Month >= 10 ? now.Year : now.Year - 1;

    /// <summary>The season rosters are being built for: the league year turns over on 1 July.</summary>
    public static int RosterSeason(DateTimeOffset now) => now.Month >= 7 ? now.Year : now.Year - 1;

    /// <summary>From opening night in October to the Stanley Cup Final in June.</summary>
    public static bool InSeason(DateTimeOffset now) => now.Month >= 10 || now.Month <= 6;

    /// <summary>2023 → 20232024, the NHL's own season id.</summary>
    public static int SeasonId(int season) => season * 10000 + season + 1;

    /// <summary>20232024 → 2023.</summary>
    public static int Season(int seasonId) => seasonId / 10000;
}

/// <summary>
/// The NHL's teams and the names they have played under (<c>nhl-teams.json</c>). The NHL's own files name a team by
/// its full name ("Winnipeg Jets (1979)", "Quebec Nordiques"); a team that moved is a former team of the franchise
/// it became. The Coyotes moved twice and then stopped: Phoenix and the first Winnipeg Jets are former Arizona teams,
/// and Arizona is a former team of no one.
/// </summary>
internal sealed class NhlTeams
{
    internal sealed record CurrentTeam(string Key, string Abbreviation, string City, string Name, string Conference, string Division, string LogoUrl, string FullName);
    internal sealed record FormerTeam(string Code, string FullName, string City, string Name, string? Franchise);
    private sealed record TeamFile(List<CurrentTeam> Current, List<FormerTeam> Former, Dictionary<string, string> Aliases);

    public static readonly Lazy<NhlTeams> Instance = new(Load);

    public IReadOnlyDictionary<string, CurrentTeam> Current { get; }
    public IReadOnlyDictionary<string, FormerTeam> Former { get; }
    private readonly Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

    private NhlTeams(TeamFile file)
    {
        Current = file.Current.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        Former = file.Former.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var t in file.Current) _byName[t.FullName] = t.Key;
        foreach (var t in file.Former) _byName.TryAdd(t.FullName, t.Code);
        foreach (var (name, key) in file.Aliases) _byName[name] = key;
    }

    /// <summary>The key a team's full name is filed under, or null for a name this build does not know.</summary>
    public string? Key(string? fullName) => fullName is not null && _byName.TryGetValue(fullName.Trim(), out var key) ? key : null;

    /// <summary>The key for a three-letter code, current or former, or null.</summary>
    public string? KeyOfCode(string? code) =>
        code is null ? null : Current.ContainsKey(code) ? Current[code].Key : Former.TryGetValue(code, out var f) ? f.Code : null;

    public static SourceTeam ToSourceTeam(CurrentTeam t) =>
        new(t.Key, t.City, t.Name, t.Abbreviation, t.Conference, t.Division, LogoUrl: t.LogoUrl);

    public IReadOnlyList<SourceTeam> CurrentSourceTeams() => Current.Values.Select(ToSourceTeam).ToList();

    /// <summary>The current team a former one became, following the Coyotes' moves; null for one that folded.</summary>
    public CurrentTeam? CurrentFranchise(string key)
    {
        for (var i = 0; i < 4; i++)
        {
            if (Current.TryGetValue(key, out var current)) return current;
            if (!Former.TryGetValue(key, out var f) || f.Franchise is null) return null;
            key = f.Franchise;
        }
        return null;
    }

    /// <summary>Every current team, and each former identity seen in the record, with the seasons it was seen.</summary>
    public List<HistoryTeam> HistoryTeams(IReadOnlyDictionary<string, (int From, int To)> seen, int latestSeason)
    {
        var teams = new List<HistoryTeam>();
        foreach (var t in Current.Values)
        {
            int? from = seen.TryGetValue(t.Key, out var span) ? span.From : null;
            teams.Add(new HistoryTeam(t.Key, ToSourceTeam(t), from, null, null));
        }

        // A former team's franchise has to be in the list too, even when no one in the record played for it.
        var keys = seen.Keys.Where(Former.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.ToList())
        {
            for (var f = Former[key].Franchise; f is not null && Former.TryGetValue(f, out var next); f = next.Franchise) keys.Add(f);
        }

        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            var f = Former[key];
            var franchise = CurrentFranchise(key);
            int? from = seen.TryGetValue(key, out var span) ? span.From : null;
            int? to = seen.TryGetValue(key, out span) ? span.To : null;
            teams.Add(new HistoryTeam(
                key,
                // A team that kept its name when it moved wore the logo it wears now; the Nordiques did not.
                new SourceTeam(key, f.City, f.Name, f.Code, franchise?.Conference, franchise?.Division,
                    LogoUrl: franchise is not null && f.Name == franchise.Name && f.Franchise == franchise.Key ? franchise.LogoUrl : null),
                from,
                to,
                f.Franchise));
        }
        return teams;
    }

    private static NhlTeams Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("nhl-teams.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        var file = JsonSerializer.Deserialize<TeamFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("nhl-teams.json is empty");
        return new NhlTeams(file);
    }
}

/// <summary>
/// The NHL's answers, from the web or from a folder. Each call has a file name for offline runs; online, a stats
/// report is read a page of 100 rows at a time and the pages are joined, so the file and the web answer look the same.
/// </summary>
internal static class NhlApiFiles
{
    public const string CareerSkaters = "stats/skaters.json";
    public const string CareerGoalies = "stats/goalies.json";
    public static string SeasonSkaters(int season) => $"stats/skaters-{season}.json";
    public static string SeasonGoalies(int season) => $"stats/goalies-{season}.json";
    /// <param name="gameType">2 for the regular season, 3 for the playoffs.</param>
    public static string TeamSeasons(int season, int gameType) => $"stats/teams-{season}-{gameType}.json";
    public static string Player(string id) => $"players/{id}.json";
    public static string Roster(string team) => $"rosters/{team}.json";

    /// <summary>The answer, or null when there is none (a player id the site no longer knows).</summary>
    public static async Task<JsonDocument?> OpenAsync(HttpClient http, NhlApiOptions opts, string file, string url, CancellationToken ct)
    {
        var pause = TimeSpan.FromSeconds(Math.Max(0, opts.RetryPauseSeconds));
        if (!string.IsNullOrWhiteSpace(opts.Directory))
        {
            var local = Path.Combine(opts.Directory, file);
            if (!File.Exists(local)) return null;
            await using var stream = File.OpenRead(local);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }

        var response = await http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests && pause > TimeSpan.Zero)
        {
            // Still turned away once the quick retries are spent: the site wants a longer rest before one more try.
            response.Dispose();
            await Task.Delay(pause, ct);
            response = await http.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct);
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Every row of a stats report: <paramref name="report"/> such as "skater/summary", filtered by a cayenne
    /// expression. Offline, the rows are the "data" array of <paramref name="file"/>.
    /// </summary>
    public static async Task<List<JsonElement>> ReportAsync(
        HttpClient http, NhlApiOptions opts, string file, string report, string filter, string? factFilter, CancellationToken ct)
    {
        var rows = new List<JsonElement>();
        const int page = 100;
        // The order keeps the pages stable; a team report has no player id, and is read one season at a time.
        var teamReport = report.StartsWith("team/", StringComparison.Ordinal);
        var sort = Uri.EscapeDataString($$"""[{"property":"{{(teamReport ? "teamId" : "playerId")}}","direction":"ASC"}]""");
        for (var start = 0; ; start += page)
        {
            var url = $"{opts.StatsUrl}{report}?isAggregate={(teamReport ? "false" : "true")}&isGame=false&limit={page}&start={start}&sort={sort}"
                      + $"&cayenneExp={Uri.EscapeDataString(filter)}"
                      + (factFilter is null ? "" : $"&factCayenneExp={Uri.EscapeDataString(factFilter)}");
            using var doc = await OpenAsync(http, opts, file, url, ct);
            if (doc is null) return rows;
            var data = doc.RootElement.GetProperty("data");
            foreach (var row in data.EnumerateArray()) rows.Add(row.Clone());

            var total = doc.RootElement.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : rows.Count;
            if (!string.IsNullOrWhiteSpace(opts.Directory) || data.GetArrayLength() < page || rows.Count >= total) return rows;
        }
    }

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
                JsonValueKind.Number => v.GetRawText(),
                // The site API gives names in several languages: {"default": "Alex", "cs": "Alexandr"}.
                JsonValueKind.Object => v.Str("default"),
                _ => null,
            }
            : null;

    public static int? Int(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static double? Dbl(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>"L" → "LW", "R" → "RW"; centers, defensemen and goalies keep their letter.</summary>
    public static string? Position(string? code) => code?.ToUpperInvariant() switch
    {
        null => null,
        "L" or "LW" => "LW",
        "R" or "RW" => "RW",
        "C" => "C",
        "D" => "D",
        "G" => "G",
        _ => null,
    };

    /// <summary>"17:21" → 17.35 minutes; "2413:22" → 2413.37.</summary>
    public static double? Minutes(string? value)
    {
        if (value is null) return null;
        var parts = value.Split(':');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? m + s / 60.0
            : null;
    }

    /// <summary>The countries most NHL players come from, by the code the NHL files use; any other code is kept as it is.</summary>
    public static string? Country(string? code) => code?.ToUpperInvariant() switch
    {
        null => null,
        "CAN" => "Canada",
        "USA" => "USA",
        "SWE" => "Sweden",
        "FIN" => "Finland",
        "RUS" => "Russia",
        "CZE" => "Czechia",
        "SVK" => "Slovakia",
        "DEU" or "GER" => "Germany",
        "CHE" or "SUI" => "Switzerland",
        "LVA" or "LAT" => "Latvia",
        "DNK" or "DEN" => "Denmark",
        "NOR" => "Norway",
        "AUT" => "Austria",
        "SVN" or "SLO" => "Slovenia",
        "BLR" => "Belarus",
        "UKR" => "Ukraine",
        "KAZ" => "Kazakhstan",
        "FRA" => "France",
        "GBR" => "United Kingdom",
        "POL" => "Poland",
        "AUS" => "Australia",
        "NLD" or "NED" => "Netherlands",
        "ITA" => "Italy",
        "JPN" => "Japan",
        "KOR" => "South Korea",
        "LTU" => "Lithuania",
        "EST" => "Estonia",
        "HRV" or "CRO" => "Croatia",
        "SRB" => "Serbia",
        "HUN" => "Hungary",
        "BRA" => "Brazil",
        "NGA" => "Nigeria",
        "ZAF" => "South Africa",
        "JAM" => "Jamaica",
        "VEN" => "Venezuela",
        "TWN" => "Taiwan",
        "BHS" => "Bahamas",
        "BRN" => "Brunei",
        "SCO" => "Scotland",
        "IRL" => "Ireland",
        var other => other,
    };
}

/// <summary>
/// The NHL's full record from its own public stats API: everyone with <see cref="NhlApiOptions.MinGames"/> games
/// since <see cref="NhlApiOptions.FirstSeason"/>, or who has played this season. The stats reports pick out who,
/// and each one's record on the NHL's site API gives the rest: bio facts, draft slot, headshot, every regular season
/// with each team in the order played, and his trophies and Stanley Cups. Team records come from the team reports.
/// </summary>
public sealed class NhlHistorySource(HttpClient http, IOptions<NhlApiOptions> options, TimeProvider clock, ILogger<NhlHistorySource> logger)
    : IHistorySource
{
    public const string SourceName = "nhl-history";
    public const string IdSource = "nhl";
    public string Name => SourceName;

    public async Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var teams = NhlTeams.Instance.Value;
        var latest = NhlCalendar.LatestPlayedSeason(clock.GetUtcNow());

        // The team records are a few dozen requests, read first so that a bad answer fails the load before the long part.
        var records = await ReadRecordsAsync(opts, latest, teams, cancellationToken);
        var ids = await PlayerIdsAsync(opts, latest, cancellationToken);
        if (ids.Count == 0) throw new InvalidOperationException("The NHL stats reports list no players.");
        logger.LogInformation("The NHL lists {Players} players with {Games}+ games since {First} or a game this season",
            ids.Count, opts.MinGames, opts.FirstSeason);

        var players = new ConcurrentBag<HistoryPlayer>();
        var unknownTeams = new ConcurrentDictionary<string, int>();
        var failed = new ConcurrentBag<string>();
        var missing = 0;
        async Task ReadAsync(string id, CancellationToken ct)
        {
            try
            {
                using var doc = await NhlApiFiles.OpenAsync(http, opts, NhlApiFiles.Player(id), $"{opts.WebUrl}player/{id}/landing", ct);
                if (doc is null)
                {
                    Interlocked.Increment(ref missing);
                    return;
                }
                if (ReadPlayer(doc.RootElement, opts.FirstSeason, latest, teams, unknownTeams) is { } player) players.Add(player);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException
                                           || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                failed.Add(id);
            }
        }

        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, opts.Parallelism), CancellationToken = cancellationToken },
            async (id, ct) => await ReadAsync(id, ct));

        // The site turns a busy client away for a while (429). The records it would not give are asked for again, one
        // at a time, after a pause; a few still missing leave those players as they were rather than stop the load.
        if (!failed.IsEmpty)
        {
            var again = failed.Order(StringComparer.Ordinal).ToList();
            failed.Clear();
            logger.LogWarning("{Failed} NHL player records failed; asking again in {Pause} seconds", again.Count, opts.RetryPauseSeconds);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, opts.RetryPauseSeconds)), clock, cancellationToken);
            foreach (var id in again) await ReadAsync(id, cancellationToken);
            if (failed.Count > ids.Count * opts.MaxFailedShare)
                throw new InvalidOperationException($"{failed.Count} of {ids.Count} NHL player records could not be read.");
            if (!failed.IsEmpty) logger.LogWarning("{Failed} NHL player records could not be read and are left as they were", failed.Count);
        }
        foreach (var (name, count) in unknownTeams)
            logger.LogWarning("{Count} NHL season lines name a team this build does not know: {Team}", count, name);
        if (missing > 0) logger.LogWarning("{Missing} NHL players had no record on the site", missing);

        var ordered = players.OrderBy(p => p.ExternalId, StringComparer.Ordinal).ToList();
        var newest = ordered.SelectMany(p => p.Seasons).Select(s => s.Season).DefaultIfEmpty(latest).Max();
        var seen = new Dictionary<string, (int From, int To)>();
        foreach (var s in ordered.SelectMany(p => p.Seasons))
        {
            if (s.TeamKey is not { } key) continue;
            seen[key] = seen.TryGetValue(key, out var span) ? (Math.Min(span.From, s.Season), Math.Max(span.To, s.Season)) : (s.Season, s.Season);
        }

        logger.LogInformation("The NHL has {Players} players, {Teams} teams and {Records} team records from {First} through {Season}",
            ordered.Count, seen.Count, records.Count, opts.FirstSeason, newest);
        return new HistorySnapshot("nhl", IdSource, newest, teams.HistoryTeams(seen, newest), ordered, TeamSeasons: records);
    }

    /// <summary>Everyone with enough games since the first season, and everyone who has played this season.</summary>
    private async Task<List<string>> PlayerIdsAsync(NhlApiOptions opts, int latest, CancellationToken ct)
    {
        var range = $"seasonId>={NhlCalendar.SeasonId(opts.FirstSeason)} and seasonId<={NhlCalendar.SeasonId(latest)} and gameTypeId=2";
        var season = $"seasonId={NhlCalendar.SeasonId(latest)} and gameTypeId=2";
        var games = $"gamesPlayed>={opts.MinGames}";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, report, filter, fact) in new[]
                 {
                     (NhlApiFiles.CareerSkaters, "skater/summary", range, (string?)games),
                     (NhlApiFiles.CareerGoalies, "goalie/summary", range, games),
                     (NhlApiFiles.SeasonSkaters(latest), "skater/summary", season, null),
                     (NhlApiFiles.SeasonGoalies(latest), "goalie/summary", season, null),
                 })
        {
            foreach (var row in await NhlApiFiles.ReportAsync(http, opts, file, report, filter, fact, ct))
            {
                // Offline files are not filtered by the server, so the game count is checked here too.
                if (fact is not null && (row.Int("gamesPlayed") ?? 0) < opts.MinGames) continue;
                if (row.Str("playerId") is { } id) ids.Add(id);
            }
        }
        return ids.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// One player from his record on the site, or null for one with no NHL regular season since the first season read.
    /// Each season with one team is one line with his stats; a season split by a trade has a line per team, in the
    /// order he played for them, and a line with no team for the whole season.
    /// </summary>
    internal static HistoryPlayer? ReadPlayer(JsonElement p, int firstSeason, int latestSeason, NhlTeams teams,
        ConcurrentDictionary<string, int>? unknownTeams = null)
    {
        if (p.Str("playerId") is not { } id) return null;
        var position = NhlApiFiles.Position(p.Str("position"));
        var goalie = position == "G";

        var nhl = new List<(int Season, int Sequence, string? Team, JsonElement Line)>();
        string? college = null;
        if (p.TryGetProperty("seasonTotals", out var totals) && totals.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in totals.EnumerateArray())
            {
                var league = line.Str("leagueAbbrev");
                if (league is "NCAA" && line.Str("teamName") is { } school) college = school;
                if (league != "NHL" || line.Int("gameTypeId") != 2 || line.Int("season") is not { } seasonId) continue;
                var season = NhlCalendar.Season(seasonId);
                if (season > latestSeason) continue;
                var name = line.Str("teamName");
                var key = teams.Key(name);
                if (key is null && name is not null) unknownTeams?.AddOrUpdate(name, 1, (_, n) => n + 1);
                nhl.Add((season, line.Int("sequence") ?? 0, key, line));
            }
        }
        if (nhl.Count == 0) return null;

        var read = nhl.Where(l => l.Season >= firstSeason).ToList();
        if (read.Count == 0) return null;

        var seasons = new List<HistorySeason>();
        foreach (var year in read.GroupBy(l => l.Season).OrderBy(g => g.Key))
        {
            var lines = year.OrderBy(l => l.Sequence).ToList();
            if (lines.Count == 1)
            {
                seasons.Add(Line(year.Key, lines[0].Team, true, [lines[0].Line], goalie));
                continue;
            }
            // A team this build does not know gets no line of its own; its games still count in the season's total.
            seasons.AddRange(lines.Where(l => l.Team is not null).Select(l => Line(year.Key, l.Team, false, [l.Line], goalie)));
            seasons.Add(Line(year.Key, null, true, lines.Select(l => l.Line).ToList(), goalie));
        }

        var awards = Awards(p);
        var draft = p.TryGetProperty("draftDetails", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
        var career = p.TryGetProperty("careerTotals", out var c) && c.TryGetProperty("regularSeason", out var rs) ? rs : default;
        var lastSeason = nhl.Max(l => l.Season);
        var playedSeasons = nhl.Select(l => l.Season).Distinct().Count();
        var recent = seasons.Where(s => s.IsSeasonTotal && s.Season >= latestSeason - 1).ToList();

        var popularity = PopularityCalculator.FromHockeyCareer(new HockeyCareer(
            position,
            CareerPoints: career.Int("points") ?? 0,
            CareerWins: goalie ? career.Int("wins") ?? 0 : 0,
            Seasons: playedSeasons,
            MvpAwards: awards.Count(a => a.Type == AwardTypes.Mvp),
            MajorAwards: awards.Count(a => AwardTypes.MajorHockeyAwards.Contains(a.Type)),
            StanleyCups: awards.Count(a => a.Type == AwardTypes.StanleyCup),
            HallOfFame: p.Int("inHHOF") == 1,
            DraftPick: draft.Int("overallPick"),
            RecentPerGame: lastSeason >= latestSeason - 1 && recent.Count > 0 ? recent.Max(s => RecentPerGame(s, goalie)) : null));

        return new HistoryPlayer(
            ExternalId: id,
            FirstName: p.Str("firstName") ?? "",
            LastName: p.Str("lastName") ?? "",
            Position: position,
            HeightInches: p.Int("heightInInches") is > 0 and var h ? h : null,
            WeightLbs: p.Int("weightInPounds") is > 0 and var w ? w : null,
            BirthDate: NhlApiFiles.Date(p.Str("birthDate")),
            College: college,
            FromYear: nhl.Min(l => l.Season),
            ToYear: lastSeason,
            HallOfFame: p.Int("inHHOF") == 1,
            DraftYear: draft.Int("year"),
            DraftNumber: draft.Int("overallPick"),
            Seasons: seasons,
            Awards: awards,
            JerseyNumber: p.Str("sweaterNumber"),
            HeadshotUrl: p.Str("headshot"),
            Popularity: popularity,
            Country: NhlApiFiles.Country(p.Str("birthCountry")));
    }

    /// <summary>Points a game for a skater; for a goalie, a win rate on about the same scale.</summary>
    private static double RecentPerGame(HistorySeason s, bool goalie)
    {
        if (!goalie) return s.PointsPerGame ?? 0;
        var stats = JsonNode.Parse(s.StatsJson)!.AsObject();
        var games = s.Games ?? 0;
        return games == 0 ? 0 : Math.Round((stats["w"]?.GetValue<double>() ?? 0) / games * 1.6 * Math.Min(games, 50) / 50, 2);
    }

    /// <summary>
    /// One card line from the season lines it covers: one for a team, or all of a traded season's for its total.
    /// A skater's line is goals, assists, points, plus-minus, penalty minutes, power-play and game-winning goals and
    /// shots; a goalie's is starts, wins, losses, ties, overtime losses, goals-against average, save percentage and
    /// shutouts. Zeros are left out, as on the other leagues' cards.
    /// </summary>
    internal static HistorySeason Line(int season, string? team, bool total, IReadOnlyList<JsonElement> lines, bool goalie)
    {
        double Sum(string name) => lines.Sum(l => l.Dbl(name) ?? 0);
        var games = (int)Sum("gamesPlayed");
        var json = new JsonObject();
        void Put(string key, double value, int digits = 0)
        {
            if (value == 0) return;
            json[key] = digits == 0 ? (JsonNode)(int)Math.Round(value) : Math.Round(value, digits);
        }

        if (goalie)
        {
            var against = Sum("goalsAgainst");
            var shots = Sum("shotsAgainst");
            var minutes = lines.Sum(l => NhlApiFiles.Minutes(l.Str("timeOnIce")) ?? 0);
            Put("gs", Sum("gamesStarted"));
            Put("w", Sum("wins"));
            Put("l", Sum("losses"));
            Put("t", Sum("ties"));
            Put("otl", Sum("otLosses"));
            // A single line's own average is used when the minutes are missing, as they are in the oldest records.
            var gaa = minutes > 0 ? against * 60 / minutes : lines.Count == 1 ? lines[0].Dbl("goalsAgainstAvg") ?? 0 : 0;
            Put("gaa", gaa, 2);
            var savePct = shots > 0 ? (shots - against) / shots : lines.Count == 1 ? lines[0].Dbl("savePctg") ?? 0 : 0;
            Put("svp", savePct, 3);
            Put("so", Sum("shutouts"));
            return new HistorySeason(season, team, total, games, null, null, null, null, json.ToJsonString());
        }

        var points = Sum("points");
        Put("g", Sum("goals"));
        Put("a", Sum("assists"));
        Put("pts", points);
        Put("pm", Sum("plusMinus"));
        Put("pim", Sum("pim"));
        Put("ppg", Sum("powerPlayGoals"));
        Put("gwg", Sum("gameWinningGoals"));
        Put("sog", Sum("shots"));
        var toi = lines.Count == 1 ? NhlApiFiles.Minutes(lines[0].Str("avgToi")) : null;
        return new HistorySeason(season, team, total, games, toi is { } m ? Math.Round(m, 1) : null,
            games > 0 ? Math.Round(points / games, 2) : null, null, null, json.ToJsonString());
    }

    /// <summary>The NHL's trophies a card stamps, by the name the site gives them, and the award each one is.</summary>
    internal static readonly (string Name, string Type)[] Trophies =
    [
        ("Hart Memorial Trophy", AwardTypes.Mvp),
        ("Art Ross Trophy", AwardTypes.ArtRoss),
        ("Richard Trophy", AwardTypes.RocketRichard),
        ("James Norris Memorial Trophy", AwardTypes.Norris),
        ("Vezina Trophy", AwardTypes.Vezina),
        ("Calder Memorial Trophy", AwardTypes.Rookie),
        ("Conn Smythe Trophy", AwardTypes.ConnSmythe),
        ("Ted Lindsay Award", AwardTypes.TedLindsay),
        ("Frank J. Selke Trophy", AwardTypes.Selke),
        ("Lady Byng Memorial Trophy", AwardTypes.LadyByng),
        ("Stanley Cup", AwardTypes.StanleyCup),
    ];

    /// <summary>
    /// The player's trophies by season. The Rocket Richard Trophy is matched on "Richard Trophy", since the site
    /// writes its name with curly quotes.
    /// </summary>
    internal static List<HistoryAward> Awards(JsonElement p)
    {
        var awards = new List<HistoryAward>();
        if (!p.TryGetProperty("awards", out var list) || list.ValueKind != JsonValueKind.Array) return awards;
        foreach (var a in list.EnumerateArray())
        {
            var name = a.TryGetProperty("trophy", out var trophy) ? trophy.Str("default") : null;
            if (name is null) continue;
            var type = Trophies.FirstOrDefault(t => name.Contains(t.Name, StringComparison.OrdinalIgnoreCase)).Type;
            if (type is null || !a.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array) continue;
            foreach (var s in seasons.EnumerateArray())
            {
                if (s.Int("seasonId") is not { } seasonId) continue;
                var award = new HistoryAward(NhlCalendar.Season(seasonId), type);
                if (!awards.Contains(award)) awards.Add(award);
            }
        }
        return awards.OrderBy(a => a.Season).ThenBy(a => a.Type, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Each team's regular-season record, with overtime and shootout losses apart from losses, and whether it made the
    /// playoffs. A season with no games (the 2004-05 lockout) has no records.
    /// </summary>
    private async Task<List<HistoryTeamSeason>> ReadRecordsAsync(NhlApiOptions opts, int latestSeason, NhlTeams teams, CancellationToken ct)
    {
        var records = new List<HistoryTeamSeason>();
        for (var season = opts.FirstSeason; season <= latestSeason; season++)
        {
            var filter = $"seasonId={NhlCalendar.SeasonId(season)} and gameTypeId=";
            var regular = await TeamRowsAsync(opts, season, 2, filter + "2", ct);
            var playoffs = (await TeamRowsAsync(opts, season, 3, filter + "3", ct))
                .Select(r => teams.Key(r.Str("teamFullName"))).OfType<string>().ToHashSet();
            records.AddRange(Records(regular, playoffs, season, teams));
        }
        return records;
    }

    private Task<List<JsonElement>> TeamRowsAsync(NhlApiOptions opts, int season, int gameType, string filter, CancellationToken ct) =>
        NhlApiFiles.ReportAsync(http, opts, NhlApiFiles.TeamSeasons(season, gameType), "team/summary", filter, null, ct);

    internal static IEnumerable<HistoryTeamSeason> Records(IEnumerable<JsonElement> rows, IReadOnlySet<string> playoffs, int season, NhlTeams teams)
    {
        foreach (var r in rows)
        {
            if (teams.Key(r.Str("teamFullName")) is not { } key || (r.Int("gamesPlayed") ?? 0) == 0) continue;
            yield return new HistoryTeamSeason(key, season, r.Int("wins") ?? 0, r.Int("losses") ?? 0, playoffs.Contains(key),
                r.Int("ties") ?? 0, r.Int("otLosses") ?? 0);
        }
    }
}

/// <summary>
/// Today's NHL rosters from the NHL's site API, one team at a time. Layered on the full record: it sets each player's
/// team, sweater number and active status and fills in facts the record lacks. A player on no roster today, in the
/// minors or retired, stops being a current player.
/// </summary>
public sealed class NhlRosterSource(HttpClient http, IOptions<NhlApiOptions> options, TimeProvider clock, ILogger<NhlRosterSource> logger)
    : IPlayerSource
{
    public const string SourceName = "nhl";
    public string Name => SourceName;

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var opts = options.Value;
        var teams = NhlTeams.Instance.Value;
        var players = new List<SourcePlayer>();
        foreach (var team in teams.Current.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            using var doc = await NhlApiFiles.OpenAsync(http, opts, NhlApiFiles.Roster(team.Key), $"{opts.WebUrl}roster/{team.Key}/current", cancellationToken)
                            // A missing team would have every one of its players marked as gone, so the night stops instead.
                            ?? throw new InvalidOperationException($"The NHL has no roster for {team.Key}.");
            players.AddRange(ReadRoster(doc.RootElement, team.Key));
        }

        var season = NhlCalendar.RosterSeason(now);
        logger.LogInformation("The NHL lists {Players} players on {Season} rosters", players.Count, season);
        return new SourceSnapshot("nhl", season, teams.CurrentSourceTeams(), players,
            IdSource: NhlHistorySource.IdSource, InSeason: NhlCalendar.InSeason(now), Supplement: true);
    }

    internal static IEnumerable<SourcePlayer> ReadRoster(JsonElement roster, string team)
    {
        foreach (var group in new[] { "forwards", "defensemen", "goalies" })
        {
            if (!roster.TryGetProperty(group, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in list.EnumerateArray())
            {
                if (p.Str("id") is not { } id) continue;
                yield return new SourcePlayer(
                    ExternalId: id,
                    FirstName: p.Str("firstName") ?? "",
                    LastName: p.Str("lastName") ?? "",
                    TeamExternalId: team,
                    Position: NhlApiFiles.Position(p.Str("positionCode")),
                    HeightInches: p.Int("heightInInches") is > 0 and var h ? h : null,
                    WeightLbs: p.Int("weightInPounds") is > 0 and var w ? w : null,
                    JerseyNumber: p.Str("sweaterNumber"),
                    Country: NhlApiFiles.Country(p.Str("birthCountry")),
                    BirthDate: NhlApiFiles.Date(p.Str("birthDate")),
                    HeadshotUrl: p.Str("headshot"));
            }
        }
    }
}

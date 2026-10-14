using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;

namespace KnowBall.Infrastructure.Sources;

public sealed class NbaStatsOptions
{
    public string BaseUrl { get; set; } = "https://stats.nba.com/stats/";
    public string HeadshotUrlTemplate { get; set; } = "https://cdn.nba.com/headshots/nba/latest/1040x760/{0}.png";
    /// <summary>Override the season, e.g. "2025-26". Empty = work it out from today's date.</summary>
    public string? Season { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Reads the public stats.nba.com "playerindex" endpoint (the same one nba.com's player
/// directory uses). One request returns every rostered player with team, bio and per-game stats.
/// Unofficial and unversioned, so parsing is by column name and tolerant of missing columns.
/// For production, swap in a licensed provider behind the same <see cref="IPlayerSource"/>.
/// </summary>
public sealed class NbaStatsPlayerSource(
    HttpClient http,
    IOptions<NbaStatsOptions> options,
    TimeProvider clock,
    ILogger<NbaStatsPlayerSource> logger) : IPlayerSource
{
    public const string SourceName = "nba-stats";
    public string Name => SourceName;

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var seasonStart = string.IsNullOrWhiteSpace(opts.Season)
            ? SeasonCalendar.CurrentSeasonStartYear(clock.GetUtcNow())
            : int.Parse(opts.Season[..4], CultureInfo.InvariantCulture);

        var rows = await FetchSeasonAsync(seasonStart, cancellationToken);

        // Early October: the new season's rosters may not be published yet.
        if (rows.Count == 0 && string.IsNullOrWhiteSpace(opts.Season))
        {
            logger.LogWarning("No players for {Season}; falling back to the previous season", SeasonCalendar.Label(seasonStart));
            seasonStart--;
            rows = await FetchSeasonAsync(seasonStart, cancellationToken);
        }

        var reference = TeamReference.Nba;
        var teams = new Dictionary<string, SourceTeam>();
        var players = new List<SourcePlayer>(rows.Count);

        foreach (var r in rows)
        {
            var personId = r.Str("PERSON_ID");
            if (personId is null) continue;

            var teamId = r.Str("TEAM_ID");
            if (teamId is "0" or "") teamId = null;

            if (teamId is not null && !teams.ContainsKey(teamId))
            {
                var abbr = r.Str("TEAM_ABBREVIATION") ?? "";
                reference.TryGetValue(abbr, out var meta);
                teams[teamId] = new SourceTeam(
                    teamId,
                    r.Str("TEAM_CITY") ?? meta?.City ?? "",
                    r.Str("TEAM_NAME") ?? meta?.Name ?? "",
                    abbr,
                    meta?.Conference,
                    meta?.Division,
                    meta?.Arena,
                    meta?.Latitude,
                    meta?.Longitude);
            }

            players.Add(new SourcePlayer(
                ExternalId: personId,
                FirstName: r.Str("PLAYER_FIRST_NAME") ?? "",
                LastName: r.Str("PLAYER_LAST_NAME") ?? "",
                TeamExternalId: teamId,
                Position: r.Str("POSITION"),
                HeightInches: ParseHeight(r.Str("HEIGHT")),
                WeightLbs: r.Int("WEIGHT"),
                JerseyNumber: r.Str("JERSEY_NUMBER"),
                College: r.Str("COLLEGE"),
                Country: r.Str("COUNTRY"),
                DraftYear: r.Int("DRAFT_YEAR"),
                DraftNumber: r.Int("DRAFT_NUMBER"),
                FromYear: r.Int("FROM_YEAR"),
                ToYear: r.Int("TO_YEAR"),
                PointsPerGame: r.Dbl("PTS"),
                ReboundsPerGame: r.Dbl("REB"),
                AssistsPerGame: r.Dbl("AST"),
                HeadshotUrl: string.Format(CultureInfo.InvariantCulture, opts.HeadshotUrlTemplate, personId)));
        }

        // Teams with no rostered players still exist; add them from the reference list.
        foreach (var t in reference.Values)
        {
            if (teams.Values.Any(x => x.Abbreviation == t.Abbreviation)) continue;
            teams[t.ExternalId] = t.ToSourceTeam();
        }

        logger.LogInformation("nba-stats returned {Players} players on {Teams} teams for {Season}",
            players.Count, teams.Count, SeasonCalendar.Label(seasonStart));

        return new SourceSnapshot("nba", seasonStart, teams.Values.ToList(), players);
    }

    private async Task<List<Row>> FetchSeasonAsync(int seasonStart, CancellationToken ct)
    {
        var season = SeasonCalendar.Label(seasonStart);
        var url = "playerindex?College=&Country=&DraftPick=&DraftRound=&DraftYear=&Height=&Historical=0" +
                  $"&LeagueID=00&Season={season}&SeasonType=Regular%20Season&TeamID=0&Weight=";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
                        ?? throw new InvalidOperationException("Empty response from stats.nba.com");

        return ParseResultSet(doc.RootElement, "PlayerIndex");
    }

    internal static List<Row> ParseResultSet(JsonElement root, string name)
    {
        if (!root.TryGetProperty("resultSets", out var sets))
            throw new InvalidOperationException("stats.nba.com response has no resultSets; the API format may have changed.");

        foreach (var set in sets.EnumerateArray())
        {
            if (set.TryGetProperty("name", out var n) && n.GetString() != name && sets.GetArrayLength() > 1) continue;

            var headers = set.GetProperty("headers").EnumerateArray()
                .Select((h, i) => (h.GetString() ?? "", i))
                .ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase);

            return set.GetProperty("rowSet").EnumerateArray()
                .Select(row => new Row(headers, row.EnumerateArray().Select(c => c.Clone()).ToArray()))
                .ToList();
        }

        return [];
    }

    /// <summary>"6-9" → 81.</summary>
    internal static int? ParseHeight(string? height)
    {
        if (string.IsNullOrWhiteSpace(height)) return null;
        var parts = height.Split(['-', '\'', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var ft)
            && int.TryParse(parts[1].Trim('"'), CultureInfo.InvariantCulture, out var inch))
            return ft * 12 + inch;
        return null;
    }

    internal sealed class Row(Dictionary<string, int> headers, JsonElement[] cells)
    {
        private JsonElement? Cell(string column) =>
            headers.TryGetValue(column, out var i) && i < cells.Length ? cells[i] : null;

        public string? Str(string column) => Cell(column) switch
        {
            { ValueKind: JsonValueKind.String } e => NullIfBlank(e.GetString()),
            { ValueKind: JsonValueKind.Number } e => e.GetRawText(),
            _ => null,
        };

        public int? Int(string column) => Cell(column) switch
        {
            { ValueKind: JsonValueKind.Number } e when e.TryGetInt32(out var v) => v,
            { ValueKind: JsonValueKind.Number } e => (int)Math.Round(e.GetDouble()),
            { ValueKind: JsonValueKind.String } e when int.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var v) => v,
            _ => null,
        };

        public double? Dbl(string column) => Cell(column) switch
        {
            { ValueKind: JsonValueKind.Number } e => e.GetDouble(),
            { ValueKind: JsonValueKind.String } e when double.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var v) => v,
            _ => null,
        };

        private static string? NullIfBlank(string? s) =>
            string.IsNullOrWhiteSpace(s) || s.Equals("Undrafted", StringComparison.OrdinalIgnoreCase) ? null : s.Trim();
    }
}

public static class SeasonCalendar
{
    /// <summary>NBA seasons tip off in October; before that, "current" is last year's season.</summary>
    public static int CurrentSeasonStartYear(DateTimeOffset now) => now.Month >= 10 ? now.Year : now.Year - 1;

    /// <summary>2025 → "2025-26".</summary>
    public static string Label(int startYear) => $"{startYear}-{(startYear + 1) % 100:00}";
}

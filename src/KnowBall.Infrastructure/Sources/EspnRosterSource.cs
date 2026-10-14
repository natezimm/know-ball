using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;

namespace KnowBall.Infrastructure.Sources;

public sealed class EspnOptions
{
    /// <summary>Must end with a slash.</summary>
    public string BaseUrl { get; set; } = "https://site.api.espn.com/apis/site/v2/sports/basketball/nba/";
    /// <summary>A second host with the same data, tried when the first refuses a request. Empty = none.</summary>
    public string? FallbackBaseUrl { get; set; } = "https://site.web.api.espn.com/apis/site/v2/sports/basketball/nba/";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Today's NBA rosters from ESPN's public site feed: one request for the team list and one per
/// team. It needs no key but is unofficial, so every field is optional and both of ESPN's hosts
/// are tried. It knows who is on which team and what number they wear; the player history comes
/// from elsewhere, so the snapshot is marked as a supplement.
/// </summary>
public sealed class EspnRosterSource(
    HttpClient http,
    IOptions<EspnOptions> options,
    TimeProvider clock,
    ILogger<EspnRosterSource> logger) : IPlayerSource
{
    public const string SourceName = "espn";
    public string Name => SourceName;

    /// <summary>ESPN's abbreviations where they differ from the NBA's.</summary>
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GS"] = "GSW", ["NO"] = "NOP", ["NY"] = "NYK", ["SA"] = "SAS", ["UTAH"] = "UTA", ["WSH"] = "WAS",
    };

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        using var teamList = await GetAsync("teams?limit=50", cancellationToken);
        var espnTeams = ParseTeams(teamList.RootElement);
        if (espnTeams.Count == 0) throw new InvalidOperationException("ESPN returned no teams; the feed format may have changed.");

        var reference = TeamReference.Nba;
        var players = new List<SourcePlayer>();
        int? seasonStart = null;
        var inSeason = true;

        foreach (var (espnId, abbreviation) in espnTeams)
        {
            if (!reference.TryGetValue(Abbreviations.GetValueOrDefault(abbreviation, abbreviation), out var team))
                throw new InvalidOperationException($"ESPN team '{abbreviation}' is not a known NBA team.");

            using var roster = await GetAsync($"teams/{espnId}/roster", cancellationToken);
            var parsed = ParseRoster(roster.RootElement, team.ExternalId);
            // An empty roster is a broken response, and treating it as real would release the whole team.
            if (parsed.Players.Count == 0) throw new InvalidOperationException($"ESPN returned no players for {team.Abbreviation}.");

            players.AddRange(parsed.Players);
            if (seasonStart is null && parsed.SeasonStart is { } start)
            {
                seasonStart = start;
                inSeason = parsed.InSeason;
            }
        }

        var season = seasonStart ?? SeasonCalendar.CurrentSeasonStartYear(clock.GetUtcNow());
        logger.LogInformation("espn returned {Players} players on {Teams} teams for {Season}",
            players.Count, espnTeams.Count, SeasonCalendar.Label(season));

        return new SourceSnapshot(
            "nba", season, reference.Values.Select(t => t.ToSourceTeam()).ToList(), players,
            IdSource: SourceName, InSeason: inSeason, Supplement: true);
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        var opts = options.Value;
        var hosts = new[] { opts.BaseUrl, opts.FallbackBaseUrl }.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();

        Exception? last = null;
        foreach (var host in hosts)
        {
            try
            {
                using var response = await http.GetAsync(new Uri(new Uri(host!), path), ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
                       ?? throw new InvalidOperationException($"Empty response from {host}{path}");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                last = ex;
                logger.LogWarning("ESPN request {Host}{Path} failed: {Error}", host, path, ex.Message);
            }
        }
        throw new HttpRequestException($"ESPN request '{path}' failed on every host: {last?.Message}", last);
    }

    /// <summary>ESPN team ids and abbreviations from the team list.</summary>
    internal static List<(string Id, string Abbreviation)> ParseTeams(JsonElement root)
    {
        var teams = new List<(string, string)>();
        if (!root.TryGetProperty("sports", out var sports)) return teams;

        foreach (var sport in sports.EnumerateArray())
        foreach (var league in Array(sport, "leagues"))
        foreach (var entry in Array(league, "teams"))
        {
            if (!entry.TryGetProperty("team", out var team)) continue;
            if (Str(team, "id") is { } id && Str(team, "abbreviation") is { } abbreviation) teams.Add((id, abbreviation));
        }
        return teams;
    }

    internal sealed record Roster(List<SourcePlayer> Players, int? SeasonStart, bool InSeason);

    internal static Roster ParseRoster(JsonElement root, string teamExternalId)
    {
        // "season": { "year": 2027, "type": 1 } is the 2026-27 preseason; 2 and 3 are the regular season and playoffs.
        int? seasonStart = null;
        var inSeason = true;
        if (root.TryGetProperty("season", out var season) && season.ValueKind == JsonValueKind.Object)
        {
            if (Int(season, "year") is { } year) seasonStart = year - 1;
            if (Int(season, "type") is { } type) inSeason = type is 2 or 3;
        }

        var players = new List<SourcePlayer>();
        foreach (var a in Array(root, "athletes"))
        {
            if (Str(a, "id") is not { } id) continue;
            var first = Str(a, "firstName") ?? "";
            var last = Str(a, "lastName") ?? "";
            if (first.Length == 0 && last.Length == 0)
            {
                if (Str(a, "fullName") is not { } full) continue;
                (first, last) = BrefCsvSource.SplitName(full);
            }

            var birthPlace = Object(a, "birthPlace");
            var years = Object(a, "experience") is { } experience ? Int(experience, "years") : null;

            players.Add(new SourcePlayer(
                ExternalId: id,
                FirstName: first,
                LastName: last,
                TeamExternalId: teamExternalId,
                Position: Object(a, "position") is { } position ? Str(position, "abbreviation") : null,
                HeightInches: Dbl(a, "height") is { } height and > 0 ? (int)Math.Round(height) : null,
                WeightLbs: Dbl(a, "weight") is { } weight and > 0 ? (int)Math.Round(weight) : null,
                JerseyNumber: Str(a, "jersey"),
                College: Object(a, "college") is { } college ? Str(college, "name") : null,
                Country: birthPlace is { } place ? Str(place, "country") : null,
                FromYear: seasonStart is { } start && years is { } y ? start - y : null,
                BirthDate: ParseDate(Str(a, "dateOfBirth")),
                HeadshotUrl: Object(a, "headshot") is { } headshot ? Str(headshot, "href") : null,
                BirthPlace: birthPlace is { } p ? Str(p, "city") : null));
        }
        return new Roster(players, seasonStart, inSeason);
    }

    /// <summary>"1999-02-28T08:00Z" → 28 February 1999.</summary>
    private static DateOnly? ParseDate(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? Str(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static double? Dbl(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? Int(JsonElement parent, string name) =>
        Dbl(parent, name) is { } value ? (int)Math.Round(value) : null;
}

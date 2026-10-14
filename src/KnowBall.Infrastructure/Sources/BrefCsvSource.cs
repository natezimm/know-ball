using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;

namespace KnowBall.Infrastructure.Sources;

public sealed class BrefOptions
{
    /// <summary>Where the CSV files are downloaded from. Must end with a slash.</summary>
    public string BaseUrl { get; set; } = "https://raw.githubusercontent.com/sumitrodatta/bball-reference-datasets/master/Data/";
    /// <summary>Read the files from this folder instead of downloading them. Used by tests and offline runs.</summary>
    public string? Directory { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// The NBA's full record, from a public CSV mirror of Basketball-Reference: every player since
/// 1947 with season lines, All-Star picks, awards and draft slots. The mirror is refreshed a few
/// times a year, so this is loaded occasionally and the nightly roster feed covers the gap.
/// </summary>
public sealed partial class BrefCsvSource(HttpClient http, IOptions<BrefOptions> options, ILogger<BrefCsvSource> logger) : IHistorySource
{
    public const string SourceName = "bref";
    public string Name => SourceName;

    private const string CareerFile = "Player Career Info.csv";
    private const string PerGameFile = "Player Per Game.csv";
    private const string AllStarFile = "All-Star Selections.csv";
    private const string SeasonTeamsFile = "End of Season Teams.csv";
    private const string AwardsFile = "Player Award Shares.csv";
    private const string DraftFile = "Draft Pick History.csv";
    private const string TeamsFile = "Team Abbrev.csv";

    public async Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, List<CsvRow>>();
        foreach (var name in new[] { CareerFile, PerGameFile, AllStarFile, SeasonTeamsFile, AwardsFile, DraftFile, TeamsFile })
        {
            await using var stream = await OpenAsync(name, cancellationToken);
            files[name] = await CsvReader.ReadAsync(stream, cancellationToken);
            if (files[name].Count == 0) throw new InvalidOperationException($"{name} is empty.");
        }

        var snapshot = Build(
            files[CareerFile], files[PerGameFile], files[AllStarFile], files[SeasonTeamsFile],
            files[AwardsFile], files[DraftFile], files[TeamsFile]);

        logger.LogInformation("bref has {Players} players and {Teams} teams through {Season}",
            snapshot.Players.Count, snapshot.Teams.Count, SeasonCalendar.Label(snapshot.LatestSeason));
        return snapshot;
    }

    private async Task<Stream> OpenAsync(string fileName, CancellationToken ct)
    {
        var opts = options.Value;
        if (!string.IsNullOrWhiteSpace(opts.Directory))
            return File.OpenRead(Path.Combine(opts.Directory, fileName));

        var response = await http.GetAsync(new Uri(new Uri(opts.BaseUrl), Uri.EscapeDataString(fileName)), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    internal static HistorySnapshot Build(
        List<CsvRow> career, List<CsvRow> perGame, List<CsvRow> allStars, List<CsvRow> seasonTeams,
        List<CsvRow> awardShares, List<CsvRow> draft, List<CsvRow> teamCodes)
    {
        var map = TeamHistory.Value;

        // The files name a season by the year it ends; everything here uses the year it starts.
        var latestSeason = perGame.Max(r => r.Int("season") ?? 0) - 1;
        var teams = BuildTeams(teamCodes, map, latestSeason);

        var seasonsByPlayer = perGame
            .Where(r => r.Str("player_id") is not null && r.Int("season") is not null)
            .GroupBy(r => r.Str("player_id")!)
            .ToDictionary(g => g.Key, g => BuildSeasons(g, map));

        var awards = new Dictionary<string, HashSet<HistoryAward>>();
        void AddAward(CsvRow row, string? type)
        {
            if (type is null || row.Str("player_id") is not { } id || row.Int("season") is not { } season) return;
            if (!awards.TryGetValue(id, out var set)) awards[id] = set = [];
            set.Add(new HistoryAward(season - 1, type));
        }

        foreach (var r in allStars) AddAward(r, AwardTypes.AllStar);
        foreach (var r in awardShares.Where(r => r.Flag("winner"))) AddAward(r, AwardType(r.Str("award")));
        foreach (var r in seasonTeams) AddAward(r, TeamAwardType(r.Str("type"), r.Str("number_tm")));

        // A player drafted twice signed after the later draft.
        var drafts = draft
            .Where(r => r.Str("player_id") is not null && r.Int("season") is not null)
            .GroupBy(r => r.Str("player_id")!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Int("season")).First());

        var players = new List<HistoryPlayer>(career.Count);
        foreach (var r in career)
        {
            if (r.Str("player_id") is not { } id || r.Str("player") is not { } name) continue;

            var (first, last) = SplitName(name);
            drafts.TryGetValue(id, out var pick);
            players.Add(new HistoryPlayer(
                ExternalId: id,
                FirstName: first,
                LastName: last,
                Position: r.Str("pos"),
                HeightInches: r.Int("ht_in_in"),
                WeightLbs: r.Int("wt"),
                BirthDate: DateOnly.TryParseExact(r.Str("birth_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var born) ? born : null,
                // Listed in the order attended; the last is the one the player left for the pros.
                College: r.Str("colleges")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                FromYear: r.Int("from") - 1,
                ToYear: r.Int("to") - 1,
                HallOfFame: r.Flag("hof"),
                DraftYear: pick?.Int("season"),
                DraftNumber: pick?.Int("overall_pick"),
                Seasons: seasonsByPlayer.GetValueOrDefault(id) ?? [],
                Awards: awards.TryGetValue(id, out var won) ? won.OrderBy(a => a.Season).ThenBy(a => a.Type, StringComparer.Ordinal).ToList() : []));
        }

        return new HistorySnapshot("nba", SourceName, latestSeason, teams, players);
    }

    private static List<HistoryTeam> BuildTeams(List<CsvRow> teamCodes, TeamHistoryMap map, int latestSeason)
    {
        var spans = new Dictionary<string, (int From, int To, string FullName)>();
        foreach (var r in teamCodes)
        {
            if (r.Str("abbreviation") is not { } code || r.Int("season") is not { } endYear) continue;
            var key = map.Identity(code);
            var season = endYear - 1;
            spans[key] = spans.TryGetValue(key, out var span)
                ? (Math.Min(span.From, season), Math.Max(span.To, season), span.FullName)
                : (season, season, r.Str("team") ?? code);
        }

        var teams = new List<HistoryTeam>(spans.Count);
        foreach (var (key, span) in spans)
        {
            int? activeTo = span.To >= latestSeason ? null : span.To;

            if (TeamReference.Nba.TryGetValue(map.Current.GetValueOrDefault(key, key), out var current) && !map.Former.ContainsKey(key))
            {
                teams.Add(new HistoryTeam(key, current.ToSourceTeam(), span.From, activeTo, null));
            }
            else if (map.Former.TryGetValue(key, out var former))
            {
                teams.Add(new HistoryTeam(key, new SourceTeam($"bref-{key}", former.City, former.Name, key), span.From, activeTo, former.Franchise));
            }
            else
            {
                // A code this build has never seen (a new or renamed team): keep the name as given.
                teams.Add(new HistoryTeam(key, new SourceTeam($"bref-{key}", "", span.FullName, key), span.From, activeTo, null));
            }
        }
        return teams;
    }

    /// <summary>
    /// A player's lines, oldest season first. Within a season the file lists the combined line
    /// of a traded player first and then each team in the order he played for them.
    /// </summary>
    private static List<HistorySeason> BuildSeasons(IEnumerable<CsvRow> rows, TeamHistoryMap map)
    {
        var seasons = new List<HistorySeason>();
        foreach (var group in rows.GroupBy(r => r.Int("season")!.Value).OrderBy(g => g.Key))
        {
            var lines = group.ToList();
            foreach (var r in lines)
            {
                var code = r.Str("team");
                var combined = code is null || CombinedCode().IsMatch(code);
                seasons.Add(new HistorySeason(
                    Season: group.Key - 1,
                    TeamKey: combined ? null : map.Identity(code!),
                    IsSeasonTotal: combined || lines.Count == 1,
                    Games: r.Int("g"),
                    MinutesPerGame: r.Dbl("mp_per_game"),
                    PointsPerGame: r.Dbl("pts_per_game"),
                    ReboundsPerGame: r.Dbl("trb_per_game"),
                    AssistsPerGame: r.Dbl("ast_per_game"),
                    StatsJson: ExtraStats(r)));
            }
        }
        return seasons;
    }

    private static string ExtraStats(CsvRow r)
    {
        var extras = new SortedDictionary<string, double>(StringComparer.Ordinal);
        void Add(string key, string column)
        {
            if (r.Dbl(column) is { } value) extras[key] = value;
        }

        Add("gs", "gs");
        Add("stl", "stl_per_game");
        Add("blk", "blk_per_game");
        Add("fg", "fg_percent");
        Add("tp", "x3p_percent");
        Add("ft", "ft_percent");
        return JsonSerializer.Serialize(extras);
    }

    private static string? AwardType(string? award) => award?.Split(' ').LastOrDefault() switch
    {
        "mvp" => AwardTypes.Mvp,
        "dpoy" => AwardTypes.DefensivePlayer,
        "roy" => AwardTypes.Rookie,
        "smoy" => AwardTypes.SixthMan,
        "mip" => AwardTypes.MostImproved,
        "clutch_poy" => AwardTypes.Clutch,
        _ => null,
    };

    private static string? TeamAwardType(string? type, string? number)
    {
        var rank = number?.TrimEnd('s', 't', 'n', 'd', 'r', 'h');
        if (rank is not ("1" or "2" or "3")) return null;
        return type switch
        {
            "All-NBA" or "All-ABA" or "All-BAA" => AwardTypes.AllLeague + rank,
            "All-Defense" => AwardTypes.AllDefense + rank,
            "All-Rookie" => AwardTypes.AllRookie + rank,
            _ => null,
        };
    }

    /// <summary>"Shai Gilgeous-Alexander" → ("Shai", "Gilgeous-Alexander"); a single name has no last name.</summary>
    internal static (string First, string Last) SplitName(string fullName)
    {
        var name = fullName.Trim();
        var space = name.IndexOf(' ');
        return space < 0 ? (name, "") : (name[..space], name[(space + 1)..].Trim());
    }

    [GeneratedRegex(@"^(\dTM|TOT)$")]
    private static partial Regex CombinedCode();

    private static readonly Lazy<TeamHistoryMap> TeamHistory = new(TeamHistoryMap.Load);

    internal sealed record FormerTeam(string Code, string City, string Name, string? Franchise);

    internal sealed record TeamHistoryMap(
        Dictionary<string, string> Current,
        Dictionary<string, string> Aliases,
        Dictionary<string, FormerTeam> Former)
    {
        /// <summary>The code a team is filed under: "INA" (the ABA Pacers) is "IND".</summary>
        public string Identity(string code) => Aliases.GetValueOrDefault(code, code);

        public static TeamHistoryMap Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("nba-team-history.json", StringComparison.Ordinal));
            using var stream = asm.GetManifestResourceStream(resource)!;
            var file = JsonSerializer.Deserialize<TeamHistoryFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                       ?? throw new InvalidOperationException("nba-team-history.json is empty");
            return new TeamHistoryMap(file.Current, file.Aliases, file.Former.ToDictionary(f => f.Code));
        }

        private sealed record TeamHistoryFile(Dictionary<string, string> Current, Dictionary<string, string> Aliases, List<FormerTeam> Former);
    }
}

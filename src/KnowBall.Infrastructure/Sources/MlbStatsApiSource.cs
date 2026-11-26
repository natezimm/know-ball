using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;

namespace KnowBall.Infrastructure.Sources;

public sealed class MlbStatsOptions
{
    /// <summary>MLB's Stats API. Must end with a slash.</summary>
    public string BaseUrl { get; set; } = "https://statsapi.mlb.com/api/v1/";
    /// <summary>
    /// Read saved responses from this folder instead of the web: teams/{season}.json, players/{season}.json,
    /// stats/{hitting|pitching|fielding}/{season}.json, standings/{season}.json, awards/{id}.json, draft/{year}.json and
    /// rosters/{teamId}.json. Used by tests and offline runs; a draft, standings or awards file that is missing is skipped.
    /// </summary>
    public string? Directory { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>The first season read. A career that ends in 1990 or later began in 1965 or later, almost always.</summary>
    public int FirstSeason { get; set; } = 1960;
    /// <summary>The first draft read for pick numbers and colleges: the draft began in 1965.</summary>
    public int FirstDraft { get; set; } = 1965;
    /// <summary>
    /// Players with fewer seasons than this in the majors are left out, unless they played this season: a September
    /// call-up is nobody's guess, and leaving thousands of them out keeps the import small.
    /// </summary>
    public int MinSeasons { get; set; } = 2;
    /// <summary>Rows asked for in one page of season stats; the API's whole season fits in one.</summary>
    public int PageSize { get; set; } = 5000;
    /// <summary>Traded players whose year-by-year lines are asked for in one request.</summary>
    public int CareerBatchSize { get; set; } = 25;
}

/// <summary>MLB dates: when a season starts, and which season a day belongs to.</summary>
public static class MlbCalendar
{
    /// <summary>The newest season that has started: opening day is at the end of March.</summary>
    public static int LatestPlayedSeason(DateTimeOffset now) => now.Month >= 4 ? now.Year : now.Year - 1;

    /// <summary>The season rosters are being built for: after the World Series, signings are for next year.</summary>
    public static int RosterSeason(DateTimeOffset now) => now.Month >= 11 ? now.Year + 1 : now.Year;

    /// <summary>From opening day in late March to the World Series in October.</summary>
    public static bool InSeason(DateTimeOffset now) => now.Month is >= 4 and <= 10;
}

/// <summary>Reads the Stats API's responses from the web or from a folder of saved ones.</summary>
internal static class MlbStatsFiles
{
    public static string TeamsPath(int season) => $"teams/{season}.json";
    public static string PlayersPath(int season) => $"players/{season}.json";
    public static string StatsPath(string group, int season) => $"stats/{group}/{season}.json";
    public static string StandingsPath(int season) => $"standings/{season}.json";
    public static string AwardPath(string id) => $"awards/{id}.json";
    public static string DraftPath(int year) => $"draft/{year}.json";
    public static string RosterPath(string teamId) => $"rosters/{teamId}.json";
    /// <summary>One player's year-by-year lines, which split a traded season by team; asked for in batches on the web.</summary>
    public static string CareerPath(string personId) => $"careers/{personId}.json";

    /// <summary>Several players' year-by-year lines in one request: the hitting, pitching and fielding lists of each.</summary>
    public static string CareersUrl(IEnumerable<string> personIds) =>
        $"people?personIds={string.Join(',', personIds)}&hydrate=stats(group=[hitting,pitching,fielding],type=[yearByYear],sportId=1)";

    /// <summary>The Stats API address for a saved-file path.</summary>
    public static string UrlFor(string path, int pageSize, int offset = 0)
    {
        var parts = path[..^".json".Length].Split('/');
        return parts switch
        {
            ["teams", var s] => $"teams?sportId=1&season={s}",
            ["players", var s] => $"sports/1/players?season={s}",
            ["stats", var group, var s] => $"stats?stats=season&group={group}&season={s}&sportId=1&playerPool=ALL&gameType=R&limit={pageSize}&offset={offset}",
            ["standings", var s] => $"standings?leagueId=103,104&season={s}&standingsTypes=regularSeason",
            ["awards", var id] => $"awards/{id}/recipients?sportId=1",
            ["draft", var y] => $"draft/{y}",
            ["rosters", var team] => $"teams/{team}/roster?rosterType=40Man&hydrate=person",
            _ => throw new ArgumentException($"No Stats API address for {path}"),
        };
    }

    /// <summary>The parsed response, or null when it does not exist (a 404, or a saved file that is not there).</summary>
    public static async Task<JsonDocument?> ReadAsync(HttpClient http, MlbStatsOptions opts, string path, CancellationToken ct, int offset = 0)
    {
        if (!string.IsNullOrWhiteSpace(opts.Directory))
        {
            if (offset > 0) return null;
            var local = Path.Combine(opts.Directory, path);
            if (!File.Exists(local)) return null;
            await using var file = File.OpenRead(local);
            return await JsonDocument.ParseAsync(file, cancellationToken: ct);
        }

        using var response = await http.GetAsync(new Uri(new Uri(opts.BaseUrl), UrlFor(path, opts.PageSize, offset)), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    public static async Task<JsonDocument> ReadRequiredAsync(HttpClient http, MlbStatsOptions opts, string path, CancellationToken ct) =>
        await ReadAsync(http, opts, path, ct) ?? throw new InvalidOperationException($"MLB Stats API has no {path}.");

    public static string HeadshotUrl(string personId) =>
        $"https://img.mlbstatic.com/mlb-photos/image/upload/d_people:generic:headshot:67:current.png/w_213,q_auto:best/v1/people/{personId}/headshot/67/current";

    public static string LogoUrl(string teamId) => $"https://www.mlbstatic.com/team-logos/{teamId}.svg";

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    public static int? Int(JsonElement e, string name)
    {
        var s = Str(e, name);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : [];

    /// <summary>"AL" for the American League (103), "NL" for the National (104).</summary>
    public static string? LeagueOf(JsonElement league) => Int(league, "id") switch
    {
        103 => "AL",
        104 => "NL",
        _ => null,
    };

    /// <summary>"American League East" → "AL East"; null before divisions (1969).</summary>
    public static string? DivisionOf(JsonElement division)
    {
        var name = Str(division, "name");
        if (name is null) return null;
        return name.Replace("American League", "AL", StringComparison.Ordinal).Replace("National League", "NL", StringComparison.Ordinal);
    }

    /// <summary>A person's facts as the API gives them, in the players list and in a roster's hydrated person.</summary>
    public static MlbPerson? Person(JsonElement p)
    {
        if (Str(p, "id") is not { } id || Str(p, "fullName") is not { } name) return null;
        // "Oak Park, IL" in the US and Canada, "Santo Domingo, Dominican Republic" elsewhere.
        var place = string.Join(", ", new[] { Str(p, "birthCity"), Str(p, "birthStateProvince") ?? Str(p, "birthCountry") }.Where(s => s is not null));
        return new MlbPerson(
            id, name,
            Str(Obj(p, "primaryPosition"), "abbreviation"),
            NflverseFiles.Height(Str(p, "height")),
            Int(p, "weight"),
            NflverseFiles.Date(Str(p, "birthDate")),
            Str(p, "primaryNumber"),
            Str(p, "birthCountry"),
            place.Length == 0 ? null : place,
            Int(p, "draftYear"),
            NflverseFiles.Date(Str(p, "mlbDebutDate"))?.Year,
            Str(Obj(p, "currentTeam"), "id"),
            p.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True);
    }
}

/// <param name="Position">The API's abbreviation: P, C, 1B, SS, LF, OF, DH, TWP.</param>
/// <param name="DebutYear">Year of his first game in the majors.</param>
internal sealed record MlbPerson(
    string Id, string FullName, string? Position, int? Height, int? Weight, DateOnly? BirthDate, string? Number,
    string? Country, string? BirthPlace, int? DraftYear, int? DebutYear, string? CurrentTeamId, bool Active);

/// <summary>
/// The teams of the majors and the names they have played under. The API gives every franchise one id for life (the
/// Expos and the Nationals are both 120), so a name is told apart by the seasons it was used: the current name is filed
/// under the bare id, "120", and an earlier one under the id and its first season, "120-1969".
/// </summary>
internal sealed class MlbTeams
{
    internal sealed record Identity(string TeamId, string Name, string City, string ClubName, string Abbreviation, string? League, string? Division, string? Venue, int From, int To);

    private readonly Dictionary<string, List<Identity>> _byTeam = [];

    public int LatestSeason { get; private set; }

    /// <summary>Adds one season's list of teams from <c>teams?season=</c>.</summary>
    public void AddSeason(int season, JsonElement root)
    {
        foreach (var t in MlbStatsFiles.Array(root, "teams"))
        {
            if (MlbStatsFiles.Str(t, "id") is not { } id || MlbStatsFiles.Str(t, "name") is not { } name) continue;
            if (MlbStatsFiles.Int(MlbStatsFiles.Obj(t, "sport"), "id") is { } sport && sport != 1) continue;
            var club = MlbStatsFiles.Str(t, "clubName") ?? MlbStatsFiles.Str(t, "teamName") ?? name;
            var city = name.EndsWith(club, StringComparison.Ordinal) ? name[..^club.Length].Trim() : MlbStatsFiles.Str(t, "shortName") ?? "";
            var seen = new Identity(id, name, city, club, MlbStatsFiles.Str(t, "abbreviation") ?? id,
                MlbStatsFiles.LeagueOf(MlbStatsFiles.Obj(t, "league")), MlbStatsFiles.DivisionOf(MlbStatsFiles.Obj(t, "division")),
                MlbStatsFiles.Str(MlbStatsFiles.Obj(t, "venue"), "name"), season, season);

            if (!_byTeam.TryGetValue(id, out var names)) _byTeam[id] = names = [];
            // Only a run of seasons is one name: the Los Angeles Angels of 1961-64 are not the ones of 2016 on, with the
            // California and Anaheim years between.
            var same = names.FindIndex(n => n.Name == name && season >= n.From - 1 && season <= n.To + 1);
            if (same < 0) names.Add(seen);
            else
            {
                var n = names[same];
                // The newest season's facts (division, ballpark, abbreviation) stand for the name.
                names[same] = season >= n.To
                    ? seen with { From = Math.Min(n.From, season) }
                    : n with { From = Math.Min(n.From, season) };
            }
        }
        LatestSeason = Math.Max(LatestSeason, season);
    }

    /// <summary>The key a team is filed under in that season, or null for a team id this build has not seen.</summary>
    public string? Key(string? teamId, int season)
    {
        if (teamId is null || !_byTeam.TryGetValue(teamId, out var names)) return null;
        var current = Current(names);
        var named = names.FirstOrDefault(n => n.From <= season && season <= n.To)
                    ?? names.OrderBy(n => Math.Min(Math.Abs(n.From - season), Math.Abs(n.To - season))).First();
        return named == current ? teamId : $"{teamId}-{named.From}";
    }

    /// <summary>Whether a key is a team still playing under that name.</summary>
    public bool IsCurrent(string key) => !key.Contains('-', StringComparison.Ordinal) && _byTeam.ContainsKey(key);

    /// <summary>The teams playing in the newest season read, filed under their ids.</summary>
    public IReadOnlyList<SourceTeam> CurrentSourceTeams() =>
        _byTeam.Where(t => Current(t.Value).To >= LatestSeason).Select(t => ToSourceTeam(t.Key, Current(t.Value), Current(t.Value))).ToList();

    /// <summary>Every name seen, current ones first; a former name points at the team it became.</summary>
    public List<HistoryTeam> HistoryTeams()
    {
        var teams = new List<HistoryTeam>();
        foreach (var (id, names) in _byTeam.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var current = Current(names);
            var gone = current.To < LatestSeason;
            teams.Add(new HistoryTeam(id, ToSourceTeam(id, current, current), current.From, gone ? current.To : null, null));
            foreach (var n in names.Where(n => n != current).OrderBy(n => n.From))
            {
                var key = $"{id}-{n.From}";
                teams.Add(new HistoryTeam(key, ToSourceTeam(key, n, current), n.From, n.To, id));
            }
        }
        return teams;
    }

    private static Identity Current(List<Identity> names) => names.MaxBy(n => n.To)!;

    /// <summary>
    /// A former name takes the current team's league and division, and its logo only when the club's name has not
    /// changed: the California Angels wore the halo, the Montreal Expos did not wear the Nationals' curly W.
    /// </summary>
    private static SourceTeam ToSourceTeam(string key, Identity n, Identity current) => new(
        key, n.City, n.ClubName, n.Abbreviation,
        current.League, current.Division,
        Arena: n.Venue,
        LogoUrl: n.ClubName == current.ClubName ? MlbStatsFiles.LogoUrl(current.TeamId) : null);
}

/// <summary>
/// The majors' full record from MLB's Stats API: every player since <see cref="MlbStatsOptions.FirstSeason"/> with bio
/// facts and headshot, a line for each team he played for each season (hitting, pitching and the games at each position
/// in the field), his draft slot and college, the major awards and the Hall of Fame, and each team's record. The fame
/// score is worked out here from the career (<see cref="PopularityCalculator.FromBaseballCareer"/>).
/// </summary>
public sealed class MlbStatsHistorySource(HttpClient http, IOptions<MlbStatsOptions> options, TimeProvider clock, ILogger<MlbStatsHistorySource> logger)
    : IHistorySource
{
    public const string SourceName = "mlb-statsapi-history";
    public const string IdSource = "mlbam";
    public string Name => SourceName;

    /// <summary>The Stats API's award ids read, and the award each one is. Hall of Fame is read on its own.</summary>
    internal static readonly (string Id, string Type)[] Awards =
    [
        ("ALMVP", AwardTypes.Mvp), ("NLMVP", AwardTypes.Mvp),
        ("ALCY", AwardTypes.CyYoung), ("NLCY", AwardTypes.CyYoung),
        ("ALROY", AwardTypes.Rookie), ("NLROY", AwardTypes.Rookie),
        ("WSMVP", AwardTypes.WorldSeriesMvp),
        ("ALAS", AwardTypes.AllStar), ("NLAS", AwardTypes.AllStar),
        ("ALGG", AwardTypes.GoldGlove), ("NLGG", AwardTypes.GoldGlove),
        ("ALSS", AwardTypes.SilverSlugger), ("NLSS", AwardTypes.SilverSlugger),
    ];

    public const string HallOfFameAward = "MLBHOF";

    /// <summary>One player's season for one team, as the three stat lists give it.</summary>
    internal sealed class TeamLine(int season, string teamId, int order)
    {
        public int Season { get; } = season;
        public string TeamId { get; } = teamId;
        /// <summary>Where the line came in the API's list: the fallback order for a season split between teams.</summary>
        public int Order { get; set; } = order;
        public int HitGames { get; set; }
        public int PitchGames { get; set; }
        public Dictionary<string, double> Totals { get; } = [];
        public Dictionary<string, int> Fielding { get; } = [];
        /// <summary>
        /// True for a season list's row of a traded player: his whole season, filed under the team he ended it with.
        /// It is replaced by his year-by-year lines, which split the season by team, when those can be read.
        /// </summary>
        public bool Combined { get; set; }
        public int Games => Math.Max(HitGames, PitchGames);
    }

    internal sealed record DraftPick(int Year, int Pick, string? College);

    public async Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var latest = MlbCalendar.LatestPlayedSeason(clock.GetUtcNow());
        var teams = new MlbTeams();
        var people = new Dictionary<string, MlbPerson>();
        var lines = new Dictionary<string, List<TeamLine>>();
        var records = new List<HistoryTeamSeason>();

        var newest = opts.FirstSeason;
        for (var season = opts.FirstSeason; season <= latest; season++)
        {
            using (var teamDoc = await MlbStatsFiles.ReadAsync(http, opts, MlbStatsFiles.TeamsPath(season), cancellationToken))
            {
                if (teamDoc is null)
                {
                    if (season < latest) throw new InvalidOperationException($"MLB Stats API has no teams for {season}.");
                    break;
                }
                teams.AddSeason(season, teamDoc.RootElement);
            }

            var rows = await ReadStatsAsync(season, lines, cancellationToken);
            if (rows == 0)
            {
                if (season < latest) throw new InvalidOperationException($"MLB Stats API has no stat lines for {season}.");
                logger.LogInformation("MLB Stats API has no {Season} stats yet; the record ends with {Previous}", season, season - 1);
                break;
            }
            newest = season;

            using (var playersDoc = await MlbStatsFiles.ReadRequiredAsync(http, opts, MlbStatsFiles.PlayersPath(season), cancellationToken))
            {
                // A later season's facts replace an earlier one's: the newest number, position and team stand.
                foreach (var p in MlbStatsFiles.Array(playersDoc.RootElement, "people"))
                {
                    if (MlbStatsFiles.Person(p) is { } person) people[person.Id] = person;
                }
            }

            using var standings = await MlbStatsFiles.ReadAsync(http, opts, MlbStatsFiles.StandingsPath(season), cancellationToken);
            if (standings is not null) records.AddRange(Records(standings.RootElement, season));
        }

        await SplitTradesAsync(lines, cancellationToken);
        var draft = await ReadDraftAsync(opts.FirstDraft, newest, cancellationToken);
        var (awards, hallOfFame) = await ReadAwardsAsync(cancellationToken);
        // Records are filed under team keys only now that every season's names are known.
        var filed = records
            .Select(r => teams.Key(r.TeamKey, r.Season) is { } key ? r with { TeamKey = key } : null)
            .OfType<HistoryTeamSeason>()
            .ToList();
        var snapshot = Build(people, lines, draft, awards, hallOfFame, teams, newest, opts.MinSeasons) with { TeamSeasons = filed };
        logger.LogInformation("MLB Stats API has {Players} players and {Teams} teams from {First} through {Season}",
            snapshot.Players.Count, snapshot.Teams.Count, opts.FirstSeason, newest);
        return snapshot;
    }

    /// <summary>Reads a season's hitting, pitching and fielding lists into <paramref name="lines"/>; returns the rows read.</summary>
    private async Task<int> ReadStatsAsync(int season, Dictionary<string, List<TeamLine>> lines, CancellationToken ct)
    {
        var rows = 0;
        foreach (var group in new[] { "hitting", "pitching", "fielding" })
        {
            var offset = 0;
            while (true)
            {
                using var doc = await MlbStatsFiles.ReadAsync(http, options.Value, MlbStatsFiles.StatsPath(group, season), ct, offset);
                if (doc is null) break;
                var stats = MlbStatsFiles.Array(doc.RootElement, "stats").FirstOrDefault();
                var splits = MlbStatsFiles.Array(stats, "splits").ToList();
                foreach (var split in splits)
                {
                    if (AddSplit(lines, group, season, split, rows)) rows++;
                }
                offset += splits.Count;
                // One page holds a whole season; a larger one is read on until the total is reached.
                if (splits.Count == 0 || MlbStatsFiles.Int(stats, "totalSplits") is not { } total || offset >= total) break;
            }
        }
        return rows;
    }

    /// <summary>Adds one row of a stat list to the player's line for that team. False for a row with no player or team.</summary>
    internal static bool AddSplit(Dictionary<string, List<TeamLine>> lines, string group, int season, JsonElement split, int order, string? playerId = null)
    {
        playerId ??= MlbStatsFiles.Str(MlbStatsFiles.Obj(split, "player"), "id");
        var teamId = MlbStatsFiles.Str(MlbStatsFiles.Obj(split, "team"), "id");
        if (playerId is null || teamId is null) return false;
        var stat = MlbStatsFiles.Obj(split, "stat");

        if (!lines.TryGetValue(playerId, out var list)) lines[playerId] = list = [];
        var line = list.FirstOrDefault(l => l.Season == season && l.TeamId == teamId);
        if (line is null)
        {
            line = new TeamLine(season, teamId, order);
            list.Add(line);
        }
        if (MlbStatsFiles.Int(split, "numTeams") is > 1) line.Combined = true;
        line.Order = Math.Min(line.Order, order);

        void Add(string key, string field)
        {
            if (MlbStatsFiles.Int(stat, field) is { } v && v != 0) line.Totals[key] = line.Totals.GetValueOrDefault(key) + v;
        }

        switch (group)
        {
            case "hitting":
                line.HitGames = MlbStatsFiles.Int(stat, "gamesPlayed") ?? 0;
                foreach (var (key, field) in HittingFields) Add(key, field);
                break;
            case "pitching":
                line.PitchGames = MlbStatsFiles.Int(stat, "gamesPitched") ?? MlbStatsFiles.Int(stat, "gamesPlayed") ?? 0;
                foreach (var (key, field) in PitchingFields) Add(key, field);
                break;
            case "fielding":
                var position = MlbStatsFiles.Str(MlbStatsFiles.Obj(split, "position"), "abbreviation");
                var games = MlbStatsFiles.Int(stat, "games") ?? MlbStatsFiles.Int(stat, "gamesPlayed") ?? 0;
                if (position is not null && games > 0) line.Fielding[position] = line.Fielding.GetValueOrDefault(position) + games;
                break;
        }
        return true;
    }

    /// <summary>
    /// The season lists give a traded player's whole season under the team he ended it with. His year-by-year lines have
    /// a line for each team, in the order he played for them, so those seasons are rebuilt from them. A season that
    /// cannot be read that way keeps the whole season under the last team.
    /// </summary>
    private async Task SplitTradesAsync(Dictionary<string, List<TeamLine>> lines, CancellationToken ct)
    {
        var opts = options.Value;
        var traded = lines.Where(l => l.Value.Any(t => t.Combined)).Select(l => l.Key).Order(StringComparer.Ordinal).ToList();
        var split = 0;
        foreach (var batch in traded.Chunk(Math.Max(1, opts.CareerBatchSize)))
        {
            var people = new List<JsonDocument>();
            try
            {
                if (!string.IsNullOrWhiteSpace(opts.Directory))
                {
                    foreach (var id in batch)
                    {
                        if (await MlbStatsFiles.ReadAsync(http, opts, MlbStatsFiles.CareerPath(id), ct) is { } doc) people.Add(doc);
                    }
                }
                else
                {
                    using var response = await http.GetAsync(new Uri(new Uri(opts.BaseUrl), MlbStatsFiles.CareersUrl(batch)), HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    people.Add(await JsonDocument.ParseAsync(stream, cancellationToken: ct));
                }

                foreach (var doc in people)
                foreach (var person in MlbStatsFiles.Array(doc.RootElement, "people"))
                {
                    if (MlbStatsFiles.Str(person, "id") is { } id && lines.TryGetValue(id, out var list)) split += ApplyCareer(list, person);
                }
            }
            finally
            {
                foreach (var doc in people) doc.Dispose();
            }
        }
        logger.LogInformation("MLB year-by-year lines split {Seasons} traded seasons of {Players} players by team", split, traded.Count);
    }

    /// <summary>
    /// Replaces each combined season in <paramref name="list"/> with the person's year-by-year lines for it, one per team
    /// in the order played. Returns the seasons split.
    /// </summary>
    internal static int ApplyCareer(List<TeamLine> list, JsonElement person)
    {
        var seasons = list.Where(l => l.Combined).Select(l => l.Season).ToHashSet();
        var rebuilt = new Dictionary<string, List<TeamLine>>();
        var order = 0;
        foreach (var group in MlbStatsFiles.Array(person, "stats"))
        {
            var name = MlbStatsFiles.Str(MlbStatsFiles.Obj(group, "group"), "displayName");
            if (name is not ("hitting" or "pitching" or "fielding")) continue;
            foreach (var split in MlbStatsFiles.Array(group, "splits"))
            {
                if (MlbStatsFiles.Int(split, "season") is not { } season || !seasons.Contains(season)) continue;
                if (MlbStatsFiles.Int(MlbStatsFiles.Obj(split, "sport"), "id") is { } sport && sport != 1) continue;
                // The line with no team is the whole season, which the card works out again from the teams' lines.
                if (MlbStatsFiles.Str(MlbStatsFiles.Obj(split, "team"), "id") is null) continue;
                AddSplit(rebuilt, name, season, split, order++, playerId: "");
            }
        }

        var done = 0;
        if (!rebuilt.TryGetValue("", out var byTeam)) return 0;
        foreach (var season in seasons)
        {
            var teams = byTeam.Where(l => l.Season == season).ToList();
            if (teams.Count < 2) continue;
            list.RemoveAll(l => l.Season == season);
            list.AddRange(teams);
            done++;
        }
        return done;
    }

    internal static readonly (string Key, string Field)[] HittingFields =
    [
        ("ab", "atBats"), ("r", "runs"), ("h", "hits"), ("2b", "doubles"), ("3b", "triples"), ("hr", "homeRuns"), ("rbi", "rbi"),
        ("sb", "stolenBases"), ("bb", "baseOnBalls"), ("so", "strikeOuts"), ("hbp", "hitByPitch"), ("sf", "sacFlies"),
    ];

    /// <summary>Card keys for the pitching list's counting stats: innings are kept as outs, so they add up.</summary>
    internal static readonly (string Key, string Field)[] PitchingFields =
    [
        ("gs", "gamesStarted"), ("w", "wins"), ("l", "losses"), ("sv", "saves"), ("outs", "outs"), ("er", "earnedRuns"),
        ("ha", "hits"), ("pbb", "baseOnBalls"), ("k", "strikeOuts"),
    ];

    /// <summary>
    /// Each team's regular-season record, and a playoff berth for a team that clinched one, under the team's id: the
    /// caller files it under the name the team had that season.
    /// </summary>
    internal static IEnumerable<HistoryTeamSeason> Records(JsonElement root, int season)
    {
        foreach (var group in MlbStatsFiles.Array(root, "records"))
        foreach (var r in MlbStatsFiles.Array(group, "teamRecords"))
        {
            if (MlbStatsFiles.Str(MlbStatsFiles.Obj(r, "team"), "id") is not { } key) continue;
            if (MlbStatsFiles.Int(r, "wins") is not { } wins || MlbStatsFiles.Int(r, "losses") is not { } losses) continue;
            var clinched = r.TryGetProperty("clinched", out var c) && c.ValueKind == JsonValueKind.True
                           || r.TryGetProperty("divisionChamp", out var d) && d.ValueKind == JsonValueKind.True
                           || MlbStatsFiles.Str(r, "clinchIndicator") is "y" or "w" or "z" or "x";
            yield return new HistoryTeamSeason(key, season, wins, losses, clinched);
        }
    }

    internal static HistorySnapshot Build(
        IReadOnlyDictionary<string, MlbPerson> people,
        IReadOnlyDictionary<string, List<TeamLine>> lines,
        IReadOnlyDictionary<string, DraftPick> draft,
        IReadOnlyDictionary<string, List<HistoryAward>> awards,
        IReadOnlySet<string> hallOfFame,
        MlbTeams teams,
        int latestSeason,
        int minSeasons = 1)
    {
        var history = new List<HistoryPlayer>(lines.Count);
        foreach (var (id, playerLines) in lines.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (!people.TryGetValue(id, out var person)) continue;
            var played = playerLines.Where(l => l.Games > 0).ToList();
            if (played.Count == 0) continue;

            var seasons = played.Select(l => l.Season).Distinct().Order().ToList();
            var last = seasons[^1];
            if (seasons.Count < minSeasons && last < latestSeason) continue;

            var won = awards.GetValueOrDefault(id)?.Distinct().OrderBy(a => a.Season).ThenBy(a => a.Type, StringComparer.Ordinal).ToList() ?? [];
            var position = PositionOf(person.Position, played);
            var (first, lastName) = BrefCsvSource.SplitName(person.FullName);
            draft.TryGetValue(id, out var pick);
            var hof = hallOfFame.Contains(id);

            history.Add(new HistoryPlayer(
                ExternalId: id,
                FirstName: first,
                LastName: lastName,
                Position: position,
                HeightInches: person.Height,
                WeightLbs: person.Weight,
                BirthDate: person.BirthDate,
                College: pick?.College,
                FromYear: person.DebutYear is { } debut ? Math.Min(debut, seasons[0]) : seasons[0],
                ToYear: last,
                HallOfFame: hof,
                DraftYear: pick?.Year ?? person.DraftYear,
                DraftNumber: pick?.Pick,
                Seasons: SeasonLines(played, teams),
                Awards: won,
                JerseyNumber: person.Number,
                HeadshotUrl: MlbStatsFiles.HeadshotUrl(id),
                Popularity: PopularityCalculator.FromBaseballCareer(Career(played, won, hof, last >= latestSeason, latestSeason)),
                BirthPlace: person.BirthPlace,
                Country: person.Country));
        }

        return new HistorySnapshot("mlb", IdSource, latestSeason, teams.HistoryTeams(), history);
    }

    /// <summary>
    /// The lines in the order played, shaped like a basketball record: a one-team season is one line, and a season split
    /// by a trade has a line per team, in the order the year-by-year list gives them, and a combined line with no team
    /// that carries the whole season.
    /// </summary>
    internal static List<HistorySeason> SeasonLines(List<TeamLine> lines, MlbTeams teams)
    {
        var result = new List<HistorySeason>();
        foreach (var group in lines.GroupBy(l => l.Season).OrderBy(g => g.Key))
        {
            var year = group.ToList();
            if (year.Count == 1)
            {
                result.Add(Line(year[0], teams.Key(year[0].TeamId, year[0].Season), true));
                continue;
            }

            var ordered = year.OrderBy(l => l.Order).ToList();
            result.AddRange(ordered.Select(l => Line(l, teams.Key(l.TeamId, l.Season), false)));

            var total = new TeamLine(year[0].Season, "", 0) { HitGames = year.Sum(l => l.HitGames), PitchGames = year.Sum(l => l.PitchGames) };
            foreach (var l in year)
            {
                foreach (var (k, v) in l.Totals) total.Totals[k] = total.Totals.GetValueOrDefault(k) + v;
                foreach (var (k, v) in l.Fielding) total.Fielding[k] = total.Fielding.GetValueOrDefault(k) + v;
            }
            result.Add(Line(total, null, true));
        }
        return result;

        static HistorySeason Line(TeamLine l, string? team, bool total) =>
            new(l.Season, team, total, l.Games, null, null, null, null, StatsJson(l));
    }

    /// <summary>
    /// Card JSON for a line: the counting stats under short keys with zeros left out, the games pitched ("pg") when he
    /// pitched, the games at each fielding position ("fpos") and the position he played most ("pos").
    /// </summary>
    internal static string StatsJson(TeamLine l)
    {
        var json = new JsonObject();
        foreach (var (key, _) in HittingFields.Concat(PitchingFields))
        {
            if (l.Totals.TryGetValue(key, out var v) && v != 0) json[key] = v;
        }
        if (l.PitchGames > 0) json["pg"] = l.PitchGames;
        if (l.Fielding.Count > 0)
        {
            var fpos = new JsonObject();
            foreach (var (pos, games) in l.Fielding.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal)) fpos[pos] = games;
            json["fpos"] = fpos;
            json["pos"] = l.Fielding.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).First().Key;
        }
        return json.ToJsonString();
    }

    /// <summary>
    /// A pitcher is filed as a starter (SP) or a reliever (RP) by his last three seasons; a two-way player stays TWP.
    /// Anyone else keeps the API's position.
    /// </summary>
    internal static string? PositionOf(string? position, IReadOnlyList<TeamLine> lines)
    {
        if (position != "P") return position;
        var newest = lines.Select(l => l.Season).DefaultIfEmpty().Max();
        var recent = lines.Where(l => l.Season > newest - 3).ToList();
        var games = recent.Sum(l => l.PitchGames);
        var starts = recent.Sum(l => l.Totals.GetValueOrDefault("gs"));
        return games == 0 ? "P" : starts * 2 >= games ? "SP" : "RP";
    }

    internal static BaseballCareer Career(IReadOnlyList<TeamLine> lines, IReadOnlyList<HistoryAward> awards, bool hallOfFame, bool active, int latestSeason)
    {
        double Sum(string key) => lines.Sum(l => l.Totals.GetValueOrDefault(key));
        int Count(string type) => awards.Count(a => a.Type == type);

        double? recent = null;
        if (active)
        {
            recent = lines.Where(l => l.Season >= latestSeason - 1)
                .GroupBy(l => l.Season)
                .Select(g => PopularityCalculator.BaseballSeasonValue(
                    g.Sum(l => l.Totals.GetValueOrDefault("h")), g.Sum(l => l.Totals.GetValueOrDefault("hr")), g.Sum(l => l.Totals.GetValueOrDefault("sb")),
                    g.Sum(l => l.Totals.GetValueOrDefault("w")), g.Sum(l => l.Totals.GetValueOrDefault("k")), g.Sum(l => l.Totals.GetValueOrDefault("sv"))))
                .DefaultIfEmpty(0)
                .Max();
        }

        return new BaseballCareer(
            Hits: Sum("h"), HomeRuns: Sum("hr"), Wins: Sum("w"), Strikeouts: Sum("k"), Saves: Sum("sv"),
            AllStars: Count(AwardTypes.AllStar), Mvps: Count(AwardTypes.Mvp), CyYoungs: Count(AwardTypes.CyYoung),
            OtherAwards: Count(AwardTypes.Rookie) + Count(AwardTypes.GoldGlove) + Count(AwardTypes.SilverSlugger) + Count(AwardTypes.WorldSeriesMvp),
            HallOfFame: hallOfFame,
            Seasons: lines.Select(l => l.Season).Distinct().Count(),
            RecentValue: recent);
    }

    /// <summary>Each player's draft pick that the API links to him (the last one, for a player drafted more than once), and his college.</summary>
    private async Task<Dictionary<string, DraftPick>> ReadDraftAsync(int from, int latestSeason, CancellationToken ct)
    {
        var picks = new Dictionary<string, DraftPick>();
        for (var year = from; year <= latestSeason; year++)
        {
            using var doc = await MlbStatsFiles.ReadAsync(http, options.Value, MlbStatsFiles.DraftPath(year), ct);
            if (doc is null) continue;
            foreach (var (id, pick) in DraftPicks(doc.RootElement, year)) picks[id] = pick;
        }
        logger.LogInformation("MLB drafts give {Picks} picks", picks.Count);
        return picks;
    }

    internal static IEnumerable<(string Id, DraftPick Pick)> DraftPicks(JsonElement root, int year)
    {
        var drafts = MlbStatsFiles.Obj(root, "drafts");
        foreach (var round in MlbStatsFiles.Array(drafts, "rounds"))
        foreach (var pick in MlbStatsFiles.Array(round, "picks"))
        {
            if (MlbStatsFiles.Str(MlbStatsFiles.Obj(pick, "person"), "id") is not { } id) continue;
            if (MlbStatsFiles.Int(pick, "pickNumber") is not { } number) continue;
            var school = MlbStatsFiles.Obj(pick, "school");
            var schoolClass = MlbStatsFiles.Str(school, "schoolClass") ?? "";
            // High schoolers are drafted out of school, not out of college.
            var college = schoolClass.StartsWith("HS", StringComparison.OrdinalIgnoreCase) ? null : MlbStatsFiles.Str(school, "name");
            yield return (id, new DraftPick(year, number, college));
        }
    }

    /// <summary>Every award winner by MLBAM id, and the Hall of Famers.</summary>
    private async Task<(Dictionary<string, List<HistoryAward>> Awards, HashSet<string> HallOfFame)> ReadAwardsAsync(CancellationToken ct)
    {
        var awards = new Dictionary<string, List<HistoryAward>>();
        var count = 0;
        foreach (var (awardId, type) in Awards)
        {
            using var doc = await MlbStatsFiles.ReadAsync(http, options.Value, MlbStatsFiles.AwardPath(awardId), ct);
            if (doc is null) continue;
            foreach (var (id, season) in Recipients(doc.RootElement))
            {
                if (!awards.TryGetValue(id, out var list)) awards[id] = list = [];
                list.Add(new HistoryAward(season, type));
                count++;
            }
        }

        var hallOfFame = new HashSet<string>();
        using (var doc = await MlbStatsFiles.ReadAsync(http, options.Value, MlbStatsFiles.AwardPath(HallOfFameAward), ct))
        {
            if (doc is not null) hallOfFame.UnionWith(Recipients(doc.RootElement).Select(r => r.Id));
        }
        logger.LogInformation("MLB Stats API has {Awards} awards and {HallOfFame} Hall of Famers", count, hallOfFame.Count);
        return (awards, hallOfFame);
    }

    internal static IEnumerable<(string Id, int Season)> Recipients(JsonElement root)
    {
        foreach (var a in MlbStatsFiles.Array(root, "awards"))
        {
            if (MlbStatsFiles.Str(MlbStatsFiles.Obj(a, "player"), "id") is { } id && MlbStatsFiles.Int(a, "season") is { } season)
                yield return (id, season);
        }
    }
}

/// <summary>
/// Today's 40-man rosters from MLB's Stats API, refreshed every night. Layered on the full record: it sets each
/// player's team, jersey number and active status and fills in facts the record lacks. Prospects who have not played in
/// the majors are left off, so the guess box holds only players someone could know.
/// </summary>
public sealed class MlbStatsRosterSource(HttpClient http, IOptions<MlbStatsOptions> options, TimeProvider clock, ILogger<MlbStatsRosterSource> logger)
    : IPlayerSource
{
    public const string SourceName = "mlb-statsapi";
    public string Name => SourceName;

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow();
        var played = MlbCalendar.LatestPlayedSeason(now);
        var season = MlbCalendar.RosterSeason(now);

        // The teams of the newest season with a list; next season's may not be out over the winter.
        var teams = new MlbTeams();
        for (var year = season; year >= played; year--)
        {
            using var doc = await MlbStatsFiles.ReadAsync(http, opts, MlbStatsFiles.TeamsPath(year), cancellationToken);
            if (doc is null) continue;
            teams.AddSeason(year, doc.RootElement);
            break;
        }
        var current = teams.CurrentSourceTeams();
        if (current.Count == 0) throw new InvalidOperationException("MLB Stats API lists no teams.");

        var players = new List<SourcePlayer>();
        foreach (var team in current)
        {
            using var doc = await MlbStatsFiles.ReadRequiredAsync(http, opts, MlbStatsFiles.RosterPath(team.ExternalId), cancellationToken);
            players.AddRange(ReadRoster(doc.RootElement, team.ExternalId));
        }

        logger.LogInformation("MLB Stats API lists {Players} major leaguers on {Teams} 40-man rosters", players.Count, current.Count);
        return new SourceSnapshot("mlb", season, current, players,
            IdSource: MlbStatsHistorySource.IdSource, InSeason: MlbCalendar.InSeason(now), Supplement: true);
    }

    internal static IEnumerable<SourcePlayer> ReadRoster(JsonElement root, string teamId)
    {
        foreach (var entry in MlbStatsFiles.Array(root, "roster"))
        {
            var personElement = MlbStatsFiles.Obj(entry, "person");
            if (MlbStatsFiles.Person(personElement) is not { } person || person.DebutYear is null) continue;
            var (first, last) = BrefCsvSource.SplitName(person.FullName);
            yield return new SourcePlayer(
                ExternalId: person.Id,
                FirstName: first,
                LastName: last,
                TeamExternalId: teamId,
                Position: MlbStatsFiles.Str(MlbStatsFiles.Obj(entry, "position"), "abbreviation") ?? person.Position,
                HeightInches: person.Height,
                WeightLbs: person.Weight,
                JerseyNumber: MlbStatsFiles.Str(entry, "jerseyNumber") ?? person.Number,
                Country: person.Country,
                DraftYear: person.DraftYear,
                FromYear: person.DebutYear,
                BirthDate: person.BirthDate,
                HeadshotUrl: MlbStatsFiles.HeadshotUrl(person.Id),
                BirthPlace: person.BirthPlace);
        }
    }
}

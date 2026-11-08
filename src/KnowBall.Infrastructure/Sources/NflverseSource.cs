using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;
using KnowBall.Application.Text;

namespace KnowBall.Infrastructure.Sources;

public sealed class NflverseOptions
{
    /// <summary>Where the nflverse release files are downloaded from. Must end with a slash.</summary>
    public string BaseUrl { get; set; } = "https://github.com/nflverse/nflverse-data/releases/download/";
    /// <summary>Read the files from this folder instead, laid out as the release paths (players/players.csv). Used by tests and offline runs.</summary>
    public string? Directory { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
    /// <summary>The first season whose rosters are read. Player ids are reliable from 1990 on.</summary>
    public int FirstSeason { get; set; } = 1990;
    /// <summary>
    /// From this season on, the season roster files list each player once, so a mid-season trade is lost; the
    /// weekly rosters are read instead.
    /// </summary>
    public int WeeklyFromSeason { get; set; } = 2016;
    /// <summary>
    /// Players with fewer seasons than this on a game-day roster are left out, unless they are on one this
    /// season: a one-year backup is nobody's guess, and leaving thousands of them out keeps the import small.
    /// </summary>
    public int MinSeasons { get; set; } = 2;
}

/// <summary>NFL dates: when a season starts, and which season a day belongs to.</summary>
public static class NflCalendar
{
    /// <summary>The newest season that has kicked off: games start in September.</summary>
    public static int LatestPlayedSeason(DateTimeOffset now) => now.Month >= 9 ? now.Year : now.Year - 1;

    /// <summary>The season rosters are being built for: the league year turns over in March.</summary>
    public static int RosterSeason(DateTimeOffset now) => now.Month >= 3 ? now.Year : now.Year - 1;

    /// <summary>From kickoff in September to the Super Bowl in February.</summary>
    public static bool InSeason(DateTimeOffset now) => now.Month >= 9 || now.Month <= 2;
}

/// <summary>
/// The NFL's teams and the names they have played under. nflverse files name a team by a code that
/// can change with the season ("HOU" was the Oilers until 1996) or differ between files ("ARZ", "ARI").
/// </summary>
internal sealed class NflTeams
{
    internal sealed record CurrentTeam(string Key, string Abbreviation, string City, string Name, string Conference, string Division, string LogoUrl);
    internal sealed record FormerTeam(string Code, string City, string Name, string Franchise);
    internal sealed record SeasonalCode(string Code, int ThroughSeason, string Identity);
    private sealed record TeamFile(List<CurrentTeam> Current, Dictionary<string, string> Aliases, List<SeasonalCode> Seasonal, List<FormerTeam> Former);

    public static readonly Lazy<NflTeams> Instance = new(Load);

    public IReadOnlyDictionary<string, CurrentTeam> Current { get; }
    public IReadOnlyDictionary<string, FormerTeam> Former { get; }
    private readonly IReadOnlyDictionary<string, string> _aliases;
    private readonly IReadOnlyList<SeasonalCode> _seasonal;

    private NflTeams(TeamFile file)
    {
        Current = file.Current.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        Former = file.Former.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        _aliases = new Dictionary<string, string>(file.Aliases, StringComparer.OrdinalIgnoreCase);
        _seasonal = file.Seasonal;
    }

    /// <summary>The key a team is filed under in that season, or null for a code this build does not know.</summary>
    public string? Identity(string? code, int season)
    {
        if (code is null) return null;
        code = _aliases.GetValueOrDefault(code, code).ToUpperInvariant();
        foreach (var s in _seasonal)
        {
            if (s.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && season <= s.ThroughSeason) return s.Identity;
        }
        return Current.ContainsKey(code) || Former.ContainsKey(code) ? code : null;
    }

    public static SourceTeam ToSourceTeam(CurrentTeam t) =>
        new(t.Key, t.City, t.Name, t.Abbreviation, t.Conference, t.Division, LogoUrl: t.LogoUrl);

    public IReadOnlyList<SourceTeam> CurrentSourceTeams() => Current.Values.Select(ToSourceTeam).ToList();

    /// <summary>Every current team, and each former identity seen in the rosters, with the seasons it was seen.</summary>
    public List<HistoryTeam> HistoryTeams(IReadOnlyDictionary<string, (int From, int To)> seen, int latestSeason)
    {
        var teams = new List<HistoryTeam>();
        foreach (var t in Current.Values)
        {
            int? from = seen.TryGetValue(t.Key, out var span) ? span.From : null;
            teams.Add(new HistoryTeam(t.Key, ToSourceTeam(t), from, null, null));
        }
        foreach (var (key, span) in seen)
        {
            if (!Former.TryGetValue(key, out var f)) continue;
            var franchise = Current[f.Franchise];
            teams.Add(new HistoryTeam(
                key,
                new SourceTeam(key, f.City, f.Name, key, franchise.Conference, franchise.Division),
                span.From,
                span.To >= latestSeason ? null : span.To,
                f.Franchise));
        }
        return teams;
    }

    private static NflTeams Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("nfl-teams.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        var file = JsonSerializer.Deserialize<TeamFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("nfl-teams.json is empty");
        return new NflTeams(file);
    }
}

/// <summary>Opens nflverse release files from the web or from a local folder laid out the same way.</summary>
internal static class NflverseFiles
{
    /// <summary>The file, or null if it is not published (a season whose rosters are not out yet).</summary>
    public static async Task<Stream?> OpenAsync(HttpClient http, NflverseOptions opts, string path, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(opts.Directory))
        {
            var local = Path.Combine(opts.Directory, path);
            return File.Exists(local) ? File.OpenRead(local) : null;
        }

        var response = await http.GetAsync(new Uri(new Uri(opts.BaseUrl), path), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    public static async Task<Stream> OpenRequiredAsync(HttpClient http, NflverseOptions opts, string path, CancellationToken ct) =>
        await OpenAsync(http, opts, path, ct) ?? throw new InvalidOperationException($"nflverse file {path} was not found.");

    public static string RosterPath(int season) => $"rosters/roster_{season}.csv";
    public static string WeeklyRosterPath(int season) => $"weekly_rosters/roster_weekly_{season}.csv";
    public const string PlayersPath = "players/players.csv";
    public const string DraftPath = "draft_picks/draft_picks.csv";
    public const string CareerStatsPath = "player_stats/player_stats_season.csv";
    public static string SeasonStatsPath(int season) => $"stats_player/stats_player_reg_{season}.csv";

    /// <summary>Inches from "75" or "6-3".</summary>
    public static int? Height(string? value)
    {
        if (value is null) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var inches)) return inches > 0 ? inches : null;
        var parts = value.Split('-', '\'');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var feet)
               && int.TryParse(parts[1].Trim('"', ' '), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rest)
            ? feet * 12 + rest
            : null;
    }

    public static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>"Safety" is filed as SAF in some files and S in others.</summary>
    public static string? Position(string? value) => value?.ToUpperInvariant() switch
    {
        null => null,
        "SAF" => "S",
        var p => p,
    };

    /// <summary>Statuses that mean the player is on the team's roster this season: active, inactive for the game, injured reserve.</summary>
    public static bool OnRoster(string? status) => status is "ACT" or "INA" or "RES";
}

/// <summary>
/// The NFL's full record from nflverse (open data, CC-BY): every player with bio facts, draft slot and
/// headshot, the team each was on in every season since <see cref="NflverseOptions.FirstSeason"/>, and a
/// fame score from the draft record (career value, Pro Bowls, All-Pros, Hall of Fame) and fantasy
/// production. Every file is read a row at a time and only the fields used are kept, so the import
/// stays small; no season stat lines are stored.
/// </summary>
public sealed class NflverseHistorySource(HttpClient http, IOptions<NflverseOptions> options, TimeProvider clock, ILogger<NflverseHistorySource> logger)
    : IHistorySource
{
    public const string SourceName = "nflverse-history";
    public const string IdSource = "gsis";
    public string Name => SourceName;

    internal sealed record PlayerFacts(
        string Id, string DisplayName, string? Position, int? Height, int? Weight, DateOnly? BirthDate, string? College,
        string? Jersey, string? Headshot, int? RookieSeason, int? DraftYear, int? DraftPick);

    internal sealed record DraftRecord(double? CareerValue, int ProBowls, int AllPros, bool HallOfFame);

    /// <summary>One team a player was on in one season; <see cref="Order"/> puts a traded player's teams in sequence.</summary>
    internal readonly record struct RosterSpell(int Season, int Order, string TeamKey);

    internal sealed record Production(double CareerFantasyPoints, double? RecentPerGame);

    public async Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var teams = NflTeams.Instance.Value;
        var latest = NflCalendar.LatestPlayedSeason(clock.GetUtcNow());

        var players = await ReadPlayersAsync(cancellationToken);
        var draft = await ReadDraftAsync(cancellationToken);

        var byName = new Dictionary<string, string>();
        foreach (var p in players.Values)
        {
            if (p.BirthDate is { } born) byName.TryAdd(NameKey(p.DisplayName, born), p.Id);
        }

        var spells = new Dictionary<string, List<RosterSpell>>();
        var newest = opts.FirstSeason;
        for (var season = opts.FirstSeason; season <= latest; season++)
        {
            var weekly = season >= opts.WeeklyFromSeason;
            await using var stream = await NflverseFiles.OpenAsync(http, opts,
                weekly ? NflverseFiles.WeeklyRosterPath(season) : NflverseFiles.RosterPath(season), cancellationToken);
            if (stream is null)
            {
                if (season < latest) throw new InvalidOperationException($"nflverse has no rosters for {season}.");
                logger.LogInformation("nflverse has no {Season} rosters yet; the record ends with {Previous}", season, season - 1);
                break;
            }

            var rows = 0;
            await foreach (var row in CsvReader.StreamAsync(stream, cancellationToken))
            {
                if (ReadSpell(row, season, weekly, teams, byName) is not { } spell) continue;
                if (!spells.TryGetValue(spell.Id, out var list)) spells[spell.Id] = list = [];
                list.Add(spell.Spell);
                rows++;
            }
            if (rows == 0) throw new InvalidOperationException($"nflverse rosters for {season} have no players.");
            newest = season;
        }

        var production = await ReadProductionAsync(newest, cancellationToken);
        var snapshot = Build(players, draft, spells, production, newest, teams, opts.MinSeasons);
        logger.LogInformation("nflverse has {Players} players and {Teams} teams from {First} through {Season}",
            snapshot.Players.Count, snapshot.Teams.Count, opts.FirstSeason, newest);
        return snapshot;
    }

    internal static HistorySnapshot Build(
        IReadOnlyDictionary<string, PlayerFacts> players,
        IReadOnlyDictionary<string, DraftRecord> draft,
        IReadOnlyDictionary<string, List<RosterSpell>> spells,
        IReadOnlyDictionary<string, Production> production,
        int latestSeason,
        NflTeams teams,
        int minSeasons = 1)
    {
        var seen = new Dictionary<string, (int From, int To)>();
        var history = new List<HistoryPlayer>(spells.Count);

        foreach (var (id, playerSpells) in spells.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (!players.TryGetValue(id, out var facts)) continue;

            // One line per team per season, in the order played; a team seen in several weeks counts once.
            var lines = playerSpells
                .GroupBy(s => (s.Season, s.TeamKey))
                .Select(g => g.MinBy(s => s.Order))
                .OrderBy(s => s.Season).ThenBy(s => s.Order).ThenBy(s => s.TeamKey, StringComparer.Ordinal)
                .ToList();
            var perSeason = lines.GroupBy(s => s.Season).ToDictionary(g => g.Key, g => g.Count());

            var firstSeason = lines[0].Season;
            var lastSeason = lines[^1].Season;
            if (perSeason.Count < minSeasons && lastSeason < latestSeason) continue;
            foreach (var s in lines)
            {
                seen[s.TeamKey] = seen.TryGetValue(s.TeamKey, out var span)
                    ? (Math.Min(span.From, s.Season), Math.Max(span.To, s.Season))
                    : (s.Season, s.Season);
            }

            var (first, last) = BrefCsvSource.SplitName(facts.DisplayName);
            draft.TryGetValue(id, out var picked);
            production.TryGetValue(id, out var produced);

            var popularity = PopularityCalculator.FromFootballCareer(new FootballCareer(
                facts.Position,
                picked?.CareerValue,
                produced?.CareerFantasyPoints ?? 0,
                picked?.ProBowls ?? 0,
                picked?.AllPros ?? 0,
                picked?.HallOfFame ?? false,
                perSeason.Count,
                facts.DraftPick,
                lastSeason >= latestSeason ? produced?.RecentPerGame : null));

            history.Add(new HistoryPlayer(
                ExternalId: id,
                FirstName: first,
                LastName: last,
                Position: facts.Position,
                HeightInches: facts.Height,
                WeightLbs: facts.Weight,
                BirthDate: facts.BirthDate,
                College: facts.College,
                FromYear: facts.RookieSeason is { } rookie ? Math.Min(rookie, firstSeason) : firstSeason,
                ToYear: lastSeason,
                HallOfFame: picked?.HallOfFame ?? false,
                DraftYear: facts.DraftYear,
                DraftNumber: facts.DraftPick,
                Seasons: lines.Select(s => new HistorySeason(s.Season, s.TeamKey, perSeason[s.Season] == 1, null, null, null, null, null)).ToList(),
                Awards: [],
                // Before 2023 nobody could wear 0, so a 0 in the file means the number is unknown.
                JerseyNumber: facts.Jersey == "0" && lastSeason < 2023 ? null : facts.Jersey,
                HeadshotUrl: facts.Headshot,
                Popularity: popularity));
        }

        return new HistorySnapshot("nfl", IdSource, latestSeason, teams.HistoryTeams(seen, latestSeason), history, KeepSeasonLines: false);
    }

    /// <summary>The player and team one roster row places together, or null for a row that does not count.</summary>
    internal static (string Id, RosterSpell Spell)? ReadSpell(CsvRow row, int season, bool weekly, NflTeams teams, IReadOnlyDictionary<string, string> byName)
    {
        var status = row.Str("status");
        int order;
        if (weekly)
        {
            if (status is not ("ACT" or "INA")) return null;
            order = row.Int("week") ?? 0;
        }
        else
        {
            // A season file gives each player's last status with each team; a team he was traded away from came first.
            if (status is null or "CUT" or "DEV" or "RET" or "EXE") return null;
            order = status.StartsWith("TR", StringComparison.Ordinal) ? 0 : 1;
        }

        if (teams.Identity(row.Str("team"), season) is not { } team) return null;

        var id = row.Str("gsis_id");
        if (id is null && row.Str("full_name") is { } name && NflverseFiles.Date(row.Str("birth_date")) is { } born)
            byName.TryGetValue(NameKey(name, born), out id);
        return id is null ? null : (id, new RosterSpell(season, order, team));
    }

    private static string NameKey(string name, DateOnly born) => $"{NameNormalizer.Normalize(name)}|{born:yyyy-MM-dd}";

    private async Task<Dictionary<string, PlayerFacts>> ReadPlayersAsync(CancellationToken ct)
    {
        await using var stream = await NflverseFiles.OpenRequiredAsync(http, options.Value, NflverseFiles.PlayersPath, ct);
        var players = new Dictionary<string, PlayerFacts>();
        await foreach (var r in CsvReader.StreamAsync(stream, ct))
        {
            if (ReadPlayer(r) is { } p) players.TryAdd(p.Id, p);
        }
        if (players.Count == 0) throw new InvalidOperationException("nflverse players file is empty.");
        return players;
    }

    internal static PlayerFacts? ReadPlayer(CsvRow r)
    {
        if (r.Str("gsis_id") is not { } id || r.Str("display_name") is not { } name) return null;
        return new PlayerFacts(
            id, name, NflverseFiles.Position(r.Str("position")), NflverseFiles.Height(r.Str("height")), r.Int("weight"),
            NflverseFiles.Date(r.Str("birth_date")), r.Str("college_name"), r.Str("jersey_number"), r.Str("headshot"),
            r.Int("rookie_season"), r.Int("draft_year"), r.Int("draft_pick"));
    }

    private async Task<Dictionary<string, DraftRecord>> ReadDraftAsync(CancellationToken ct)
    {
        await using var stream = await NflverseFiles.OpenRequiredAsync(http, options.Value, NflverseFiles.DraftPath, ct);
        var draft = new Dictionary<string, DraftRecord>();
        await foreach (var r in CsvReader.StreamAsync(stream, ct))
        {
            if (r.Str("gsis_id") is not { } id) continue;
            draft[id] = new DraftRecord(r.Dbl("w_av"), r.Int("probowls") ?? 0, r.Int("allpro") ?? 0, r.Flag("hof"));
        }
        return draft;
    }

    /// <summary>
    /// Career fantasy points (regular seasons since 1999) and the best per-game output of the last two seasons.
    /// The career file stops at 2024; later seasons come from the per-season files.
    /// </summary>
    private async Task<Dictionary<string, Production>> ReadProductionAsync(int latestSeason, CancellationToken ct)
    {
        var opts = options.Value;
        var career = new Dictionary<string, double>();
        var careerThrough = 0;
        await using (var stream = await NflverseFiles.OpenAsync(http, opts, NflverseFiles.CareerStatsPath, ct))
        {
            if (stream is not null)
            {
                await foreach (var r in CsvReader.StreamAsync(stream, ct))
                {
                    if (r.Str("season_type") != "REG" || r.Str("player_id") is not { } id || r.Int("season") is not { } season) continue;
                    career[id] = career.GetValueOrDefault(id) + (r.Dbl("fantasy_points_ppr") ?? 0);
                    careerThrough = Math.Max(careerThrough, season);
                }
            }
        }

        var recent = new Dictionary<string, double>();
        var from = careerThrough == 0 ? latestSeason - 1 : Math.Min(careerThrough + 1, latestSeason - 1);
        for (var season = from; season <= latestSeason; season++)
        {
            await using var stream = await NflverseFiles.OpenAsync(http, opts, NflverseFiles.SeasonStatsPath(season), ct);
            if (stream is null) continue;
            await foreach (var r in CsvReader.StreamAsync(stream, ct))
            {
                if (r.Str("player_id") is not { } id) continue;
                var points = r.Dbl("fantasy_points_ppr") ?? 0;
                if (season > careerThrough) career[id] = career.GetValueOrDefault(id) + points;
                if (season >= latestSeason - 1 && PerGame(r) is { } perGame)
                    recent[id] = Math.Max(recent.GetValueOrDefault(id), perGame);
            }
        }

        return career.Keys.Union(recent.Keys)
            .ToDictionary(id => id, id => new Production(career.GetValueOrDefault(id), recent.TryGetValue(id, out var r) ? r : null));
    }

    /// <summary>PPR points a game, or for a defender sacks, interceptions and solo tackles on about the same scale.</summary>
    internal static double? PerGame(CsvRow r)
    {
        if (r.Int("games") is not { } games || games <= 0) return null;
        var offense = (r.Dbl("fantasy_points_ppr") ?? 0) / games;
        var defense = ((r.Dbl("def_tackles_solo") ?? 0) + 4 * (r.Dbl("def_sacks") ?? 0) + 5 * (r.Dbl("def_interceptions") ?? 0)) / games * 2.5;
        return Math.Round(Math.Max(offense, defense), 2);
    }
}

/// <summary>
/// Today's NFL rosters from nflverse, refreshed through the season. Layered on the full record: it sets
/// each player's team, jersey number and active status and fills in facts the record lacks. Practice
/// squad, released and retired players are left off, so they stop being current players.
/// </summary>
public sealed class NflverseRosterSource(HttpClient http, IOptions<NflverseOptions> options, TimeProvider clock, ILogger<NflverseRosterSource> logger)
    : IPlayerSource
{
    public const string SourceName = "nflverse";
    public string Name => SourceName;

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var season = NflCalendar.RosterSeason(now);
        var stream = await NflverseFiles.OpenAsync(http, options.Value, NflverseFiles.RosterPath(season), cancellationToken);
        if (stream is null)
        {
            // Early in a league year the new file may not be out; last season's rosters still stand.
            logger.LogInformation("nflverse has no {Season} rosters yet; using {Previous}", season, season - 1);
            season--;
            stream = await NflverseFiles.OpenRequiredAsync(http, options.Value, NflverseFiles.RosterPath(season), cancellationToken);
        }

        var teams = NflTeams.Instance.Value;
        var players = new List<SourcePlayer>();
        await using (stream)
        {
            await foreach (var row in CsvReader.StreamAsync(stream, cancellationToken))
            {
                if (ReadPlayer(row, season, teams) is { } player) players.Add(player);
            }
        }

        logger.LogInformation("nflverse lists {Players} players on {Season} rosters", players.Count, season);
        return new SourceSnapshot("nfl", season, teams.CurrentSourceTeams(), players,
            IdSource: NflverseHistorySource.IdSource, InSeason: NflCalendar.InSeason(now), Supplement: true);
    }

    internal static SourcePlayer? ReadPlayer(CsvRow r, int season, NflTeams teams)
    {
        if (!NflverseFiles.OnRoster(r.Str("status"))) return null;
        if (r.Str("gsis_id") is not { } id || teams.Identity(r.Str("team"), season) is not { } team) return null;
        if (!teams.Current.ContainsKey(team)) return null;

        return new SourcePlayer(
            ExternalId: id,
            FirstName: r.Str("football_name") ?? r.Str("first_name") ?? "",
            LastName: r.Str("last_name") ?? "",
            TeamExternalId: team,
            Position: NflverseFiles.Position(r.Str("position")),
            HeightInches: NflverseFiles.Height(r.Str("height")),
            WeightLbs: r.Int("weight"),
            JerseyNumber: r.Str("jersey_number"),
            College: r.Str("college"),
            DraftNumber: r.Int("draft_number"),
            FromYear: r.Int("rookie_year") ?? r.Int("entry_year"),
            BirthDate: NflverseFiles.Date(r.Str("birth_date")),
            HeadshotUrl: r.Str("headshot_url"));
    }
}

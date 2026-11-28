using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Domain;

namespace KnowBall.Application.Games.TeamPhoto;

/// <param name="Season">The season's year: 2016 is the season the Cubs won the World Series.</param>
/// <param name="Finals">"won" or "lost" for a World Series team; null for a practice team that did not get there.</param>
public sealed record BaseballPhotoPayload(PhotoTeam Team, int Season, int? Wins, int? Losses, string? Finals, IReadOnlyList<FootballSlot> Slots);

/// <summary>
/// The MLB edition: the eight fielders, each the player with the most games at his position for that team that season,
/// and the starting pitcher with the most starts. No designated hitter. They stand as on the field: the outfield at the
/// back, the infield in the middle, the pitcher and catcher in front. Daily puzzles are both World Series teams of
/// every season since 1960, the first the MLB import reads; practice draws from the other playoff teams since then whose
/// nine are clear.
/// </summary>
public sealed partial class TeamPhotoGame
{
    /// <summary>The first MLB season the import reads.</summary>
    public const int FirstBaseballSeason = 1960;
    /// <summary>A practice fielder played his position in at least this share of the team's games...</summary>
    public const double BaseballClearFielder = 0.5;
    /// <summary>...and the pitcher started at least this share of them (24 of 162).</summary>
    public const double BaseballClearStarter = 0.15;

    private static readonly Lazy<IReadOnlyList<FinalsTeam>> BaseballPool = new(() => LoadPool("world-series-teams.json"));

    /// <summary>Both World Series teams of every season since 1960, by MLBAM team id, from the embedded list.</summary>
    public static IReadOnlyList<FinalsTeam> WorldSeriesTeams => BaseballPool.Value;

    /// <summary>The nine places in the photo, in payload order: outfield at the back, infield in the middle, battery in front.</summary>
    internal static readonly (string Position, string Row)[] BaseballSpots =
    [
        ("LF", "back"), ("CF", "back"), ("RF", "back"),
        ("3B", "middle"), ("SS", "middle"), ("2B", "middle"), ("1B", "middle"),
        ("SP", "front"), ("C", "front"),
    ];

    /// <summary>One player's season for one team: his games at each position, and his hitting or pitching totals.</summary>
    internal sealed record BaseballRosterLine(int PlayerId, int Games, IReadOnlyDictionary<string, int> Fielding, IReadOnlyDictionary<string, double> Totals)
    {
        public double this[string key] => Totals.GetValueOrDefault(key);
        public int At(string position) => Fielding.GetValueOrDefault(position);
    }

    /// <summary>
    /// The nine in payload order, or null when a place has nobody. The fielders are given out by games at the
    /// position, most first, one place to a player, so a regular who also covered first base now and then stays at
    /// his own position. The pitcher is the one with the most starts. Then any swaps from the curated list.
    /// </summary>
    internal static List<(BaseballRosterLine Line, string Position, string Row)>? BaseballLineup(
        IReadOnlyList<BaseballRosterLine> lines, IReadOnlyDictionary<int, int>? swaps)
    {
        var fielders = BaseballSpots.Where(s => s.Position != "SP").Select(s => s.Position).ToHashSet();
        var placed = new Dictionary<string, BaseballRosterLine>();
        var used = new HashSet<int>();
        var claims = lines
            .SelectMany(l => l.Fielding.Where(f => fielders.Contains(f.Key) && f.Value > 0).Select(f => (Line: l, Position: f.Key, Games: f.Value)))
            .OrderByDescending(c => c.Games).ThenByDescending(c => c.Line.Games).ThenBy(c => c.Line.PlayerId).ThenBy(c => c.Position, StringComparer.Ordinal);
        foreach (var (line, position, _) in claims)
        {
            if (placed.ContainsKey(position) || !used.Add(line.PlayerId)) continue;
            placed[position] = line;
        }
        var starter = lines.Where(l => l["gs"] > 0 && !used.Contains(l.PlayerId))
            .OrderByDescending(l => l["gs"]).ThenByDescending(l => l["outs"]).ThenBy(l => l.PlayerId)
            .FirstOrDefault();
        if (starter is null || placed.Count < fielders.Count) return null;
        placed["SP"] = starter;

        var picked = BaseballSpots.Select(s => placed[s.Position]).ToList();
        foreach (var (outId, inId) in swaps ?? new Dictionary<int, int>())
        {
            var at = picked.FindIndex(l => l.PlayerId == outId);
            var incoming = lines.FirstOrDefault(l => l.PlayerId == inId);
            if (at >= 0 && incoming is not null && picked.All(l => l.PlayerId != inId)) picked[at] = incoming;
        }
        return picked.Select((l, i) => (l, BaseballSpots[i].Position, BaseballSpots[i].Row)).ToList();
    }

    /// <summary>
    /// True when each fielder played his position in <see cref="BaseballClearFielder"/> of the team's games and the
    /// pitcher started <see cref="BaseballClearStarter"/> of them: a season with no platoon or injury muddle.
    /// </summary>
    internal static bool IsClearBaseball(IReadOnlyList<BaseballRosterLine> lines, int teamGames)
    {
        if (teamGames <= 0 || BaseballLineup(lines, null) is not { } nine) return false;
        return nine.All(n => n.Position == "SP"
            ? n.Line["gs"] >= BaseballClearStarter * teamGames
            : n.Line.At(n.Position) >= BaseballClearFielder * teamGames);
    }

    /// <summary>What a slot shows: average, home runs and runs batted in for a fielder; wins, losses, ERA and strikeouts for the pitcher.</summary>
    internal static IReadOnlyDictionary<string, double> BaseballSlotStats(string position, BaseballRosterLine line) => position == "SP"
        ? new Dictionary<string, double>
        {
            ["w"] = line["w"], ["l"] = line["l"], ["k"] = line["k"],
            ["era"] = line["outs"] > 0 ? Math.Round(line["er"] * 27 / line["outs"], 2) : 0,
        }
        : new Dictionary<string, double>
        {
            ["hr"] = line["hr"], ["rbi"] = line["rbi"],
            ["avg"] = line["ab"] > 0 ? Math.Round(line["h"] / line["ab"], 3) : 0,
        };

    /// <summary>
    /// A playoff team since 1960 with a clear nine, weighted towards famous ones, leaving the World Series teams to the
    /// dailies. Only finished seasons: the World Series ends in late October or early November.
    /// </summary>
    private async Task<GeneratedPuzzle?> BaseballPracticeAsync(GenerationContext context, List<Team> teams, CancellationToken ct)
    {
        var finished = context.AsOf.Month >= 11 ? context.AsOf.Year : context.AsOf.Year - 1;
        var worldSeries = WorldSeriesTeams
            .Select(f => FindMlbTeam(teams, f.Team, f.Season) is { } t ? (t.Id, f.Season) : (0, 0))
            .ToHashSet();
        var records = (await db.TeamSeasons.AsNoTracking()
                .Where(t => t.Team!.LeagueId == context.League.Id && t.Season >= FirstBaseballSeason && t.MadePlayoffs)
                .Select(t => new { t.TeamId, t.Season, t.Wins, t.Losses })
                .ToListAsync(ct))
            .ToDictionary(t => (t.TeamId, t.Season), t => t.Wins + t.Losses);

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.TeamId != null && s.Season >= FirstBaseballSeason && s.Season <= finished && s.Player!.LeagueId == context.League.Id)
            .Select(s => new { s.PlayerId, TeamId = s.TeamId!.Value, s.Season, s.Games, s.StatsJson, s.Player!.Popularity })
            .ToListAsync(ct);

        var candidates = new List<((int TeamId, int Season) Key, double Fame)>();
        foreach (var group in rows.GroupBy(r => (r.TeamId, r.Season)))
        {
            if (worldSeries.Contains(group.Key) || !records.TryGetValue(group.Key, out var games)) continue;
            var lines = group.Select(r => BaseballLine(r.PlayerId, r.Games, r.StatsJson)).ToList();
            if (!IsClearBaseball(lines, games)) continue;
            var nine = BaseballLineup(lines, null)!.Select(n => n.Line.PlayerId).ToHashSet();
            candidates.Add((group.Key, group.Where(r => nine.Contains(r.PlayerId)).Sum(r => r.Popularity)));
        }
        if (candidates.Count == 0) return null;

        var pick = WeightedPick.From(candidates.OrderBy(c => c.Key).ToList(), c => c.Fame * c.Fame, context.Random).Key;
        return await BuildBaseballAsync(teams.First(t => t.Id == pick.TeamId), pick.Season, null, null, ct);
    }

    /// <summary>The photo of one team-season, or null without a full nine or a record.</summary>
    private async Task<GeneratedPuzzle?> BuildBaseballAsync(Team team, int season, string? finals, IReadOnlyDictionary<string, string>? swap, CancellationToken ct)
    {
        var record = await db.TeamSeasons.AsNoTracking().FirstOrDefaultAsync(t => t.TeamId == team.Id && t.Season == season, ct);
        if (record is null) return null;

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.TeamId == team.Id && s.Season == season)
            .ToListAsync(ct);
        var lines = rows.Select(r => BaseballLine(r.PlayerId, r.Games, r.StatsJson)).ToList();
        if (BaseballLineup(lines, await SwapsAsync("mlbam", swap, ct)) is not { } nine) return null;

        var players = rows.Select(r => r.Player!).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        var payload = new BaseballPhotoPayload(
            new PhotoTeam(team.City, team.Name, team.Abbreviation, team.LogoUrl),
            season,
            record.Wins,
            record.Losses,
            finals,
            nine.Select(n => new FootballSlot(n.Position, n.Row, SlotGames(n.Position, n.Line), BaseballSlotStats(n.Position, n.Line))).ToList());
        var starters = nine.Select(n => n.Line.PlayerId).ToHashSet();
        // Keyed by the MLBAM id, as the World Series list is, so a daily team-season is known as used.
        var secret = new PhotoSecret(
            $"{season}-{team.ExternalId.Split('-')[0]}",
            nine.Select(n => new PhotoAnswer(n.Line.PlayerId, players[n.Line.PlayerId].FullName, players[n.Line.PlayerId].HeadshotUrl)).ToList(),
            players.Keys.Where(id => !starters.Contains(id)).Order().ToList());

        // Older teams and lesser-known nines are harder.
        var fame = nine.Average(n => players[n.Line.PlayerId].Popularity);
        var difficulty = (int)Math.Clamp(Math.Round(100 - fame + Math.Max(0, 2000 - season) / 3.0), 0, 100);
        return new GeneratedPuzzle(null, GameJson.Write(payload), GameJson.Write(secret), difficulty);
    }

    /// <summary>The pitcher's games pitched: a row's games also count games he only batted or ran in.</summary>
    internal static int SlotGames(string position, BaseballRosterLine line) =>
        position == "SP" && line["pg"] > 0 ? (int)line["pg"] : line.Games;

    /// <summary>
    /// The team with that MLBAM id in that season, under the name it had then: the id is the current name's key, and a
    /// former name's key is the id and its first season ("120-1969" for the Expos).
    /// </summary>
    internal static Team? FindMlbTeam(List<Team> teams, string teamId, int season) =>
        teams.FirstOrDefault(t => (t.ExternalId == teamId || t.ExternalId.StartsWith(teamId + "-", StringComparison.Ordinal))
                                  && (t.ActiveFrom is null || t.ActiveFrom <= season)
                                  && (t.ActiveTo is null || t.ActiveTo >= season));

    private static BaseballRosterLine BaseballLine(int playerId, int? games, string statsJson)
    {
        var line = BackOfTheCardGame.FootballLine.Read(new PlayerSeasonStat { Games = games, StatsJson = statsJson });
        var fielding = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(statsJson) && System.Text.Json.Nodes.JsonNode.Parse(statsJson)?["fpos"] is System.Text.Json.Nodes.JsonObject fpos)
        {
            foreach (var (position, value) in fpos)
            {
                if (value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<double>(out var n)) fielding[position] = (int)n;
            }
        }
        return new BaseballRosterLine(playerId, games ?? 0, fielding, line.Totals);
    }
}

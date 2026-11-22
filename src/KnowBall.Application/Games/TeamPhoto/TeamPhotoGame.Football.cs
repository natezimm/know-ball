using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Domain;

namespace KnowBall.Application.Games.TeamPhoto;

/// <summary>A blank spot in an NFL photo: the position, games played, and the season totals that position is known by.</summary>
/// <param name="Row">"back" for the receivers, "front" for the quarterback and running back.</param>
/// <param name="Stats">Season totals by card key: pyd, ptd and int for a quarterback; car, ryd and rtd for a running back; rec, wyd and wtd for a receiver.</param>
public sealed record FootballSlot(string Position, string Row, int Games, IReadOnlyDictionary<string, double> Stats);

/// <param name="Season">The season's year: 2019 is the season that ended in Super Bowl LIV.</param>
/// <param name="Finals">"won" or "lost" for a Super Bowl team; null for a practice team that did not get there.</param>
public sealed record FootballPhotoPayload(PhotoTeam Team, int Season, int? Wins, int? Losses, int? Ties, string? Finals, IReadOnlyList<FootballSlot> Slots);

/// <summary>
/// The NFL edition: the offense's five skill players, a quarterback, a running back, two wide receivers and a tight
/// end. Each is the one with the most of what his position does (pass attempts, carries, receiving yards) for that
/// team that season. Daily puzzles are both Super Bowl teams of every season since 1999, the first with stat lines;
/// practice draws from the other playoff teams since then whose five are clear.
/// </summary>
public sealed partial class TeamPhotoGame
{
    /// <summary>The first NFL season with stat lines.</summary>
    public const int FirstFootballSeason = 1999;
    /// <summary>A practice starter played at least this many games...</summary>
    public const int FootballClearMinGames = 10;
    /// <summary>...and the quarterback threw at least this many times as often as his backup.</summary>
    public const double FootballClearQuarterback = 2;

    private static readonly Lazy<IReadOnlyList<FinalsTeam>> FootballPool = new(() => LoadPool("super-bowl-teams.json"));

    /// <summary>Both Super Bowl teams of every season since 1999, from the embedded list.</summary>
    public static IReadOnlyList<FinalsTeam> SuperBowlTeams => FootballPool.Value;

    /// <summary>The five places in the photo, in payload order: the receivers at the back, then quarterback and running back.</summary>
    private static readonly (string Position, string Row)[] FootballSpots =
    [
        ("WR", "back"), ("TE", "back"), ("WR", "back"), ("QB", "front"), ("RB", "front"),
    ];

    /// <summary>One player's season for one team: what the five are picked from.</summary>
    internal sealed record FootballRosterLine(int PlayerId, string? Position, int Games, IReadOnlyDictionary<string, double> Totals)
    {
        public double this[string key] => Totals.GetValueOrDefault(key);
    }

    /// <summary>The five in payload order, or null when one of the places has nobody.</summary>
    internal static List<(FootballRosterLine Line, string Position, string Row)>? FootballLineup(
        IReadOnlyList<FootballRosterLine> lines, IReadOnlyDictionary<int, int>? swaps)
    {
        var quarterbacks = Leaders(lines, ["QB"], "att");
        var backs = Leaders(lines, ["RB", "HB"], "car");
        if (backs.Count == 0) backs = Leaders(lines, ["FB"], "car");
        var receivers = Leaders(lines, ["WR"], "wyd");
        var ends = Leaders(lines, ["TE"], "wyd");
        if (quarterbacks.Count == 0 || backs.Count == 0 || receivers.Count < 2 || ends.Count == 0) return null;

        var picked = new List<FootballRosterLine> { receivers[0], ends[0], receivers[1], quarterbacks[0], backs[0] };
        foreach (var (outId, inId) in swaps ?? new Dictionary<int, int>())
        {
            var at = picked.FindIndex(l => l.PlayerId == outId);
            var incoming = lines.FirstOrDefault(l => l.PlayerId == inId);
            if (at >= 0 && incoming is not null && picked.All(l => l.PlayerId != inId)) picked[at] = incoming;
        }
        return picked.Select((l, i) => (l, FootballSpots[i].Position, FootballSpots[i].Row)).ToList();
    }

    /// <summary>
    /// True when every one of the five played <see cref="FootballClearMinGames"/> games and the quarterback threw
    /// <see cref="FootballClearQuarterback"/> times as often as anyone else: a season with no injury muddle.
    /// </summary>
    internal static bool IsClearFootball(IReadOnlyList<FootballRosterLine> lines)
    {
        if (FootballLineup(lines, null) is not { } five) return false;
        if (five.Any(f => f.Line.Games < FootballClearMinGames)) return false;
        var quarterbacks = Leaders(lines, ["QB"], "att");
        return quarterbacks.Count < 2 || quarterbacks[0]["att"] >= FootballClearQuarterback * quarterbacks[1]["att"];
    }

    /// <summary>The season's lines at those positions with any of the stat, most first; games played, then id, break a tie.</summary>
    private static List<FootballRosterLine> Leaders(IReadOnlyList<FootballRosterLine> lines, string[] positions, string key) =>
        lines.Where(l => l.Position is not null && positions.Contains(l.Position, StringComparer.OrdinalIgnoreCase) && l[key] > 0)
            .OrderByDescending(l => l[key]).ThenByDescending(l => l.Games).ThenBy(l => l.PlayerId)
            .ToList();

    /// <summary>The season totals a slot shows for a position.</summary>
    internal static IReadOnlyDictionary<string, double> SlotStats(string position, FootballRosterLine line)
    {
        string[] keys = position switch
        {
            "QB" => ["pyd", "ptd", "int"],
            "RB" => ["car", "ryd", "rtd"],
            _ => ["rec", "wyd", "wtd"],
        };
        return keys.ToDictionary(k => k, k => line[k]);
    }

    /// <summary>
    /// A playoff team since 1999 with a clear five, weighted towards famous ones, leaving the Super Bowl teams to the
    /// dailies. Only finished seasons: the NFL's ends with the Super Bowl in February.
    /// </summary>
    private async Task<GeneratedPuzzle?> FootballPracticeAsync(GenerationContext context, List<Team> teams, CancellationToken ct)
    {
        var finished = context.AsOf.Month >= 3 ? context.AsOf.Year - 1 : context.AsOf.Year - 2;
        var superBowl = SuperBowlTeams
            .Select(f => FindTeam(teams, f.Team, f.Season) is { } t ? (t.Id, f.Season) : (0, 0))
            .ToHashSet();
        var playoffs = (await db.TeamSeasons.AsNoTracking()
                .Where(t => t.Team!.LeagueId == context.League.Id && t.Season >= FirstFootballSeason)
                .Select(t => new { t.TeamId, t.Season, t.MadePlayoffs })
                .ToListAsync(ct))
            .ToDictionary(t => (t.TeamId, t.Season), t => t.MadePlayoffs);

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.TeamId != null && s.Season >= FirstFootballSeason && s.Season <= finished && s.Player!.LeagueId == context.League.Id)
            .Select(s => new { s.PlayerId, TeamId = s.TeamId!.Value, s.Season, s.Games, s.StatsJson, s.Player!.Position, s.Player.Popularity })
            .ToListAsync(ct);

        var candidates = new List<((int TeamId, int Season) Key, double Fame)>();
        foreach (var group in rows.GroupBy(r => (r.TeamId, r.Season)))
        {
            if (superBowl.Contains(group.Key)) continue;
            // Only playoff teams; a season with no record makes no photo (see BuildFootballAsync).
            if (!playoffs.GetValueOrDefault(group.Key)) continue;

            var lines = group.Select(r => FootballLine(r.PlayerId, r.Position, r.Games, r.StatsJson)).ToList();
            if (!IsClearFootball(lines)) continue;
            var five = FootballLineup(lines, null)!.Select(f => f.Line.PlayerId).ToHashSet();
            candidates.Add((group.Key, group.Where(r => five.Contains(r.PlayerId)).Sum(r => r.Popularity)));
        }
        if (candidates.Count == 0) return null;

        var pick = WeightedPick.From(candidates.OrderBy(c => c.Key).ToList(), c => c.Fame * c.Fame, context.Random).Key;
        return await BuildFootballAsync(teams.First(t => t.Id == pick.TeamId), pick.Season, null, null, ct);
    }

    /// <summary>
    /// The photo of one team-season, or null without a full five or a record. Every season since 1999 has a record once
    /// the schedule is imported, and the same history load brings the stat lines of careers that began before 1999; until
    /// then a 1999 Rams photo would put Kurt Warner's backup at quarterback, so no photo is made.
    /// </summary>
    private async Task<GeneratedPuzzle?> BuildFootballAsync(Team team, int season, string? finals, IReadOnlyDictionary<string, string>? swap, CancellationToken ct)
    {
        var record = await db.TeamSeasons.AsNoTracking().FirstOrDefaultAsync(t => t.TeamId == team.Id && t.Season == season, ct);
        if (record is null) return null;

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.TeamId == team.Id && s.Season == season)
            .ToListAsync(ct);
        var lines = rows.Select(r => FootballLine(r.PlayerId, r.Player!.Position, r.Games, r.StatsJson)).ToList();
        if (FootballLineup(lines, await SwapsAsync("gsis", swap, ct)) is not { } five) return null;

        var players = rows.Select(r => r.Player!).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        // Everyone on the roster that season is the bench, linemen and kickers too, who have no stat line.
        var roster = await db.PlayerTeamStints.AsNoTracking()
            .Where(s => s.TeamId == team.Id && s.StartSeason <= season && (s.EndSeason == null || s.EndSeason >= season))
            .Select(s => s.PlayerId)
            .ToListAsync(ct);

        var payload = new FootballPhotoPayload(
            new PhotoTeam(team.City, team.Name, team.Abbreviation, team.LogoUrl),
            season,
            record.Wins,
            record.Losses,
            record.Ties,
            finals,
            five.Select(f => new FootballSlot(f.Position, f.Row, f.Line.Games, SlotStats(f.Position, f.Line))).ToList());
        var starters = five.Select(f => f.Line.PlayerId).ToHashSet();
        var secret = new PhotoSecret(
            $"{season}-{team.Abbreviation}",
            five.Select(f => new PhotoAnswer(f.Line.PlayerId, players[f.Line.PlayerId].FullName, players[f.Line.PlayerId].HeadshotUrl)).ToList(),
            roster.Concat(players.Keys).Where(id => !starters.Contains(id)).Distinct().Order().ToList());

        // Older teams and lesser-known fives are harder.
        var fame = five.Average(f => players[f.Line.PlayerId].Popularity);
        var difficulty = (int)Math.Clamp(Math.Round(100 - fame + Math.Max(0, 2015 - season) / 2.0), 0, 100);
        return new GeneratedPuzzle(null, GameJson.Write(payload), GameJson.Write(secret), difficulty);
    }

    private static FootballRosterLine FootballLine(int playerId, string? position, int? games, string statsJson) =>
        new(playerId, position, games ?? 0, BackOfTheCardGame.FootballLine.Read(new PlayerSeasonStat { Games = games, StatsJson = statsJson }).Totals);
}

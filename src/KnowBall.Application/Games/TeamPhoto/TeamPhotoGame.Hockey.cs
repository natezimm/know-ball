using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Domain;

namespace KnowBall.Application.Games.TeamPhoto;

/// <param name="Season">Start year: 2023 is 2023-24, the season that ended in the 2024 Stanley Cup Final.</param>
/// <param name="Ties">Ties, which ended after 2003-04; overtime losses are apart from losses.</param>
/// <param name="Finals">"won" or "lost" for a Stanley Cup Final team; null for a practice team that did not get there.</param>
/// <param name="Slots">Season totals by card key: g, a and pts for a skater; w, gaa and svp for the goalie.</param>
public sealed record HockeyPhotoPayload(
    PhotoTeam Team, int Season, int? Wins, int? Losses, int? Ties, int? OvertimeLosses, string? Finals, IReadOnlyList<FootballSlot> Slots);

/// <summary>
/// The NHL edition: a starting six of a left wing, a centre and a right wing at the back, and two defencemen either side
/// of the goalie in front. The forwards are the team's three top scorers up front and the defencemen its two top
/// scorers on defence that season, and the goalie the one who started the most games. Daily puzzles are both Stanley Cup Final teams of every season since
/// 1979-80; practice draws from the other playoff teams since then whose six are clear.
/// </summary>
public sealed partial class TeamPhotoGame
{
    /// <summary>The first season of the NHL record, after the WHA merger.</summary>
    public const int FirstHockeySeason = 1979;
    /// <summary>A practice skater played at least this many games...</summary>
    public const int HockeyClearMinGames = 50;
    /// <summary>...and the goalie started at least this many times as often as his backup.</summary>
    public const double HockeyClearGoalie = 1.5;

    private static readonly Lazy<IReadOnlyList<FinalsTeam>> HockeyPool = new(() => LoadPool("stanley-cup-teams.json"));

    /// <summary>Both Stanley Cup Final teams of every season since 1979-80, from the embedded list.</summary>
    public static IReadOnlyList<FinalsTeam> StanleyCupTeams => HockeyPool.Value;

    /// <summary>The six places in the photo, in payload order.</summary>
    private static readonly (string Position, string Row)[] HockeySpots =
    [
        ("LW", "back"), ("C", "back"), ("RW", "back"), ("D", "front"), ("G", "front"), ("D", "front"),
    ];

    private static readonly string[] Forwards = ["C", "LW", "RW"];

    /// <summary>
    /// The six in payload order, or null when the team has no goalie, two defencemen or three forwards. The forwards are
    /// the team's three top scorers up front, each standing at his own position where it is free: the NHL keeps one
    /// position for a whole career, and Messier, a centre on the record, played the wing beside Gretzky.
    /// </summary>
    internal static List<(FootballRosterLine Line, string Position, string Row)>? HockeyLineup(
        IReadOnlyList<FootballRosterLine> lines, IReadOnlyDictionary<int, int>? swaps)
    {
        var goalies = Goalies(lines);
        var defence = Scorers(lines, ["D"]);
        var forwards = Scorers(lines, Forwards);
        if (goalies.Count == 0 || defence.Count < 2 || forwards.Count < 3) return null;

        var top = forwards.Take(3).ToList();
        var spots = new FootballRosterLine?[3];
        string[] order = ["LW", "C", "RW"];
        foreach (var line in top)
        {
            var at = Array.FindIndex(order, (p) => string.Equals(p, line.Position, StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && spots[at] is null) spots[at] = line;
        }
        foreach (var line in top.Where(l => !spots.Contains(l)))
            spots[Array.IndexOf(spots, null)] = line;
        var picked = spots.Select(l => l!).ToList();
        picked.AddRange([defence[0], goalies[0], defence[1]]);

        foreach (var (outId, inId) in swaps ?? new Dictionary<int, int>())
        {
            var at = picked.FindIndex(l => l.PlayerId == outId);
            var incoming = lines.FirstOrDefault(l => l.PlayerId == inId);
            if (at >= 0 && incoming is not null && picked.All(l => l.PlayerId != inId)) picked[at] = incoming;
        }
        return picked.Select((l, i) => (l, HockeySpots[i].Position, HockeySpots[i].Row)).ToList();
    }

    /// <summary>
    /// True when every skater of the six played <see cref="HockeyClearMinGames"/> games and the goalie started
    /// <see cref="HockeyClearGoalie"/> times as often as anyone else: a season with no muddle in goal.
    /// </summary>
    internal static bool IsClearHockey(IReadOnlyList<FootballRosterLine> lines)
    {
        if (HockeyLineup(lines, null) is not { } six) return false;
        if (six.Any(s => s.Position != "G" && s.Line.Games < HockeyClearMinGames)) return false;
        var goalies = Goalies(lines);
        return goalies.Count < 2 || Starts(goalies[0]) >= HockeyClearGoalie * Starts(goalies[1]);
    }

    /// <summary>Skaters at those positions, most points first; games played, then id, break a tie.</summary>
    private static List<FootballRosterLine> Scorers(IReadOnlyList<FootballRosterLine> lines, string[] positions) =>
        lines.Where(l => l.Position is not null && positions.Contains(l.Position, StringComparer.OrdinalIgnoreCase) && l.Games > 0)
            .OrderByDescending(l => l["pts"]).ThenByDescending(l => l.Games).ThenBy(l => l.PlayerId)
            .ToList();

    /// <summary>Goalies, most games started first (games played where starts were not kept).</summary>
    private static List<FootballRosterLine> Goalies(IReadOnlyList<FootballRosterLine> lines) =>
        lines.Where(l => string.Equals(l.Position, "G", StringComparison.OrdinalIgnoreCase) && l.Games > 0)
            .OrderByDescending(Starts).ThenByDescending(l => l["w"]).ThenBy(l => l.PlayerId)
            .ToList();

    private static double Starts(FootballRosterLine goalie) => goalie["gs"] > 0 ? goalie["gs"] : goalie.Games;

    /// <summary>The season totals a hockey slot shows.</summary>
    internal static IReadOnlyDictionary<string, double> HockeySlotStats(string position, FootballRosterLine line)
    {
        string[] keys = position == "G" ? ["w", "gaa", "svp"] : ["g", "a", "pts"];
        return keys.ToDictionary(k => k, k => line[k]);
    }

    /// <summary>
    /// A playoff team since 1979-80 with a clear six, weighted towards famous ones, leaving the Stanley Cup Final teams
    /// to the dailies. Only finished seasons: the NHL's ends in June.
    /// </summary>
    private async Task<GeneratedPuzzle?> HockeyPracticeAsync(GenerationContext context, List<Team> teams, CancellationToken ct)
    {
        var finished = context.AsOf.Month >= 7 ? context.AsOf.Year - 1 : context.AsOf.Year - 2;
        var final = StanleyCupTeams
            .Select(f => FindTeam(teams, f.Team, f.Season) is { } t ? (t.Id, f.Season) : (0, 0))
            .ToHashSet();
        var playoffs = (await db.TeamSeasons.AsNoTracking()
                .Where(t => t.Team!.LeagueId == context.League.Id && t.Season >= FirstHockeySeason)
                .Select(t => new { t.TeamId, t.Season, t.MadePlayoffs })
                .ToListAsync(ct))
            .ToDictionary(t => (t.TeamId, t.Season), t => t.MadePlayoffs);

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.TeamId != null && s.Season >= FirstHockeySeason && s.Season <= finished && s.Player!.LeagueId == context.League.Id)
            .Select(s => new { s.PlayerId, TeamId = s.TeamId!.Value, s.Season, s.Games, s.StatsJson, s.Player!.Position, s.Player.Popularity })
            .ToListAsync(ct);

        var candidates = new List<((int TeamId, int Season) Key, double Fame)>();
        foreach (var group in rows.GroupBy(r => (r.TeamId, r.Season)))
        {
            if (final.Contains(group.Key) || !playoffs.GetValueOrDefault(group.Key)) continue;

            var lines = group.Select(r => FootballLine(r.PlayerId, r.Position, r.Games, r.StatsJson)).ToList();
            if (!IsClearHockey(lines)) continue;
            var six = HockeyLineup(lines, null)!.Select(f => f.Line.PlayerId).ToHashSet();
            candidates.Add((group.Key, group.Where(r => six.Contains(r.PlayerId)).Sum(r => r.Popularity)));
        }
        if (candidates.Count == 0) return null;

        var pick = WeightedPick.From(candidates.OrderBy(c => c.Key).ToList(), c => c.Fame * c.Fame, context.Random).Key;
        return await BuildHockeyAsync(teams.First(t => t.Id == pick.TeamId), pick.Season, null, null, ct);
    }

    /// <summary>The photo of one team-season, or null without a full six or a record.</summary>
    private async Task<GeneratedPuzzle?> BuildHockeyAsync(Team team, int season, string? finals, IReadOnlyDictionary<string, string>? swap, CancellationToken ct)
    {
        var record = await db.TeamSeasons.AsNoTracking().FirstOrDefaultAsync(t => t.TeamId == team.Id && t.Season == season, ct);
        if (record is null) return null;

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.TeamId == team.Id && s.Season == season)
            .ToListAsync(ct);
        var lines = rows.Select(r => FootballLine(r.PlayerId, r.Player!.Position, r.Games, r.StatsJson)).ToList();
        if (HockeyLineup(lines, await SwapsAsync("nhl", swap, ct)) is not { } six) return null;

        var players = rows.Select(r => r.Player!).DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        var roster = await db.PlayerTeamStints.AsNoTracking()
            .Where(s => s.TeamId == team.Id && s.StartSeason <= season && (s.EndSeason == null || s.EndSeason >= season))
            .Select(s => s.PlayerId)
            .ToListAsync(ct);

        var payload = new HockeyPhotoPayload(
            new PhotoTeam(team.City, team.Name, team.Abbreviation, team.LogoUrl),
            season,
            record.Wins,
            record.Losses,
            record.Ties,
            record.OvertimeLosses,
            finals,
            six.Select(f => new FootballSlot(f.Position, f.Row, f.Line.Games, HockeySlotStats(f.Position, f.Line))).ToList());
        var starters = six.Select(f => f.Line.PlayerId).ToHashSet();
        var secret = new PhotoSecret(
            $"{season}-{team.Abbreviation}",
            six.Select(f => new PhotoAnswer(f.Line.PlayerId, players[f.Line.PlayerId].FullName, players[f.Line.PlayerId].HeadshotUrl)).ToList(),
            roster.Concat(players.Keys).Where(id => !starters.Contains(id)).Distinct().Order().ToList());

        // Older teams and lesser-known sixes are harder.
        var fame = six.Average(f => players[f.Line.PlayerId].Popularity);
        var difficulty = (int)Math.Clamp(Math.Round(100 - fame + Math.Max(0, 2005 - season) / 2.0), 0, 100);
        return new GeneratedPuzzle(null, GameJson.Write(payload), GameJson.Write(secret), difficulty);
    }
}

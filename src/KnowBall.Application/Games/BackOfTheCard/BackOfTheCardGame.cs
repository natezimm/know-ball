using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Import;
using KnowBall.Domain;

namespace KnowBall.Application.Games.BackOfTheCard;

/// <summary>One printed line on the card. <see cref="Season"/> is the start year: 2013 is 2013-14.</summary>
public sealed record CardSeason(int Season, int Games, double Points, double Rebounds, double Assists);

/// <summary>The career line under the seasons: per-game averages weighted by games played.</summary>
public sealed record CardCareer(int Seasons, int Games, double Points, double Rebounds, double Assists);

public sealed record CardPayload(IReadOnlyList<CardSeason> Seasons, CardCareer Career);

/// <summary>A football card's stat column after games, and how many decimals it prints with.</summary>
public sealed record CardColumn(string Label, int Decimals);

/// <summary>One printed football season: games and the season totals, one value per column.</summary>
public sealed record FootballCardSeason(int Season, int Games, IReadOnlyList<double> Values);

/// <summary>The career line: totals over every printed season, and rates worked out from them.</summary>
public sealed record FootballCardCareer(int Seasons, int Games, IReadOnlyList<double> Values);

/// <summary>A football card: the columns suit his position (passing, rushing, receiving or defence) and every number is a season total.</summary>
public sealed record FootballCardPayload(IReadOnlyList<CardColumn> Columns, IReadOnlyList<FootballCardSeason> Seasons, FootballCardCareer Career);

/// <param name="Teams">The club for each printed season, in the same order: "OKC", or "BKN/PHX" for a season split by a trade.</param>
/// <param name="Stamps">The award printed beside each season, or null; the best one when there were several.</param>
public sealed record CardSecret(
    int PlayerId,
    string Name,
    string? HeadshotUrl,
    IReadOnlyList<string> Teams,
    string Position,
    string? Height,
    IReadOnlyList<string?> Stamps,
    string Drafted,
    CareerHint Origin);

/// <summary>
/// The back of a player's trading card with the name blacked out: every season's games, points,
/// rebounds and assists, or for a football player the season totals his position is known by. Five guesses; each of the first four misses uncovers more of the card: the
/// clubs, then position and height, then award stamps, then the draft line and college, so the last
/// guess is made with the whole card showing.
/// </summary>
public sealed class BackOfTheCardGame(ITriviaDb db) : IGame
{
    public const string GameSlug = "back-of-the-card";

    /// <summary>A card needs at least this many seasons on it to be a puzzle.</summary>
    public const int MinSeasons = 5;
    /// <summary>Answers are drawn from this many of the best-known players with enough seasons.</summary>
    public const int PoolSize = 300;
    /// <summary>Answers played at least one season from this year on.</summary>
    public const int MinLastSeason = 1980;
    /// <summary>Football answers began their careers this season or later: the first with season stats.</summary>
    public const int NflMinFirstSeason = 1999;

    /// <summary>What each miss uncovers, in order. The labels are shown before they open.</summary>
    public static readonly IReadOnlyList<string> Steps = ["Teams", "Position", "Awards", "Draft"];

    private Dictionary<int, List<int>> _pools = [];

    public GameType Type => GameType.BackOfTheCard;
    public string Slug => GameSlug;
    public string Name => "Back of the Card";
    /// <summary>One guess per step and a last one with the whole card open, so no miss is wasted.</summary>
    public int MaxGuesses => Steps.Count + 1;
    public bool IsGenerated => true;
    public bool IncludesRetired => true;
    public IReadOnlyList<string> Leagues => [LeagueCodes.Nba, LeagueCodes.Nfl];

    public async Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
    {
        var pool = await PoolAsync(context.League.Id, cancellationToken);
        if (pool.Count == 0) return null;

        var fresh = pool.Where(id => !context.RecentAnswerIds.Contains(id)).ToList();
        if (fresh.Count == 0) fresh = pool;
        var popularity = await db.Players.AsNoTracking()
            .Where(p => fresh.Contains(p.Id))
            .Select(p => new { p.Id, p.Popularity })
            .ToListAsync(cancellationToken);
        var answerId = WeightedPick.From(popularity.OrderBy(p => p.Id).ToList(), p => p.Popularity + 10, context.Random).Id;

        var answer = await db.Players.AsNoTracking()
            .Include(p => p.SeasonStats).ThenInclude(s => s.Team)
            .Include(p => p.Stints)
            .Include(p => p.Awards)
            .FirstAsync(p => p.Id == answerId, cancellationToken);

        var awards = answer.Awards.ToLookup(a => a.Season, a => a.Type);
        var hints = CareerPathGame.HintsFor(answer);
        CardSecret Secret(IEnumerable<int> printed, List<string> teams, Func<IEnumerable<string>, string?> stamp) => new(
            answer.Id,
            answer.FullName,
            answer.HeadshotUrl,
            teams,
            answer.Position ?? "Unknown",
            answer.HeightInches is { } inches ? AttributeComparer.FormatHeight(inches) : null,
            printed.Select(season => stamp(awards[season])).ToList(),
            hints[1].Value!,
            hints[2]);

        if (context.League.Code == LeagueCodes.Nfl)
        {
            var (lines, clubs) = Lines(answer.SeasonStats, answer.Stints, s => s.Games > 0 ? FootballLine.Read(s) : null);
            var payload = FootballCard(answer.Position, lines);
            var secret = Secret(lines.Select(l => l.Season), clubs, FootballStamp);
            return new GeneratedPuzzle(answer.Id, GameJson.Write(payload), GameJson.Write(secret), Difficulty: lines.Count);
        }
        else
        {
            var (seasons, teams) = Lines(answer.SeasonStats, answer.Stints);
            var payload = new CardPayload(seasons, Career(seasons));
            var secret = Secret(seasons.Select(s => s.Season), teams, Stamp);
            return new GeneratedPuzzle(answer.Id, GameJson.Write(payload), GameJson.Write(secret), Difficulty: seasons.Count);
        }
    }

    public Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken)
    {
        var secret = GameJson.Read<CardSecret>(puzzle.SecretJson);
        return Task.FromResult(new GuessEvaluation(guess.Id == secret.PlayerId));
    }

    /// <summary>
    /// One step of the card opens per wrong guess, and a finished game shows it all. Locked parts are
    /// absent, so nothing about them reaches the browser early.
    /// </summary>
    public JsonNode? Clues(Puzzle puzzle, int misses, bool complete)
    {
        var secret = GameJson.Read<CardSecret>(puzzle.SecretJson);
        var open = complete ? Steps.Count : Math.Min(misses, Steps.Count);

        var clues = new JsonObject
        {
            ["steps"] = new JsonArray(Steps.Select((label, i) => (JsonNode)new JsonObject { ["label"] = label, ["open"] = i < open }).ToArray()),
        };
        if (open >= 1) clues["teams"] = new JsonArray(secret.Teams.Select(t => (JsonNode)JsonValue.Create(t)).ToArray());
        if (open >= 2)
        {
            clues["position"] = secret.Position;
            if (secret.Height is not null) clues["height"] = secret.Height;
        }
        if (open >= 3) clues["stamps"] = new JsonArray(secret.Stamps.Select(s => (JsonNode?)(s is null ? null : JsonValue.Create(s))).ToArray());
        if (open >= 4)
        {
            clues["drafted"] = secret.Drafted;
            clues["origin"] = new JsonObject { ["label"] = secret.Origin.Label, ["value"] = secret.Origin.Value };
        }
        return clues;
    }

    public JsonNode Reveal(Puzzle puzzle)
    {
        var secret = GameJson.Read<CardSecret>(puzzle.SecretJson);
        return new JsonObject
        {
            ["playerId"] = secret.PlayerId,
            ["name"] = secret.Name,
            ["headshotUrl"] = secret.HeadshotUrl,
        };
    }

    /// <summary>
    /// The printed seasons, oldest first, and the club for each. A traded player's season is one
    /// line from the combined row, with the clubs he played for that year joined by a slash in the
    /// order he played for them, which comes from his stints (the stat rows are not in that order).
    /// </summary>
    internal static (List<CardSeason> Seasons, List<string> Teams) Lines(IEnumerable<PlayerSeasonStat> stats, IEnumerable<PlayerTeamStint> stints) =>
        Lines(stats, stints, total => total is { Games: > 0, PointsPerGame: not null }
            ? new CardSeason(
                total.Season,
                total.Games.Value,
                Math.Round(total.PointsPerGame.Value, 1),
                Math.Round(total.ReboundsPerGame ?? 0, 1),
                Math.Round(total.AssistsPerGame ?? 0, 1))
            : null);

    /// <summary>
    /// The printed seasons and their clubs, with <paramref name="line"/> making each season's line from its combined
    /// row, or null to leave the season off the card.
    /// </summary>
    internal static (List<T> Seasons, List<string> Teams) Lines<T>(IEnumerable<PlayerSeasonStat> stats, IEnumerable<PlayerTeamStint> stints, Func<PlayerSeasonStat, T?> line)
        where T : class
    {
        var spells = stints.ToList();
        // The stint with that club nearest the season: the one covering it, if the stints are complete.
        int Played(PlayerSeasonStat row) => spells
            .Where(s => s.TeamId == row.TeamId)
            .OrderBy(s => Math.Max(0, Math.Max(s.StartSeason - row.Season, row.Season - (s.EndSeason ?? int.MaxValue))))
            .Select(s => s.Order)
            .DefaultIfEmpty(int.MaxValue)
            .First();

        var seasons = new List<T>();
        var teams = new List<string>();
        foreach (var year in stats.GroupBy(s => s.Season).OrderBy(g => g.Key))
        {
            var total = year.FirstOrDefault(s => s.IsSeasonTotal);
            if (total is null || line(total) is not { } printed) continue;
            seasons.Add(printed);

            var clubs = total.Team is { } one
                ? [one.Abbreviation]
                : year.Where(s => !s.IsSeasonTotal && s.Team is not null).OrderBy(Played).ThenBy(s => s.Id).Select(s => s.Team!.Abbreviation).Distinct().ToList();
            teams.Add(clubs.Count > 0 ? string.Join('/', clubs) : "—");
        }
        return (seasons, teams);
    }

    internal static CardCareer Career(IReadOnlyList<CardSeason> seasons)
    {
        var games = seasons.Sum(s => s.Games);
        double Average(Func<CardSeason, double> stat) =>
            games == 0 ? 0 : Math.Round(seasons.Sum(s => stat(s) * s.Games) / games, 1);
        return new CardCareer(seasons.Count, games, Average(s => s.Points), Average(s => s.Rebounds), Average(s => s.Assists));
    }

    /// <summary>The one award a card would print beside a season, best first.</summary>
    internal static string? Stamp(IEnumerable<string> awards)
    {
        var won = awards.ToHashSet(StringComparer.Ordinal);
        if (won.Contains(AwardTypes.Mvp)) return "MVP";
        if (won.Contains(AwardTypes.DefensivePlayer)) return "DPOY";
        if (won.Contains(AwardTypes.Rookie)) return "ROY";
        if (won.Contains(AwardTypes.SixthMan)) return "6MOY";
        if (won.Contains(AwardTypes.MostImproved)) return "MIP";
        if (won.Any(a => a.StartsWith(AwardTypes.AllLeague, StringComparison.Ordinal))) return "All-NBA";
        if (won.Contains(AwardTypes.AllStar)) return "All-Star";
        return null;
    }

    /// <summary>
    /// The award a football card prints beside a season, best first. A second-team All-Pro is printed as "All-Pro 2nd".
    /// </summary>
    internal static string? FootballStamp(IEnumerable<string> awards)
    {
        var won = awards.ToHashSet(StringComparer.Ordinal);
        if (won.Contains(AwardTypes.Mvp)) return "MVP";
        if (won.Contains(AwardTypes.OffensivePlayer)) return "OPOY";
        if (won.Contains(AwardTypes.DefensivePlayer)) return "DPOY";
        if (won.Contains(AwardTypes.SuperBowlMvp)) return "SB MVP";
        if (won.Contains(AwardTypes.OffensiveRookie)) return "OROY";
        if (won.Contains(AwardTypes.DefensiveRookie)) return "DROY";
        if (won.Contains(AwardTypes.Comeback)) return "CPOY";
        if (won.Contains(AwardTypes.AllPro + "1")) return "All-Pro";
        if (won.Contains(AwardTypes.AllPro + "2")) return "All-Pro 2nd";
        if (won.Contains(AwardTypes.ProBowl)) return "Pro Bowl";
        return null;
    }

    /// <summary>One football season's totals, read from the stat row's card JSON.</summary>
    internal sealed record FootballLine(int Season, int Games, IReadOnlyDictionary<string, double> Totals)
    {
        public double this[string key] => Totals.GetValueOrDefault(key);

        public static FootballLine Read(PlayerSeasonStat row)
        {
            var totals = new Dictionary<string, double>();
            if (JsonNode.Parse(string.IsNullOrWhiteSpace(row.StatsJson) ? "{}" : row.StatsJson) is JsonObject json)
            {
                foreach (var (key, value) in json)
                {
                    if (value is JsonValue v && v.TryGetValue<double>(out var number)) totals[key] = number;
                }
            }
            return new FootballLine(row.Season, row.Games ?? 0, totals);
        }
    }

    /// <summary>The stat columns for a position, and how to fill them from a season's totals (or a career's).</summary>
    private sealed record FootballColumns(IReadOnlyList<CardColumn> Columns, Func<Func<string, double>, double[]> Values);

    private static double Rate(double part, double whole, double scale = 1) => whole > 0 ? Math.Round(part / whole * scale, 1) : 0;

    private static readonly FootballColumns Passing = new(
        [new("Cmp%", 1), new("Yds", 0), new("TD", 0), new("Int", 0)],
        t => [Rate(t("cmp"), t("att"), 100), t("pyd"), t("ptd"), t("int")]);

    private static readonly FootballColumns Rushing = new(
        [new("Car", 0), new("Yds", 0), new("TD", 0), new("Rec", 0)],
        t => [t("car"), t("ryd"), t("rtd"), t("rec")]);

    private static readonly FootballColumns Receiving = new(
        [new("Rec", 0), new("Yds", 0), new("TD", 0), new("Avg", 1)],
        t => [t("rec"), t("wyd"), t("wtd"), Rate(t("wyd"), t("rec"))]);

    private static readonly FootballColumns Defense = new(
        [new("Tkl", 0), new("Sacks", 1), new("Int", 0), new("FF", 0)],
        t => [t("tkl"), t("sk"), t("dint"), t("ff")]);

    private static FootballColumns ColumnsFor(string? position) => position switch
    {
        "QB" => Passing,
        "RB" or "HB" or "FB" => Rushing,
        "WR" or "TE" => Receiving,
        _ => Defense,
    };

    /// <summary>The football card for a player's printed seasons: the columns his position is known by, and a career line of totals.</summary>
    internal static FootballCardPayload FootballCard(string? position, IReadOnlyList<FootballLine> lines)
    {
        var columns = ColumnsFor(position);
        var seasons = lines.Select(l => new FootballCardSeason(l.Season, l.Games, columns.Values(key => l[key]))).ToList();
        var career = new FootballCardCareer(lines.Count, lines.Sum(l => l.Games), columns.Values(key => lines.Sum(l => l[key])));
        return new FootballCardPayload(columns.Columns, seasons, career);
    }

    /// <summary>Well-known players with enough printed seasons, loaded once per request or import run.</summary>
    private async Task<List<int>> PoolAsync(int leagueId, CancellationToken ct)
    {
        if (_pools.TryGetValue(leagueId, out var cached)) return cached;

        // A football season is printed from its games and totals; a basketball one needs its scoring average.
        var football = await db.Leagues.AnyAsync(l => l.Id == leagueId && l.Code == LeagueCodes.Nfl, ct);
        var rows = db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.Player!.LeagueId == leagueId && s.IsSeasonTotal && s.Games > 0);
        if (!football) rows = rows.Where(s => s.PointsPerGame != null);
        var seasons = await rows
            .GroupBy(s => s.PlayerId)
            .Select(g => new { PlayerId = g.Key, Count = g.Select(s => s.Season).Distinct().Count() })
            .ToListAsync(ct);
        var eligible = seasons.Where(s => s.Count >= MinSeasons).Select(s => s.PlayerId).ToHashSet();

        var players = db.Players.AsNoTracking()
            .Where(p => p.LeagueId == leagueId && (p.ToYear == null || p.ToYear >= MinLastSeason));
        if (football)
        {
            players = players.Where(p => p.FromYear != null && p.FromYear >= NflMinFirstSeason
                                         && (p.Position == null || !ScoutingReportGame.NeverAnswers.Contains(p.Position)));
        }
        var candidates = await players.Select(p => new { p.Id, p.Popularity }).ToListAsync(ct);

        var pool = candidates
            .Where(p => eligible.Contains(p.Id))
            .OrderByDescending(p => p.Popularity).ThenBy(p => p.Id)
            .Take(PoolSize)
            .Select(p => p.Id)
            .ToList();
        return _pools[leagueId] = pool;
    }
}

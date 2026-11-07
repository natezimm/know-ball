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
/// rebounds and assists. Eight guesses; each miss uncovers more of the card: the clubs, then
/// position and height, then award stamps, then the draft line and college.
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

    /// <summary>What each miss uncovers, in order. The labels are shown before they open.</summary>
    public static readonly IReadOnlyList<string> Steps = ["Teams", "Position", "Awards", "Draft"];

    private Dictionary<int, List<int>> _pools = [];

    public GameType Type => GameType.BackOfTheCard;
    public string Slug => GameSlug;
    public string Name => "Back of the Card";
    public int MaxGuesses => 8;
    public bool IsGenerated => true;
    public bool IncludesRetired => true;

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

        var (seasons, teams) = Lines(answer.SeasonStats, answer.Stints);
        var awards = answer.Awards.ToLookup(a => a.Season, a => a.Type);
        var hints = CareerPathGame.HintsFor(answer);

        var secret = new CardSecret(
            answer.Id,
            answer.FullName,
            answer.HeadshotUrl,
            teams,
            answer.Position ?? "Unknown",
            answer.HeightInches is { } inches ? AttributeComparer.FormatHeight(inches) : null,
            seasons.Select(s => Stamp(awards[s.Season])).ToList(),
            hints[1].Value!,
            hints[2]);
        var payload = new CardPayload(seasons, Career(seasons));
        return new GeneratedPuzzle(answer.Id, GameJson.Write(payload), GameJson.Write(secret), Difficulty: seasons.Count);
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
    internal static (List<CardSeason> Seasons, List<string> Teams) Lines(IEnumerable<PlayerSeasonStat> stats, IEnumerable<PlayerTeamStint> stints)
    {
        var spells = stints.ToList();
        // The stint with that club nearest the season: the one covering it, if the stints are complete.
        int Played(PlayerSeasonStat row) => spells
            .Where(s => s.TeamId == row.TeamId)
            .OrderBy(s => Math.Max(0, Math.Max(s.StartSeason - row.Season, row.Season - (s.EndSeason ?? int.MaxValue))))
            .Select(s => s.Order)
            .DefaultIfEmpty(int.MaxValue)
            .First();

        var seasons = new List<CardSeason>();
        var teams = new List<string>();
        foreach (var year in stats.GroupBy(s => s.Season).OrderBy(g => g.Key))
        {
            var total = year.FirstOrDefault(s => s.IsSeasonTotal);
            if (total is not { Games: > 0, PointsPerGame: not null }) continue;

            seasons.Add(new CardSeason(
                year.Key,
                total.Games.Value,
                Math.Round(total.PointsPerGame.Value, 1),
                Math.Round(total.ReboundsPerGame ?? 0, 1),
                Math.Round(total.AssistsPerGame ?? 0, 1)));

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

    /// <summary>Well-known players with enough printed seasons, loaded once per request or import run.</summary>
    private async Task<List<int>> PoolAsync(int leagueId, CancellationToken ct)
    {
        if (_pools.TryGetValue(leagueId, out var cached)) return cached;

        var seasons = await db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.Player!.LeagueId == leagueId && s.IsSeasonTotal && s.Games > 0 && s.PointsPerGame != null)
            .GroupBy(s => s.PlayerId)
            .Select(g => new { PlayerId = g.Key, Count = g.Select(s => s.Season).Distinct().Count() })
            .ToListAsync(ct);
        var eligible = seasons.Where(s => s.Count >= MinSeasons).Select(s => s.PlayerId).ToHashSet();

        var candidates = await db.Players.AsNoTracking()
            .Where(p => p.LeagueId == leagueId && (p.ToYear == null || p.ToYear >= MinLastSeason))
            .Select(p => new { p.Id, p.Popularity })
            .ToListAsync(ct);

        var pool = candidates
            .Where(p => eligible.Contains(p.Id))
            .OrderByDescending(p => p.Popularity).ThenBy(p => p.Id)
            .Take(PoolSize)
            .Select(p => p.Id)
            .ToList();
        return _pools[leagueId] = pool;
    }
}

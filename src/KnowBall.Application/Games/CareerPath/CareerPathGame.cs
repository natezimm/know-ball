using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Domain;

namespace KnowBall.Application.Games.CareerPath;

/// <summary>One stop on the journey. <see cref="To"/> is the calendar year the last season ended; null if the player is still there.</summary>
public sealed record CareerStop(string Team, string Abbreviation, string? LogoUrl, int From, int? To, int Seasons);

public sealed record CareerPayload(IReadOnlyList<CareerStop> Stops);

public sealed record CareerHint(string Label, string? Value);

/// <param name="Accepted">Everyone whose career ran through the same franchises in the same order; any of them is a right answer.</param>
/// <param name="Franchises">The franchise behind each stop, for telling a wrong guess which stops it shares.</param>
public sealed record CareerSecret(
    int PlayerId,
    string Name,
    string? HeadshotUrl,
    IReadOnlyList<int> Accepted,
    IReadOnlyList<int> Franchises,
    IReadOnlyList<CareerHint> Hints);

/// <summary>
/// Every team a player has been on, in order: who is it? Six guesses, and each miss unlocks a
/// hint (position, draft, college, initials) and says which of the stops the guess shares.
/// </summary>
public sealed class CareerPathGame(ITriviaDb db) : IGame
{
    /// <summary>A path needs at least this many franchises to be a puzzle.</summary>
    public const int MinFranchises = 3;
    /// <summary>Answers are drawn from this many of the best-known players with a long enough path.</summary>
    public const int PoolSize = 250;
    /// <summary>Answers played at least one season from this year on, so most stops are teams people know.</summary>
    public const int MinLastSeason = 1990;

    private Dictionary<int, LeaguePaths> _paths = [];

    public const string GameSlug = "career-path";

    public GameType Type => GameType.CareerPath;
    public string Slug => GameSlug;
    public string Name => "Career Path";
    public int MaxGuesses => 6;
    public bool IsGenerated => true;
    public bool IncludesRetired => true;

    public async Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
    {
        var paths = await PathsAsync(context.League.Id, cancellationToken);
        if (paths.Pool.Count == 0) return null;

        var fresh = paths.Pool.Where(p => !context.RecentAnswerIds.Contains(p.Id)).ToList();
        if (fresh.Count == 0) fresh = paths.Pool;
        var answerId = WeightedPick.From(fresh, p => p.Popularity + 10, context.Random).Id;

        var answer = await db.Players.AsNoTracking()
            .Include(p => p.Stints).ThenInclude(s => s.Team)
            .FirstAsync(p => p.Id == answerId, cancellationToken);

        var stints = answer.Stints.OrderBy(s => s.Order).ToList();
        var stops = stints
            .Select(s => new CareerStop(
                s.Team!.FullName,
                s.Team.Abbreviation,
                s.Team.LogoUrl,
                s.StartSeason,
                s.EndSeason + 1,
                s.Seasons > 0 ? s.Seasons : (s.EndSeason ?? context.AsOf.Year) - s.StartSeason + 1))
            .ToList();

        var path = paths.ByPlayer[answerId];
        var accepted = paths.ByPlayer.Where(p => p.Value.SequenceEqual(path)).Select(p => p.Key).Order().ToList();

        var secret = new CareerSecret(
            answer.Id, answer.FullName, answer.HeadshotUrl, accepted,
            stints.Select(s => s.Team!.FranchiseId ?? s.Team.Id).ToList(),
            HintsFor(answer));
        return new GeneratedPuzzle(answer.Id, GameJson.Write(new CareerPayload(stops)), GameJson.Write(secret), Difficulty: stops.Count);
    }

    public async Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken)
    {
        var secret = GameJson.Read<CareerSecret>(puzzle.SecretJson);

        var played = (await db.PlayerTeamStints.AsNoTracking()
                .Where(s => s.PlayerId == guess.Id)
                .Select(s => s.Team!.FranchiseId ?? s.TeamId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var shared = new JsonArray();
        for (var i = 0; i < secret.Franchises.Count; i++)
        {
            if (played.Contains(secret.Franchises[i])) shared.Add(i);
        }

        return new GuessEvaluation(secret.Accepted.Contains(guess.Id), new JsonObject { ["shared"] = shared });
    }

    /// <summary>One hint opens per wrong guess; the labels of the locked ones are shown so the player knows what is coming.</summary>
    public JsonNode? Clues(Puzzle puzzle, int misses, bool complete)
    {
        var hints = GameJson.Read<CareerSecret>(puzzle.SecretJson).Hints;
        var open = complete ? hints.Count : Math.Min(misses, hints.Count);
        return new JsonObject
        {
            ["hints"] = new JsonArray(hints
                .Select((h, i) => (JsonNode)new JsonObject { ["label"] = h.Label, ["value"] = i < open ? h.Value : null })
                .ToArray()),
        };
    }

    public JsonNode Reveal(Puzzle puzzle)
    {
        var secret = GameJson.Read<CareerSecret>(puzzle.SecretJson);
        return new JsonObject
        {
            ["playerId"] = secret.PlayerId,
            ["name"] = secret.Name,
            ["headshotUrl"] = secret.HeadshotUrl,
            // How many other players share this exact path and would also have counted.
            ["alsoAccepted"] = Math.Max(0, secret.Accepted.Count - 1),
        };
    }

    internal static IReadOnlyList<CareerHint> HintsFor(Player player)
    {
        var draft = player.DraftYear is { } year
            ? player.DraftNumber is { } pick ? $"{year}, pick {pick}" : year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "Undrafted";
        var origin = player.College is not null
            ? new CareerHint("College", player.College)
            : new CareerHint("From", player.Country ?? "No college listed");

        return
        [
            new CareerHint("Position", player.Position ?? "Unknown"),
            new CareerHint("Drafted", draft),
            origin,
            new CareerHint("Initials", Initials(player.FullName)),
        ];
    }

    /// <summary>"Shai Gilgeous-Alexander" → "S. G."</summary>
    internal static string Initials(string fullName) =>
        string.Join(' ', fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => char.IsLetter(part[0]) && !IsSuffix(part))
            .Select(part => $"{char.ToUpperInvariant(part[0])}."));

    private static bool IsSuffix(string part) =>
        part.TrimEnd('.').ToLowerInvariant() is "jr" or "sr" or "ii" or "iii" or "iv";

    /// <summary>Collapses a run of stops with one franchise: the Sonics and the Thunder are one stop for matching.</summary>
    internal static int[] FranchisePath(IEnumerable<int> franchiseIds)
    {
        var path = new List<int>();
        foreach (var id in franchiseIds)
        {
            if (path.Count == 0 || path[^1] != id) path.Add(id);
        }
        return path.ToArray();
    }

    private sealed record LeaguePaths(Dictionary<int, int[]> ByPlayer, List<(int Id, double Popularity)> Pool);

    /// <summary>Every player's franchise path, loaded once per request or import run.</summary>
    private async Task<LeaguePaths> PathsAsync(int leagueId, CancellationToken ct)
    {
        if (_paths.TryGetValue(leagueId, out var cached)) return cached;

        var stints = await db.PlayerTeamStints.AsNoTracking()
            .Where(s => s.Player!.LeagueId == leagueId)
            .Select(s => new { s.PlayerId, s.Order, Franchise = s.Team!.FranchiseId ?? s.TeamId })
            .ToListAsync(ct);

        var byPlayer = stints
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => FranchisePath(g.OrderBy(s => s.Order).Select(s => s.Franchise)));

        var eligible = byPlayer.Where(p => p.Value.Length >= MinFranchises).Select(p => p.Key).ToHashSet();
        var popularity = await db.Players.AsNoTracking()
            .Where(p => p.LeagueId == leagueId && (p.ToYear == null || p.ToYear >= MinLastSeason))
            .Select(p => new { p.Id, p.Popularity })
            .ToListAsync(ct);

        var pool = popularity
            .Where(p => eligible.Contains(p.Id))
            .OrderByDescending(p => p.Popularity).ThenBy(p => p.Id)
            .Take(PoolSize)
            .Select(p => (p.Id, p.Popularity))
            .ToList();

        return _paths[leagueId] = new LeaguePaths(byPlayer, pool);
    }
}

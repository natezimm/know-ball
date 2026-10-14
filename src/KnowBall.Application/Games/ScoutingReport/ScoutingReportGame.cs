using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Domain;

namespace KnowBall.Application.Games.ScoutingReport;

/// <summary>What a player's guess card is compared on, frozen for the answer when the puzzle is made.</summary>
public sealed record ScoutingProfile(
    string? Team,
    string? TeamName,
    string? Conference,
    string? Division,
    string? Position,
    int? HeightInches,
    DateOnly? BirthDate,
    string? Number);

public sealed record ScoutingSecret(int PlayerId, string Name, string? HeadshotUrl, ScoutingProfile Profile, DateOnly AsOf);

public sealed record ScoutingFeedback(Clue Team, Clue Conference, Clue Division, Clue Position, Clue Height, Clue Age, Clue Number);

/// <summary>
/// Guess the current player in eight tries. Every guess is compared with the answer on team,
/// conference, division, position, height, age and jersey number.
/// </summary>
public sealed class ScoutingReportGame(ITriviaDb db) : IGame
{
    /// <summary>Answers are drawn from this many of the best-known current players.</summary>
    public const int PoolSize = 150;

    public const string GameSlug = "scouting-report";

    public GameType Type => GameType.ScoutingReport;
    public string Slug => GameSlug;
    public string Name => "Scouting Report";
    public int MaxGuesses => 8;
    public bool IsGenerated => true;
    public bool IncludesRetired => false;

    public async Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
    {
        // Only players with every clue filled in make a fair answer.
        var pool = await db.Players.AsNoTracking()
            .Where(p => p.LeagueId == context.League.Id && p.IsActive && p.CurrentTeamId != null
                        && p.BirthDate != null && p.HeightInches != null && p.Position != null && p.JerseyNumber != null)
            .OrderByDescending(p => p.Popularity).ThenBy(p => p.Id)
            .Take(PoolSize)
            .Select(p => new { p.Id, p.Popularity })
            .ToListAsync(cancellationToken);
        if (pool.Count == 0) return null;

        var fresh = pool.Where(p => !context.RecentAnswerIds.Contains(p.Id)).ToList();
        if (fresh.Count == 0) fresh = pool; // a small league runs out; repeating beats having no puzzle

        // Better-known players come up a little more often.
        var answerId = WeightedPick.From(fresh, p => p.Popularity + 10, context.Random).Id;
        var answer = await db.Players.AsNoTracking().Include(p => p.CurrentTeam).FirstAsync(p => p.Id == answerId, cancellationToken);

        var secret = new ScoutingSecret(answer.Id, answer.FullName, answer.HeadshotUrl, ProfileOf(answer), context.AsOf);
        return new GeneratedPuzzle(answer.Id, "{}", GameJson.Write(secret));
    }

    public async Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken)
    {
        var secret = GameJson.Read<ScoutingSecret>(puzzle.SecretJson);
        var team = guess.CurrentTeamId is { } teamId
            ? await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken)
            : null;

        var feedback = Compare(ProfileOf(guess, team), secret.Profile, secret.AsOf);
        return new GuessEvaluation(guess.Id == secret.PlayerId, GameJson.Node(feedback));
    }

    public JsonNode? Clues(Puzzle puzzle, int misses, bool complete) => null;

    public JsonNode Reveal(Puzzle puzzle)
    {
        var secret = GameJson.Read<ScoutingSecret>(puzzle.SecretJson);
        var p = secret.Profile;
        return new JsonObject
        {
            ["playerId"] = secret.PlayerId,
            ["name"] = secret.Name,
            ["headshotUrl"] = secret.HeadshotUrl,
            ["team"] = p.Team,
            ["teamName"] = p.TeamName,
            ["position"] = p.Position,
            ["height"] = p.HeightInches is { } h ? AttributeComparer.FormatHeight(h) : null,
            ["age"] = p.BirthDate is { } born ? AttributeComparer.AgeOn(born, secret.AsOf) : null,
            ["number"] = p.Number,
        };
    }

    public static ScoutingFeedback Compare(ScoutingProfile guess, ScoutingProfile answer, DateOnly asOf) => new(
        Team: AttributeComparer.Exact(guess.Team, answer.Team, guess.TeamName),
        Conference: AttributeComparer.Exact(guess.Conference, answer.Conference),
        Division: AttributeComparer.Exact(guess.Division, answer.Division),
        Position: AttributeComparer.Position(guess.Position, answer.Position),
        Height: AttributeComparer.Height(guess.HeightInches, answer.HeightInches),
        Age: AttributeComparer.Age(guess.BirthDate, answer.BirthDate, asOf),
        Number: AttributeComparer.Number(guess.Number, answer.Number));

    private static ScoutingProfile ProfileOf(Player player) => ProfileOf(player, player.CurrentTeam);

    private static ScoutingProfile ProfileOf(Player player, Team? team) => new(
        team?.Abbreviation, team?.Name, team?.Conference, team?.Division,
        player.Position, player.HeightInches, player.BirthDate, player.JerseyNumber);
}

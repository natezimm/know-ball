using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KnowBall.Domain;

namespace KnowBall.Application.Games;

/// <summary>
/// One guess-the-player game. The engine (<see cref="GameService"/>) owns everything the games
/// share: the daily calendar, saved progress, guess limits and keeping the answer back until
/// the game is over. A game supplies its puzzles, its clues and how it judges a guess.
/// </summary>
public interface IGame
{
    GameType Type { get; }
    /// <summary>Name in URLs: "scouting-report".</summary>
    string Slug { get; }
    string Name { get; }
    int MaxGuesses { get; }
    /// <summary>True if the game makes its own puzzles; false if they are made by hand in the admin area.</summary>
    bool IsGenerated { get; }
    /// <summary>Whether the guess box offers retired players as well as current ones.</summary>
    bool IncludesRetired { get; }
    /// <summary>
    /// True when <see cref="MaxGuesses"/> limits misses rather than guesses: right answers are free, and the round
    /// is lost once the misses reach it. For a game with several answers, such as Team Photo.
    /// </summary>
    bool LimitsMisses => false;

    /// <summary>How many right guesses win the round: 1 for a guess-the-player game.</summary>
    int AnswersIn(Puzzle puzzle) => 1;

    /// <summary>
    /// True when the game avoids repeats by reading <see cref="GenerationContext.RecentSecrets"/>. Secrets can be
    /// large (Scouting Report keeps a whole roster), so the scheduler loads them only for a game that asks.
    /// </summary>
    bool ReadsRecentSecrets => false;
    /// <summary>The leagues the game has an edition for, by code, the default first. Each has its own daily puzzle.</summary>
    IReadOnlyList<string> Leagues => [LeagueCodes.Nba];

    /// <summary>A new puzzle, or null when no player qualifies.</summary>
    Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken);

    Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken);

    /// <summary>What the player has unlocked after this many wrong guesses. Null if the game has no step-by-step clues.</summary>
    JsonNode? Clues(Puzzle puzzle, int misses, bool complete);

    /// <summary>The answer, sent only once the game is over.</summary>
    JsonNode Reveal(Puzzle puzzle);
}

/// <param name="AsOf">The day the puzzle will be played; ages are worked out as of this day.</param>
/// <param name="RecentAnswerIds">Players used as answers too recently to use again.</param>
/// <param name="RecentSecrets">
/// The secrets of this game's puzzles in the same window, oldest first, for a game whose answer is not one player
/// and so cannot be kept from repeating by <paramref name="RecentAnswerIds"/>.
/// </param>
/// <param name="Practice">True for a practice round, which a game may draw from a wider pool than its dailies.</param>
public sealed record GenerationContext(
    League League,
    DateOnly AsOf,
    IReadOnlySet<int> RecentAnswerIds,
    Random Random,
    IReadOnlyList<string>? RecentSecrets = null,
    bool Practice = false);

/// <param name="AnswerPlayerId">The one player the puzzle is about; null for a puzzle with several answers.</param>
public sealed record GeneratedPuzzle(int? AnswerPlayerId, string PayloadJson, string SecretJson, int Difficulty = 0);

/// <param name="Miss">Whether the guess counts as a miss; null means it does when it is not correct.</param>
public sealed record GuessEvaluation(bool Correct, JsonNode? Feedback = null, bool? Miss = null);

/// <summary>A guess as it is saved and as it is sent back to the player.</summary>
/// <param name="Miss">Set only when a wrong guess does not count as a miss (false); null means it is one when not correct.</param>
public sealed record GuessRecord(int PlayerId, string Name, string? HeadshotUrl, bool Correct, JsonNode? Feedback, Guid? RequestId = null, bool? Miss = null)
{
    public bool IsMiss => Miss ?? !Correct;
}

public static class GameStatus
{
    public const string New = "new";
    public const string Playing = "playing";
    public const string Won = "won";
    public const string Lost = "lost";
}

/// <summary>Everything the client needs to draw a puzzle. <see cref="Answer"/> is null until the game is over.</summary>
public sealed record GameState(
    string Game,
    string League,
    int PuzzleId,
    int? Number,
    DateOnly? Date,
    bool Practice,
    int MaxGuesses,
    string Status,
    JsonNode Payload,
    JsonNode? Clues,
    IReadOnlyList<GuessRecord> Guesses,
    JsonNode? Answer,
    DateTimeOffset? NextPuzzleAt);

/// <summary>One past puzzle and how this player did on it.</summary>
public sealed record ArchiveEntry(int PuzzleId, int? Number, DateOnly Date, string Status, int Guesses);

public sealed record GameStats(
    string Game,
    string Name,
    int MaxGuesses,
    int Played,
    int Wins,
    int CurrentStreak,
    int BestStreak,
    /// <summary>Wins by number of guesses: index 0 is a first-guess win.</summary>
    IReadOnlyList<int> Distribution,
    string League = LeagueCodes.Nba);

/// <summary>A game's place on the home page today.</summary>
public sealed record TodayEntry(string Game, string Name, bool Available, int? Number, string Status, int Guesses, int MaxGuesses, string League = LeagueCodes.Nba);

/// <summary>
/// The leagues the games come in. An edition only goes live once its league is listed in <c>Games:Leagues</c>,
/// so a new league's puzzles are not queued or numbered before the site can show them.
/// </summary>
public static class LeagueCodes
{
    public const string Nba = "nba";
    public const string Nfl = "nfl";
}

public sealed class GameLeagueOptions
{
    /// <summary>Leagues whose editions are live, comma-separated. The NBA is always live.</summary>
    public string Leagues { get; set; } = LeagueCodes.Nba;

    public bool IsLive(string league) =>
        league == LeagueCodes.Nba
        || Leagues.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(league, StringComparer.OrdinalIgnoreCase);
}

public enum GameFailure
{
    None = 0,
    Invalid,
    NotFound,
    Conflict,
}

public sealed record GameOutcome<T>(T? Result, GameFailure Failure, string? Error)
{
    public bool Ok => Failure == GameFailure.None;

    public static GameOutcome<T> Success(T result) => new(result, GameFailure.None, null);
    public static GameOutcome<T> Invalid(string error) => new(default, GameFailure.Invalid, error);
    public static GameOutcome<T> NotFound(string error) => new(default, GameFailure.NotFound, error);
    public static GameOutcome<T> Conflict(string error) => new(default, GameFailure.Conflict, error);
}

internal static class GameJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonNode Node<T>(T value) => JsonSerializer.SerializeToNode(value, Options)!;

    public static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Stored {typeof(T).Name} is empty.");
}

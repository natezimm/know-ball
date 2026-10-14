namespace SportsTrivia.Application.Games.Spordle;

public sealed record SpordleGameStarted(string GameId, int WordLength, int MaxAttempts);

/// <summary>A guess that is not a real word is reported as <c>Valid = false</c> and costs nothing.</summary>
public sealed record SpordleGuessResult(
    bool Valid,
    IReadOnlyList<LetterStatus>? Score = null,
    bool Won = false,
    bool Complete = false,
    int AttemptsRemaining = 0,
    string? Answer = null);

/// <summary>Either a category, or a 1-based position and the letter there.</summary>
public sealed record SpordleHint(string? Category = null, int? Position = null, char? Letter = null);

public enum SpordleHintType
{
    Letter,
    Category,
}

public enum SpordleFailure
{
    None,
    Invalid,
    NotFound,
    Conflict,
}

public readonly record struct SpordleOutcome<T>(T? Result, SpordleFailure Failure = SpordleFailure.None, string? Error = null)
{
    public bool Ok => Failure == SpordleFailure.None;

    public static SpordleOutcome<T> Fail(SpordleFailure failure, string error) => new(default, failure, error);
}

/// <summary>
/// Runs Spordle games. The answer stays on the server: a browser gets an opaque game id and
/// per-letter scores, and sees the word only once the game is over.
/// Games are held in memory, expire after six idle hours and do not survive a restart.
/// </summary>
public sealed class SpordleGameService
{
    public const int MaxAttempts = 6;
    public const int MaxActiveGames = 10_000;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(6);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly SpordleWordBank _words;
    private readonly TimeProvider _clock;
    private readonly Random _random;
    private readonly int _maxActiveGames;
    private readonly object _gate = new();
    private readonly Dictionary<string, Game> _games = new(StringComparer.Ordinal);
    private long _created;
    private DateTimeOffset _lastSweep;

    public SpordleGameService(SpordleWordBank words, TimeProvider clock)
        : this(words, clock, Random.Shared, MaxActiveGames)
    {
    }

    internal SpordleGameService(SpordleWordBank words, TimeProvider clock, Random random, int maxActiveGames)
    {
        _words = words;
        _clock = clock;
        _random = random;
        _maxActiveGames = maxActiveGames;
        _lastSweep = clock.GetUtcNow();
    }

    public SpordleGameStarted CreateGame(int? requestedLength)
    {
        var wordLength = SpordleWordBank.NormalizeLength(requestedLength);
        var answers = _words.Answers(wordLength);
        var gameId = Guid.NewGuid().ToString();

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            RemoveExpired(now, force: true);
            while (_games.Count >= _maxActiveGames)
                _games.Remove(_games.MinBy(g => g.Value.Sequence).Key);

            _games[gameId] = new Game(answers[_random.Next(answers.Count)], ++_created, now);
        }

        return new SpordleGameStarted(gameId, wordLength, MaxAttempts);
    }

    public SpordleOutcome<SpordleGuessResult> SubmitGuess(string gameId, string? rawGuess)
    {
        lock (_gate)
        {
            if (!TryGetActive(gameId, out var game, out var failure, out var error))
                return SpordleOutcome<SpordleGuessResult>.Fail(failure, error);

            if (string.IsNullOrEmpty(rawGuess) || rawGuess.Length > 10)
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, "Invalid word: must be 1-10 characters");
            if (!rawGuess.All(char.IsAsciiLetter))
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, "Invalid word: alphabetic characters only");
            if (rawGuess.Length != game.Answer.Length)
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, $"Guess must be {game.Answer.Length} letters");

            var guess = rawGuess.ToLowerInvariant();
            if (!_words.IsAllowedGuess(guess))
                return new(new SpordleGuessResult(Valid: false));

            game.Attempts++;
            var score = SpordleScorer.Score(game.Answer, guess);
            for (var i = 0; i < score.Length; i++)
            {
                if (score[i] == LetterStatus.Correct) game.SolvedPositions.Add(i);
            }

            var won = guess == game.Answer;
            game.Complete = won || game.Attempts >= MaxAttempts;

            return new(new SpordleGuessResult(
                Valid: true,
                Score: score,
                Won: won,
                Complete: game.Complete,
                AttemptsRemaining: MaxAttempts - game.Attempts,
                Answer: game.Complete ? game.Answer : null));
        }
    }

    /// <summary>
    /// Hints cost no guesses. The category can be asked for freely; the letter hint is drawn once
    /// per game from the positions not yet solved, and then stays the same.
    /// </summary>
    public SpordleOutcome<SpordleHint> GetHint(string gameId, SpordleHintType type)
    {
        lock (_gate)
        {
            if (!TryGetActive(gameId, out var game, out var failure, out var error))
                return SpordleOutcome<SpordleHint>.Fail(failure, error);

            if (type == SpordleHintType.Category)
                return new(new SpordleHint(Category: _words.CategoryOf(game.Answer)));

            if (game.LetterHint is null)
            {
                var open = Enumerable.Range(0, game.Answer.Length).Where(i => !game.SolvedPositions.Contains(i)).ToList();
                if (open.Count == 0)
                {
                    return SpordleOutcome<SpordleHint>.Fail(
                        SpordleFailure.Conflict,
                        "You've already found every letter's position. Use your green tiles to finish!");
                }

                var index = open[_random.Next(open.Count)];
                game.LetterHint = new SpordleHint(Position: index + 1, Letter: game.Answer[index]);
            }

            return new(game.LetterHint);
        }
    }

    private bool TryGetActive(string gameId, out Game game, out SpordleFailure failure, out string error)
    {
        var now = _clock.GetUtcNow();
        RemoveExpired(now, force: false);

        if (!_games.TryGetValue(gameId, out game!) || IsExpired(game, now))
        {
            _games.Remove(gameId);
            (failure, error) = (SpordleFailure.NotFound, "Game not found or expired");
            return false;
        }

        if (game.Complete)
        {
            (failure, error) = (SpordleFailure.Conflict, "Game is already complete");
            return false;
        }

        game.LastActiveAt = now;
        (failure, error) = (SpordleFailure.None, "");
        return true;
    }

    // A full scan, so outside game creation it runs at most once a minute.
    private void RemoveExpired(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastSweep < SweepInterval) return;
        _lastSweep = now;

        foreach (var id in _games.Where(g => IsExpired(g.Value, now)).Select(g => g.Key).ToList())
            _games.Remove(id);
    }

    private static bool IsExpired(Game game, DateTimeOffset now) => now - game.LastActiveAt >= IdleLifetime;

    private sealed class Game(string answer, long sequence, DateTimeOffset createdAt)
    {
        public string Answer { get; } = answer;
        /// <summary>Creation order; the lowest is evicted first when the store is full.</summary>
        public long Sequence { get; } = sequence;
        public HashSet<int> SolvedPositions { get; } = [];
        public int Attempts { get; set; }
        public bool Complete { get; set; }
        public SpordleHint? LetterHint { get; set; }
        public DateTimeOffset LastActiveAt { get; set; } = createdAt;
    }
}

using System.Text.Json;
using KnowBall.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.Application.Games.Spordle;

public sealed record SpordleGameStarted(string GameId, int WordLength, int MaxAttempts);

public sealed record SpordleSavedGuess(Guid RequestId, string Word, IReadOnlyList<LetterStatus> Score);
public sealed record SpordleState(string GameId, int WordLength, int MaxAttempts,
    IReadOnlyList<SpordleSavedGuess> Guesses, bool Complete, bool Won, string? Answer, SpordleHint? LetterHint);

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
/// Rounds survive process restarts. Revision checks serialize writes across API instances.
/// Only response DTOs leave the server; the stored answer is never returned before completion.
/// </summary>
public sealed class SpordleGameService
{
    public const int MaxAttempts = 6;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(6);
    private const int MaxWriteAttempts = 8;
    private readonly SpordleWordBank _words;
    private readonly TimeProvider _clock;
    private readonly ITriviaDb _db;
    private readonly Random _random;

    public SpordleGameService(SpordleWordBank words, TimeProvider clock, ITriviaDb db)
        : this(words, clock, db, Random.Shared) { }

    internal SpordleGameService(SpordleWordBank words, TimeProvider clock, ITriviaDb db, Random random)
    {
        _words = words;
        _clock = clock;
        _db = db;
        _random = random;
    }

    public async Task<SpordleGameStarted> CreateGameAsync(int? requestedLength, CancellationToken ct = default)
    {
        await PurgeExpiredAsync(ct);
        var length = SpordleWordBank.NormalizeLength(requestedLength);
        var answers = _words.Answers(length);
        var answer = answers[_random.Next(answers.Count)];
        var session = new SpordleSession
        {
            Id = Guid.NewGuid(), Answer = answer, Category = _words.CategoryOf(answer),
            ExpiresAtUtc = _clock.GetUtcNow().UtcDateTime + IdleLifetime,
        };
        _db.SpordleSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return new(session.Id.ToString(), length, MaxAttempts);
    }

    public async Task<SpordleOutcome<SpordleState>> GetGameAsync(string gameId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(gameId, out var id))
            return SpordleOutcome<SpordleState>.Fail(SpordleFailure.NotFound, "Game not found or expired");
        var session = await _db.SpordleSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || session.ExpiresAtUtc <= _clock.GetUtcNow().UtcDateTime)
            return SpordleOutcome<SpordleState>.Fail(SpordleFailure.NotFound, "Game not found or expired");
        var receipts = JsonSerializer.Deserialize<List<GuessReceipt>>(session.GuessesJson)!;
        var guesses = receipts.Select(r => new SpordleSavedGuess(r.RequestId, r.Word, r.Result.Score!)).ToList();
        var hint = session.LetterHintPosition is { } position
            ? new SpordleHint(Position: position, Letter: session.Answer[position - 1]) : null;
        return new(new SpordleState(session.Id.ToString(), session.Answer.Length, MaxAttempts,
            guesses, session.Complete, receipts.LastOrDefault()?.Result.Won == true,
            session.Complete ? session.Answer : null, hint));
    }

    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return _db.SpordleSessions.Where(s => s.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
    }

    public Task<SpordleOutcome<SpordleGuessResult>> SubmitGuessAsync(
        string gameId, string? rawGuess, Guid? requestId = null, CancellationToken ct = default)
    {
        // Older clients can omit an ID. New clients retain one until a guess is acknowledged.
        var id = requestId ?? Guid.NewGuid();
        return UpdateAsync<SpordleGuessResult>(gameId, session =>
        {
            var receipts = JsonSerializer.Deserialize<List<GuessReceipt>>(session.GuessesJson)!;
            var guess = rawGuess?.ToLowerInvariant();
            var previous = receipts.FirstOrDefault(r => r.RequestId == id);
            if (previous is not null)
                return previous.Word == guess
                    ? new(previous.Result)
                    : SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Conflict, "Request ID was already used for a different guess");
            if (session.Complete)
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Conflict, "Game is already complete");
            if (string.IsNullOrEmpty(rawGuess) || rawGuess.Length > 10)
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, "Invalid word: must be 1-10 characters");
            if (!rawGuess.All(char.IsAsciiLetter))
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, "Invalid word: alphabetic characters only");
            if (rawGuess.Length != session.Answer.Length)
                return SpordleOutcome<SpordleGuessResult>.Fail(SpordleFailure.Invalid, $"Guess must be {session.Answer.Length} letters");
            if (!_words.IsAllowedGuess(guess!))
                return new(new SpordleGuessResult(Valid: false));

            session.Attempts++;
            var score = SpordleScorer.Score(session.Answer, guess!);
            for (var i = 0; i < score.Length; i++)
                if (score[i] == LetterStatus.Correct) session.SolvedPositions |= 1 << i;
            var won = guess == session.Answer;
            session.Complete = won || session.Attempts >= MaxAttempts;
            var result = new SpordleGuessResult(true, score, won, session.Complete,
                MaxAttempts - session.Attempts, session.Complete ? session.Answer : null);
            receipts.Add(new(id, guess!, result));
            session.GuessesJson = JsonSerializer.Serialize(receipts);
            return new(result);
        }, ct);
    }

    public Task<SpordleOutcome<SpordleHint>> GetHintAsync(
        string gameId, SpordleHintType type, CancellationToken ct = default) =>
        UpdateAsync<SpordleHint>(gameId, session =>
        {
            if (session.Complete)
                return SpordleOutcome<SpordleHint>.Fail(SpordleFailure.Conflict, "Game is already complete");
            if (type == SpordleHintType.Category)
                return new(new SpordleHint(Category: session.Category));
            if (session.LetterHintPosition is null)
            {
                var open = Enumerable.Range(0, session.Answer.Length)
                    .Where(i => (session.SolvedPositions & (1 << i)) == 0).ToList();
                if (open.Count == 0)
                    return SpordleOutcome<SpordleHint>.Fail(SpordleFailure.Conflict,
                        "You've already found every letter's position. Use your green tiles to finish!");
                session.LetterHintPosition = open[_random.Next(open.Count)] + 1;
            }
            var position = session.LetterHintPosition.Value;
            return new(new SpordleHint(Position: position, Letter: session.Answer[position - 1]));
        }, ct);

    private async Task<SpordleOutcome<T>> UpdateAsync<T>(
        string gameId, Func<SpordleSession, SpordleOutcome<T>> apply, CancellationToken ct)
    {
        if (!Guid.TryParse(gameId, out var id))
            return SpordleOutcome<T>.Fail(SpordleFailure.NotFound, "Game not found or expired");
        for (var attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            // Reload committed state after a conflict: duplicate guesses see their receipt,
            // and competing hints see the letter already chosen by the winning write.
            _db.ClearTracked();
            var session = await _db.SpordleSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (session is null || session.ExpiresAtUtc <= now)
                return SpordleOutcome<T>.Fail(SpordleFailure.NotFound, "Game not found or expired");
            var outcome = apply(session);
            if (!outcome.Ok) return outcome;
            session.ExpiresAtUtc = now + IdleLifetime;
            session.Revision++;
            try
            {
                await _db.SaveChangesAsync(ct);
                return outcome;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxWriteAttempts - 1)
            {
                // The database arbitrates writes, including across different API processes.
            }
            catch (DbUpdateConcurrencyException)
            {
                return SpordleOutcome<T>.Fail(SpordleFailure.Conflict, "The round changed. Please retry your request.");
            }
        }
        throw new InvalidOperationException("Unreachable concurrency retry state.");
    }

    private sealed record GuessReceipt(Guid RequestId, string Word, SpordleGuessResult Result);
}

using System.Text.Json;
using KnowBall.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.Application.Games.LetterJacket;

public sealed record LetterJacketGameStarted(string GameId, int WordLength, int MaxAttempts);

public sealed record LetterJacketSavedGuess(Guid RequestId, string Word, IReadOnlyList<LetterStatus> Score);
public sealed record LetterJacketState(string GameId, int WordLength, int MaxAttempts,
    IReadOnlyList<LetterJacketSavedGuess> Guesses, bool Complete, bool Won, string? Answer, LetterJacketHint? LetterHint);

/// <summary>A guess that is not a real word is reported as <c>Valid = false</c> and costs nothing.</summary>
public sealed record LetterJacketGuessResult(
    bool Valid,
    IReadOnlyList<LetterStatus>? Score = null,
    bool Won = false,
    bool Complete = false,
    int AttemptsRemaining = 0,
    string? Answer = null);

/// <summary>Either a category, or a 1-based position and the letter there.</summary>
public sealed record LetterJacketHint(string? Category = null, int? Position = null, char? Letter = null);

public enum LetterJacketHintType
{
    Letter,
    Category,
}

public enum LetterJacketFailure
{
    None,
    Invalid,
    NotFound,
    Conflict,
}

public readonly record struct LetterJacketOutcome<T>(T? Result, LetterJacketFailure Failure = LetterJacketFailure.None, string? Error = null)
{
    public bool Ok => Failure == LetterJacketFailure.None;

    public static LetterJacketOutcome<T> Fail(LetterJacketFailure failure, string error) => new(default, failure, error);
}

/// <summary>
/// Rounds survive process restarts. Revision checks serialize writes across API instances.
/// Only response DTOs leave the server; the stored answer is never returned before completion.
/// </summary>
public sealed class LetterJacketGameService
{
    public const int MaxAttempts = 6;
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(6);
    private const int MaxWriteAttempts = 8;
    private readonly LetterJacketWordBank _words;
    private readonly TimeProvider _clock;
    private readonly ITriviaDb _db;
    private readonly Random _random;

    public LetterJacketGameService(LetterJacketWordBank words, TimeProvider clock, ITriviaDb db)
        : this(words, clock, db, Random.Shared) { }

    internal LetterJacketGameService(LetterJacketWordBank words, TimeProvider clock, ITriviaDb db, Random random)
    {
        _words = words;
        _clock = clock;
        _db = db;
        _random = random;
    }

    public async Task<LetterJacketGameStarted> CreateGameAsync(int? requestedLength, CancellationToken ct = default)
    {
        await PurgeExpiredAsync(ct);
        var length = LetterJacketWordBank.NormalizeLength(requestedLength);
        var answers = _words.Answers(length);
        var answer = answers[_random.Next(answers.Count)];
        var session = new LetterJacketSession
        {
            Id = Guid.NewGuid(), Answer = answer, Category = _words.CategoryOf(answer),
            ExpiresAtUtc = _clock.GetUtcNow().UtcDateTime + IdleLifetime,
        };
        _db.LetterJacketSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return new(session.Id.ToString(), length, MaxAttempts);
    }

    public async Task<LetterJacketOutcome<LetterJacketState>> GetGameAsync(string gameId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(gameId, out var id))
            return LetterJacketOutcome<LetterJacketState>.Fail(LetterJacketFailure.NotFound, "Game not found or expired");
        var session = await _db.LetterJacketSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || session.ExpiresAtUtc <= _clock.GetUtcNow().UtcDateTime)
            return LetterJacketOutcome<LetterJacketState>.Fail(LetterJacketFailure.NotFound, "Game not found or expired");
        var receipts = JsonSerializer.Deserialize<List<GuessReceipt>>(session.GuessesJson)!;
        var guesses = receipts.Select(r => new LetterJacketSavedGuess(r.RequestId, r.Word, r.Result.Score!)).ToList();
        var hint = session.LetterHintPosition is { } position
            ? new LetterJacketHint(Position: position, Letter: session.Answer[position - 1]) : null;
        return new(new LetterJacketState(session.Id.ToString(), session.Answer.Length, MaxAttempts,
            guesses, session.Complete, receipts.LastOrDefault()?.Result.Won == true,
            session.Complete ? session.Answer : null, hint));
    }

    public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return _db.LetterJacketSessions.Where(s => s.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
    }

    public Task<LetterJacketOutcome<LetterJacketGuessResult>> SubmitGuessAsync(
        string gameId, string? rawGuess, Guid? requestId = null, CancellationToken ct = default)
    {
        // Older clients can omit an ID. New clients retain one until a guess is acknowledged.
        var id = requestId ?? Guid.NewGuid();
        return UpdateAsync<LetterJacketGuessResult>(gameId, session =>
        {
            var receipts = JsonSerializer.Deserialize<List<GuessReceipt>>(session.GuessesJson)!;
            var guess = rawGuess?.ToLowerInvariant();
            var previous = receipts.FirstOrDefault(r => r.RequestId == id);
            if (previous is not null)
                return previous.Word == guess
                    ? new(previous.Result)
                    : LetterJacketOutcome<LetterJacketGuessResult>.Fail(LetterJacketFailure.Conflict, "Request ID was already used for a different guess");
            if (session.Complete)
                return LetterJacketOutcome<LetterJacketGuessResult>.Fail(LetterJacketFailure.Conflict, "Game is already complete");
            if (string.IsNullOrEmpty(rawGuess) || rawGuess.Length > 10)
                return LetterJacketOutcome<LetterJacketGuessResult>.Fail(LetterJacketFailure.Invalid, "Invalid word: must be 1-10 characters");
            if (!rawGuess.All(char.IsAsciiLetter))
                return LetterJacketOutcome<LetterJacketGuessResult>.Fail(LetterJacketFailure.Invalid, "Invalid word: alphabetic characters only");
            if (rawGuess.Length != session.Answer.Length)
                return LetterJacketOutcome<LetterJacketGuessResult>.Fail(LetterJacketFailure.Invalid, $"Guess must be {session.Answer.Length} letters");
            if (!_words.IsAllowedGuess(guess!))
                return new(new LetterJacketGuessResult(Valid: false));

            session.Attempts++;
            var score = LetterJacketScorer.Score(session.Answer, guess!);
            for (var i = 0; i < score.Length; i++)
                if (score[i] == LetterStatus.Correct) session.SolvedPositions |= 1 << i;
            var won = guess == session.Answer;
            session.Complete = won || session.Attempts >= MaxAttempts;
            var result = new LetterJacketGuessResult(true, score, won, session.Complete,
                MaxAttempts - session.Attempts, session.Complete ? session.Answer : null);
            receipts.Add(new(id, guess!, result));
            session.GuessesJson = JsonSerializer.Serialize(receipts);
            return new(result);
        }, ct);
    }

    public Task<LetterJacketOutcome<LetterJacketHint>> GetHintAsync(
        string gameId, LetterJacketHintType type, CancellationToken ct = default) =>
        UpdateAsync<LetterJacketHint>(gameId, session =>
        {
            if (session.Complete)
                return LetterJacketOutcome<LetterJacketHint>.Fail(LetterJacketFailure.Conflict, "Game is already complete");
            if (type == LetterJacketHintType.Category)
                return new(new LetterJacketHint(Category: session.Category));
            if (session.LetterHintPosition is null)
            {
                var open = Enumerable.Range(0, session.Answer.Length)
                    .Where(i => (session.SolvedPositions & (1 << i)) == 0).ToList();
                if (open.Count == 0)
                    return LetterJacketOutcome<LetterJacketHint>.Fail(LetterJacketFailure.Conflict,
                        "You've already found every letter's position. Use your green tiles to finish!");
                session.LetterHintPosition = open[_random.Next(open.Count)] + 1;
            }
            var position = session.LetterHintPosition.Value;
            return new(new LetterJacketHint(Position: position, Letter: session.Answer[position - 1]));
        }, ct);

    private async Task<LetterJacketOutcome<T>> UpdateAsync<T>(
        string gameId, Func<LetterJacketSession, LetterJacketOutcome<T>> apply, CancellationToken ct)
    {
        if (!Guid.TryParse(gameId, out var id))
            return LetterJacketOutcome<T>.Fail(LetterJacketFailure.NotFound, "Game not found or expired");
        for (var attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            // Reload committed state after a conflict: duplicate guesses see their receipt,
            // and competing hints see the letter already chosen by the winning write.
            _db.ClearTracked();
            var session = await _db.LetterJacketSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (session is null || session.ExpiresAtUtc <= now)
                return LetterJacketOutcome<T>.Fail(LetterJacketFailure.NotFound, "Game not found or expired");
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
                return LetterJacketOutcome<T>.Fail(LetterJacketFailure.Conflict, "The round changed. Please retry your request.");
            }
        }
        throw new InvalidOperationException("Unreachable concurrency retry state.");
    }

    private sealed record GuessReceipt(Guid RequestId, string Word, LetterJacketGuessResult Result);
}

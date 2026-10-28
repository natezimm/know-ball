using Microsoft.Extensions.Time.Testing;
using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using KnowBall.Application.Games.Spordle;
using static KnowBall.Application.Games.Spordle.LetterStatus;

namespace KnowBall.UnitTests;

public class SpordleGameServiceTests : IDisposable
{
    // One answer per length, so every game's answer is known: "dunk", "blitz" or "tackle".
    private static readonly SpordleWordBank Bank = new(
        new Dictionary<string, string> { ["Basketball"] = "dunk", ["Football"] = "blitz tackle" },
        ["punts", "teams", "glitz", "about", "other", "which", "their", "there", "would"]);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    private readonly TestDb _database = new();
    private readonly List<TriviaDbContext> _contexts = [];
    private SpordleGameService NewService()
    {
        var db = _database.NewContext();
        _contexts.Add(db);
        return new(Bank, _clock, db, new Random(1));
    }

    public void Dispose()
    {
        foreach (var db in _contexts) db.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Resume_restores_scored_guesses_and_hint_without_revealing_an_unfinished_answer()
    {
        var game = await NewService().CreateGameAsync(5);
        var requestId = Guid.NewGuid();
        var guess = (await NewService().SubmitGuessAsync(game.GameId, "glitz", requestId)).Result!;
        var hint = (await NewService().GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!;
        var state = (await NewService().GetGameAsync(game.GameId)).Result!;
        Assert.Equal(game.GameId, state.GameId);
        Assert.Equal(5, state.WordLength);
        Assert.Equal(6, state.MaxAttempts);
        var saved = Assert.Single(state.Guesses);
        Assert.Equal(requestId, saved.RequestId);
        Assert.Equal("glitz", saved.Word);
        Assert.Equal(guess.Score, saved.Score);
        Assert.Equal(hint, state.LetterHint);
        Assert.Null(state.Answer);
        Assert.False(state.Complete);
        Assert.False(state.Won);
        await NewService().SubmitGuessAsync(game.GameId, "blitz");
        var finished = (await NewService().GetGameAsync(game.GameId)).Result!;
        Assert.True(finished.Won);
        Assert.True(finished.Complete);
        Assert.Equal("blitz", finished.Answer);
        Assert.Equal(2, finished.Guesses.Count);
    }

    [Fact]
    public async Task Resume_rejects_unknown_and_expired_rounds_without_extending_expiry()
    {
        var game = await NewService().CreateGameAsync(5);
        Assert.Equal(SpordleFailure.NotFound, (await NewService().GetGameAsync("nope")).Failure);
        Assert.Equal(SpordleFailure.NotFound, (await NewService().GetGameAsync(Guid.NewGuid().ToString())).Failure);
        _clock.Advance(SpordleGameService.IdleLifetime - TimeSpan.FromSeconds(1));
        Assert.True((await NewService().GetGameAsync(game.GameId)).Ok);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SpordleFailure.NotFound, (await NewService().GetGameAsync(game.GameId)).Failure);
    }

    [Fact]
    public async Task Starting_a_game_returns_an_id_and_limits_but_no_answer()
    {
        var game = (await NewService().CreateGameAsync(4));

        Assert.True(Guid.TryParse(game.GameId, out _));
        Assert.Equal(4, game.WordLength);
        Assert.Equal(6, game.MaxAttempts);
    }

    [Fact]
    public async Task An_unsupported_length_starts_a_five_letter_game()
    {
        Assert.Equal(5, (await NewService().CreateGameAsync(9)).WordLength);
        Assert.Equal(5, (await NewService().CreateGameAsync(null)).WordLength);
    }

    [Fact]
    public async Task A_wrong_guess_is_scored_without_revealing_the_answer()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        var result = (await service.SubmitGuessAsync(game.GameId, "PUNTS")).Result!;

        Assert.True(result.Valid);
        Assert.Equal([Absent, Absent, Absent, Correct, Absent], result.Score);
        Assert.False(result.Won);
        Assert.False(result.Complete);
        Assert.Equal(5, result.AttemptsRemaining);
        Assert.Null(result.Answer);
    }

    [Fact]
    public async Task Winning_completes_the_game_and_reveals_the_answer()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        var result = (await service.SubmitGuessAsync(game.GameId, "blitz")).Result!;

        Assert.True(result.Won);
        Assert.True(result.Complete);
        Assert.Equal("blitz", result.Answer);
        Assert.Equal(SpordleFailure.Conflict, (await service.SubmitGuessAsync(game.GameId, "blitz")).Failure);
    }

    [Fact]
    public async Task The_sixth_miss_ends_the_game_and_reveals_the_answer()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        SpordleGuessResult last = null!;
        foreach (var word in new[] { "about", "other", "which", "their", "there", "would" })
            last = (await service.SubmitGuessAsync(game.GameId, word)).Result!;

        Assert.False(last.Won);
        Assert.True(last.Complete);
        Assert.Equal(0, last.AttemptsRemaining);
        Assert.Equal("blitz", last.Answer);
    }

    [Fact]
    public async Task A_word_outside_the_dictionary_does_not_use_an_attempt()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        var invalid = (await service.SubmitGuessAsync(game.GameId, "zzzzz")).Result!;
        var next = (await service.SubmitGuessAsync(game.GameId, "punts")).Result!;

        Assert.False(invalid.Valid);
        Assert.Null(invalid.Score);
        Assert.Equal(5, next.AttemptsRemaining);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdefghijk")]
    [InlineData("bl1tz")]
    [InlineData("bli z")]
    [InlineData("dunk")]
    public async Task Malformed_and_wrong_length_guesses_are_rejected(string? guess)
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        var outcome = (await service.SubmitGuessAsync(game.GameId, guess));

        Assert.Equal(SpordleFailure.Invalid, outcome.Failure);
        Assert.Equal(6, (await service.SubmitGuessAsync(game.GameId, "blitz")).Result!.AttemptsRemaining + 1);
    }

    [Fact]
    public async Task Unknown_and_expired_games_are_not_found()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);

        Assert.Equal(SpordleFailure.NotFound, (await service.SubmitGuessAsync("nope", "blitz")).Failure);

        _clock.Advance(SpordleGameService.IdleLifetime - TimeSpan.FromMinutes(1));
        Assert.True((await service.SubmitGuessAsync(game.GameId, "punts")).Ok); // activity keeps it alive

        _clock.Advance(SpordleGameService.IdleLifetime);
        Assert.Equal(SpordleFailure.NotFound, (await service.SubmitGuessAsync(game.GameId, "blitz")).Failure);
        Assert.Equal(SpordleFailure.NotFound, (await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Failure);
    }

    [Fact]
    public async Task Expired_rows_are_purged_without_evicting_live_rounds()
    {
        var old = await NewService().CreateGameAsync(5);
        _clock.Advance(SpordleGameService.IdleLifetime);
        var live = await NewService().CreateGameAsync(5);
        using var db = _database.NewContext();
        Assert.False(await db.SpordleSessions.AnyAsync(s => s.Id == Guid.Parse(old.GameId)));
        Assert.True((await NewService().SubmitGuessAsync(live.GameId, "punts")).Ok);
        Assert.Equal(SpordleFailure.NotFound, (await NewService().SubmitGuessAsync(old.GameId, "blitz")).Failure);
    }

    [Fact]
    public async Task The_category_hint_names_the_answers_group_and_costs_nothing()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(6);

        var hint = (await service.GetHintAsync(game.GameId, SpordleHintType.Category)).Result!;

        Assert.Equal("Football", hint.Category);
        Assert.Null(hint.Letter);
        Assert.Equal(5, (await service.SubmitGuessAsync(game.GameId, "tackle")).Result!.AttemptsRemaining);
    }

    [Fact]
    public async Task The_letter_hint_skips_solved_positions_and_then_stays_the_same()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);
        await service.SubmitGuessAsync(game.GameId, "glitz"); // solves every position but the first

        var hint = (await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!;
        var again = (await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!;

        Assert.Equal(1, hint.Position);
        Assert.Equal('b', hint.Letter);
        Assert.Equal(hint, again);
        Assert.Equal(4, (await service.SubmitGuessAsync(game.GameId, "punts")).Result!.AttemptsRemaining);
    }

    [Fact]
    public async Task The_letter_hint_is_drawn_once_even_when_several_positions_are_open()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(6);

        var hint = (await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!;
        var repeats = new List<SpordleHint>();
        for (var i = 0; i < 10; i++)
            repeats.Add((await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!);

        Assert.InRange(hint.Position!.Value, 1, 6);
        Assert.Equal("tackle"[hint.Position.Value - 1], hint.Letter);
        Assert.All(repeats, r => Assert.Equal(hint, r));
    }

    [Fact]
    public async Task Hints_are_refused_once_the_game_is_over()
    {
        var service = NewService();
        var game = await service.CreateGameAsync(5);
        await service.SubmitGuessAsync(game.GameId, "blitz");

        Assert.Equal(SpordleFailure.Conflict, (await service.GetHintAsync(game.GameId, SpordleHintType.Category)).Failure);
        Assert.Equal(SpordleFailure.Conflict, (await service.GetHintAsync(game.GameId, SpordleHintType.Letter)).Failure);
    }
    [Fact]
    public async Task Fresh_services_retain_progress_hints_and_completion()
    {
        var game = await NewService().CreateGameAsync(5);
        await NewService().SubmitGuessAsync(game.GameId, "glitz");
        var hint = (await NewService().GetHintAsync(game.GameId, SpordleHintType.Letter)).Result!;
        Assert.Equal(1, hint.Position);
        Assert.Equal('b', hint.Letter);
        Assert.Equal(hint, (await NewService().GetHintAsync(game.GameId, SpordleHintType.Letter)).Result);
        Assert.Equal("Football", (await NewService().GetHintAsync(game.GameId, SpordleHintType.Category)).Result!.Category);
        var won = (await NewService().SubmitGuessAsync(game.GameId, "blitz")).Result!;
        Assert.True(won.Won);
        Assert.Equal(4, won.AttemptsRemaining);
        Assert.Equal(SpordleFailure.Conflict, (await NewService().SubmitGuessAsync(game.GameId, "about")).Failure);
    }

    [Fact]
    public async Task Request_ids_replay_results_without_spending_another_attempt_even_after_completion()
    {
        var game = await NewService().CreateGameAsync(5);
        var requestId = Guid.NewGuid();
        var first = (await NewService().SubmitGuessAsync(game.GameId, "punts", requestId)).Result!;
        var replay = (await NewService().SubmitGuessAsync(game.GameId, "PUNTS", requestId)).Result!;
        Assert.Equal(first.AttemptsRemaining, replay.AttemptsRemaining);
        Assert.Equal(first.Score, replay.Score);
        Assert.Equal(SpordleFailure.Conflict, (await NewService().SubmitGuessAsync(game.GameId, "about", requestId)).Failure);
        var winningId = Guid.NewGuid();
        Assert.True((await NewService().SubmitGuessAsync(game.GameId, "blitz", winningId)).Result!.Won);
        var wonAgain = (await NewService().SubmitGuessAsync(game.GameId, "blitz", winningId)).Result!;
        Assert.True(wonAgain.Won);
        Assert.Equal(4, wonAgain.AttemptsRemaining);
    }

    [Fact]
    public async Task A_stale_write_reloads_and_scores_against_the_committed_attempt_count()
    {
        var game = await NewService().CreateGameAsync(5);
        var conflict = new CompetingWrite(async () =>
            Assert.True((await NewService().SubmitGuessAsync(game.GameId, "punts")).Ok));
        using var db = _database.NewContext(conflict);
        var service = new SpordleGameService(Bank, _clock, db, new Random(1));
        var result = (await service.SubmitGuessAsync(game.GameId, "about")).Result!;
        Assert.True(conflict.Ran);
        Assert.Equal(4, result.AttemptsRemaining);
        Assert.Equal(3, (await NewService().SubmitGuessAsync(game.GameId, "other")).Result!.AttemptsRemaining);
    }

    // Commit another request after this request reads, but before it saves. This guarantees
    // that the revision check and retry execute without relying on thread timing.
    private sealed class CompetingWrite(Func<Task> write) : SaveChangesInterceptor
    {
        public bool Ran { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!Ran)
            {
                Ran = true;
                await write();
            }
            return result;
        }
    }

}

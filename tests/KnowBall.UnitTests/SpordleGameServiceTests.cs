using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games.Spordle;
using static KnowBall.Application.Games.Spordle.LetterStatus;

namespace KnowBall.UnitTests;

public class SpordleGameServiceTests
{
    // One answer per length, so every game's answer is known: "dunk", "blitz" or "tackle".
    private static readonly SpordleWordBank Bank = new(
        new Dictionary<string, string> { ["Basketball"] = "dunk", ["Football"] = "blitz tackle" },
        ["punts", "teams", "glitz", "about", "other", "which", "their", "there", "would"]);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    private SpordleGameService NewService(int maxActiveGames = SpordleGameService.MaxActiveGames) =>
        new(Bank, _clock, new Random(1), maxActiveGames);

    [Fact]
    public void Starting_a_game_returns_an_id_and_limits_but_no_answer()
    {
        var game = NewService().CreateGame(4);

        Assert.True(Guid.TryParse(game.GameId, out _));
        Assert.Equal(4, game.WordLength);
        Assert.Equal(6, game.MaxAttempts);
    }

    [Fact]
    public void An_unsupported_length_starts_a_five_letter_game()
    {
        Assert.Equal(5, NewService().CreateGame(9).WordLength);
        Assert.Equal(5, NewService().CreateGame(null).WordLength);
    }

    [Fact]
    public void A_wrong_guess_is_scored_without_revealing_the_answer()
    {
        var service = NewService();
        var game = service.CreateGame(5);

        var result = service.SubmitGuess(game.GameId, "PUNTS").Result!;

        Assert.True(result.Valid);
        Assert.Equal([Absent, Absent, Absent, Correct, Absent], result.Score);
        Assert.False(result.Won);
        Assert.False(result.Complete);
        Assert.Equal(5, result.AttemptsRemaining);
        Assert.Null(result.Answer);
    }

    [Fact]
    public void Winning_completes_the_game_and_reveals_the_answer()
    {
        var service = NewService();
        var game = service.CreateGame(5);

        var result = service.SubmitGuess(game.GameId, "blitz").Result!;

        Assert.True(result.Won);
        Assert.True(result.Complete);
        Assert.Equal("blitz", result.Answer);
        Assert.Equal(SpordleFailure.Conflict, service.SubmitGuess(game.GameId, "blitz").Failure);
    }

    [Fact]
    public void The_sixth_miss_ends_the_game_and_reveals_the_answer()
    {
        var service = NewService();
        var game = service.CreateGame(5);

        SpordleGuessResult last = null!;
        foreach (var word in new[] { "about", "other", "which", "their", "there", "would" })
            last = service.SubmitGuess(game.GameId, word).Result!;

        Assert.False(last.Won);
        Assert.True(last.Complete);
        Assert.Equal(0, last.AttemptsRemaining);
        Assert.Equal("blitz", last.Answer);
    }

    [Fact]
    public void A_word_outside_the_dictionary_does_not_use_an_attempt()
    {
        var service = NewService();
        var game = service.CreateGame(5);

        var invalid = service.SubmitGuess(game.GameId, "zzzzz").Result!;
        var next = service.SubmitGuess(game.GameId, "punts").Result!;

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
    public void Malformed_and_wrong_length_guesses_are_rejected(string? guess)
    {
        var service = NewService();
        var game = service.CreateGame(5);

        var outcome = service.SubmitGuess(game.GameId, guess);

        Assert.Equal(SpordleFailure.Invalid, outcome.Failure);
        Assert.Equal(6, service.SubmitGuess(game.GameId, "blitz").Result!.AttemptsRemaining + 1);
    }

    [Fact]
    public void Unknown_and_expired_games_are_not_found()
    {
        var service = NewService();
        var game = service.CreateGame(5);

        Assert.Equal(SpordleFailure.NotFound, service.SubmitGuess("nope", "blitz").Failure);

        _clock.Advance(SpordleGameService.IdleLifetime - TimeSpan.FromMinutes(1));
        Assert.True(service.SubmitGuess(game.GameId, "punts").Ok); // activity keeps it alive

        _clock.Advance(SpordleGameService.IdleLifetime);
        Assert.Equal(SpordleFailure.NotFound, service.SubmitGuess(game.GameId, "blitz").Failure);
        Assert.Equal(SpordleFailure.NotFound, service.GetHint(game.GameId, SpordleHintType.Letter).Failure);
    }

    [Fact]
    public void The_oldest_game_is_evicted_when_the_store_is_full()
    {
        var service = NewService(maxActiveGames: 2);
        var first = service.CreateGame(5);
        var second = service.CreateGame(5);
        var third = service.CreateGame(5);

        Assert.Equal(SpordleFailure.NotFound, service.SubmitGuess(first.GameId, "blitz").Failure);
        Assert.True(service.SubmitGuess(second.GameId, "punts").Ok);
        Assert.True(service.SubmitGuess(third.GameId, "punts").Ok);
    }

    [Fact]
    public void The_category_hint_names_the_answers_group_and_costs_nothing()
    {
        var service = NewService();
        var game = service.CreateGame(6);

        var hint = service.GetHint(game.GameId, SpordleHintType.Category).Result!;

        Assert.Equal("Football", hint.Category);
        Assert.Null(hint.Letter);
        Assert.Equal(5, service.SubmitGuess(game.GameId, "tackle").Result!.AttemptsRemaining);
    }

    [Fact]
    public void The_letter_hint_skips_solved_positions_and_then_stays_the_same()
    {
        var service = NewService();
        var game = service.CreateGame(5);
        service.SubmitGuess(game.GameId, "glitz"); // solves every position but the first

        var hint = service.GetHint(game.GameId, SpordleHintType.Letter).Result!;
        var again = service.GetHint(game.GameId, SpordleHintType.Letter).Result!;

        Assert.Equal(1, hint.Position);
        Assert.Equal('b', hint.Letter);
        Assert.Equal(hint, again);
        Assert.Equal(4, service.SubmitGuess(game.GameId, "punts").Result!.AttemptsRemaining);
    }

    [Fact]
    public void The_letter_hint_is_drawn_once_even_when_several_positions_are_open()
    {
        var service = NewService();
        var game = service.CreateGame(6);

        var hint = service.GetHint(game.GameId, SpordleHintType.Letter).Result!;
        var repeats = Enumerable.Range(0, 10).Select(_ => service.GetHint(game.GameId, SpordleHintType.Letter).Result!);

        Assert.InRange(hint.Position!.Value, 1, 6);
        Assert.Equal("tackle"[hint.Position.Value - 1], hint.Letter);
        Assert.All(repeats, r => Assert.Equal(hint, r));
    }

    [Fact]
    public void Hints_are_refused_once_the_game_is_over()
    {
        var service = NewService();
        var game = service.CreateGame(5);
        service.SubmitGuess(game.GameId, "blitz");

        Assert.Equal(SpordleFailure.Conflict, service.GetHint(game.GameId, SpordleHintType.Category).Failure);
        Assert.Equal(SpordleFailure.Conflict, service.GetHint(game.GameId, SpordleHintType.Letter).Failure);
    }
}

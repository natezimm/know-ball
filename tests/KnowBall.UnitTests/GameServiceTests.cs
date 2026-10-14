using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

public class GameServiceTests : IDisposable
{
    private const string Me = "player-one-1234";
    private const string Scouting = ScoutingReportGame.GameSlug;

    private readonly GameWorld _world = new();

    private async Task<GameState> Daily(string user = Me, string game = Scouting)
    {
        using var engine = _world.NewEngine();
        var outcome = await engine.Service.GetDailyAsync(game, user, default);
        Assert.True(outcome.Ok, outcome.Error);
        return outcome.Result!;
    }

    private async Task<GameOutcome<GameState>> Guess(int puzzleId, string player, string user = Me, string game = Scouting)
    {
        using var engine = _world.NewEngine();
        return await engine.Service.GuessAsync(game, puzzleId, _world.Id(player), user, default);
    }

    private async Task<string> AnswerOf(int puzzleId)
    {
        await using var ctx = _world.Db.NewContext();
        var id = (await ctx.Puzzles.SingleAsync(p => p.Id == puzzleId)).AnswerPlayerId;
        return _world.Players.Values.Single(p => p.Id == id).FullName;
    }

    private string[] WrongGuesses(string answer, int count) =>
        _world.Players.Values.Where(p => p.FullName != answer).Select(p => p.FullName).Take(count).ToArray();

    [Fact]
    public async Task The_daily_puzzle_is_made_on_first_request_and_is_the_same_for_everyone()
    {
        var mine = await Daily();
        var yours = await Daily("someone-else-99");

        Assert.Equal(mine.PuzzleId, yours.PuzzleId);
        Assert.Equal((Scouting, 1, GameWorld.Today, false, 8, GameStatus.Playing), (mine.Game, mine.Number, mine.Date, mine.Practice, mine.MaxGuesses, mine.Status));
        Assert.Empty(mine.Guesses);
        Assert.Null(mine.Answer);
        // Midnight in New York is 04:00 UTC while daylight saving is on.
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 4, 0, 0, TimeSpan.Zero), mine.NextPuzzleAt);
    }

    [Fact]
    public async Task Only_current_players_with_every_clue_filled_in_can_be_the_answer()
    {
        var eligible = new[] { "Jayson Tatum", "Jalen Brunson", "Nikola Jokić", "Jaylen Brown", "Kevin Durant" };
        for (var day = 0; day < 12; day++)
        {
            var state = await Daily();
            Assert.Contains(await AnswerOf(state.PuzzleId), eligible);
            _world.Clock.Advance(TimeSpan.FromDays(1));
        }
    }

    [Fact]
    public async Task A_wrong_guess_is_judged_saved_and_does_not_reveal_the_answer()
    {
        var state = await Daily();
        var answer = await AnswerOf(state.PuzzleId);
        var wrong = WrongGuesses(answer, 1)[0];

        var after = (await Guess(state.PuzzleId, wrong)).Result!;

        var guess = Assert.Single(after.Guesses);
        Assert.Equal((wrong, false), (guess.Name, guess.Correct));
        Assert.Equal(7, guess.Feedback!.AsObject().Count);
        Assert.Equal(GameStatus.Playing, after.Status);
        Assert.Null(after.Answer);
        Assert.DoesNotContain(answer, JsonSerializer.Serialize(after));

        // A reload, or another device with the same id, sees the same game.
        Assert.Equal(JsonSerializer.Serialize(after), JsonSerializer.Serialize(await Daily()));
        Assert.Empty((await Daily("someone-else-99")).Guesses);
    }

    [Fact]
    public async Task The_right_guess_wins_and_reveals_the_answer()
    {
        var state = await Daily();
        var answer = await AnswerOf(state.PuzzleId);
        await Guess(state.PuzzleId, WrongGuesses(answer, 1)[0]);

        var won = (await Guess(state.PuzzleId, answer)).Result!;

        Assert.Equal(GameStatus.Won, won.Status);
        Assert.True(won.Guesses[^1].Correct);
        Assert.All(won.Guesses[^1].Feedback!.AsObject(), clue => Assert.Equal("ok", clue.Value!["state"]!.GetValue<string>()));
        Assert.Equal(answer, won.Answer!["name"]!.GetValue<string>());

        await using var ctx = _world.Db.NewContext();
        var saved = await ctx.UserResults.SingleAsync();
        Assert.Equal((true, 2, 1), (saved.Solved, saved.Score, saved.HintsUsed));
        Assert.NotNull(saved.CompletedAt);
    }

    [Fact]
    public async Task Running_out_of_guesses_loses_and_reveals_the_answer()
    {
        // Career Path allows six guesses; Scouting Report's eight are more than this league has wrong players.
        var state = await Daily(game: "career-path");
        var answer = await AnswerOf(state.PuzzleId);

        await using var ctx = _world.Db.NewContext();
        var secret = JsonDocument.Parse((await ctx.Puzzles.SingleAsync(p => p.Id == state.PuzzleId)).SecretJson).RootElement;
        var accepted = secret.GetProperty("accepted").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();

        var last = state;
        foreach (var wrong in _world.Players.Values.Where(p => !accepted.Contains(p.Id)).Take(6))
        {
            var outcome = await Guess(state.PuzzleId, wrong.FullName, game: "career-path");
            Assert.True(outcome.Ok, outcome.Error);
            Assert.Equal(outcome.Result!.Guesses.Count < 6, outcome.Result.Answer is null);
            last = outcome.Result;
        }

        Assert.Equal(GameStatus.Lost, last.Status);
        Assert.Equal(6, last.Guesses.Count);
        Assert.Equal(answer, last.Answer!["name"]!.GetValue<string>());

        var more = await Guess(state.PuzzleId, answer, game: "career-path");
        Assert.Equal((GameFailure.Conflict, "This puzzle is finished."), (more.Failure, more.Error));
    }

    [Fact]
    public async Task Bad_guesses_are_refused_without_using_a_turn()
    {
        var state = await Daily();
        var answer = await AnswerOf(state.PuzzleId);
        var wrong = WrongGuesses(answer, 1)[0];
        await Guess(state.PuzzleId, wrong);

        var repeat = await Guess(state.PuzzleId, wrong);
        Assert.Equal(GameFailure.Invalid, repeat.Failure);
        Assert.Equal($"You already guessed {wrong}.", repeat.Error);

        using var engine = _world.NewEngine();
        Assert.Equal(GameFailure.Invalid, (await engine.Service.GuessAsync(Scouting, state.PuzzleId, 999_999, Me, default)).Failure);
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GuessAsync(Scouting, 999_999, _world.Id(wrong), Me, default)).Failure);
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GuessAsync("no-such-game", state.PuzzleId, _world.Id(wrong), Me, default)).Failure);
        // A puzzle id from another game is not playable under this one.
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id(wrong), Me, default)).Failure);

        Assert.Single((await Daily()).Guesses);
    }

    [Fact]
    public async Task Tomorrows_puzzle_cannot_be_played_today()
    {
        using var engine = _world.NewEngine();
        await engine.Scheduler.FillQueueAsync(engine.Games.Find(Scouting)!, 3, default);
        var tomorrow = await engine.Db.Puzzles.SingleAsync(p => p.Date == GameWorld.Today.AddDays(1));

        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetPuzzleAsync(Scouting, tomorrow.Id, Me, default)).Failure);
        Assert.Equal(GameFailure.NotFound, (await Guess(tomorrow.Id, "Jayson Tatum")).Failure);

        _world.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await engine.Service.GetPuzzleAsync(Scouting, tomorrow.Id, Me, default)).Ok);
    }

    [Fact]
    public async Task The_archive_lists_past_dailies_with_this_players_results()
    {
        var first = await Daily();
        await Guess(first.PuzzleId, await AnswerOf(first.PuzzleId));
        _world.Clock.Advance(TimeSpan.FromDays(1));
        var second = await Daily();
        await Guess(second.PuzzleId, WrongGuesses(await AnswerOf(second.PuzzleId), 1)[0]);
        _world.Clock.Advance(TimeSpan.FromDays(1));
        await Daily();

        using var engine = _world.NewEngine();
        var archive = (await engine.Service.GetArchiveAsync(Scouting, Me, default)).Result!;

        Assert.Equal([3, 2, 1], archive.Select(a => a.Number));
        Assert.Equal([GameStatus.New, GameStatus.Playing, GameStatus.Won], archive.Select(a => a.Status));
        Assert.Equal([0, 1, 1], archive.Select(a => a.Guesses));
    }

    [Fact]
    public async Task Stats_count_finished_dailies_wins_streaks_and_the_guess_distribution()
    {
        for (var day = 0; day < 3; day++)
        {
            var state = await Daily();
            var answer = await AnswerOf(state.PuzzleId);
            if (day == 1) await Guess(state.PuzzleId, WrongGuesses(answer, 1)[0]);
            await Guess(state.PuzzleId, answer);
            _world.Clock.Advance(TimeSpan.FromDays(1));
        }
        var unfinished = await Daily();
        await Guess(unfinished.PuzzleId, WrongGuesses(await AnswerOf(unfinished.PuzzleId), 1)[0]);

        using var engine = _world.NewEngine();
        var stats = (await engine.Service.GetStatsAsync(Me, default)).Single(s => s.Game == Scouting);

        Assert.Equal((3, 3, 3, 3), (stats.Played, stats.Wins, stats.CurrentStreak, stats.BestStreak));
        Assert.Equal([2, 1, 0, 0, 0, 0, 0, 0], stats.Distribution);
        Assert.Equal(0, (await engine.Service.GetStatsAsync("someone-else-99", default)).Single(s => s.Game == Scouting).Played);
    }

    [Theory]
    [InlineData("2026-10-11,2026-10-12,2026-10-13", 3, 3)] // through today
    [InlineData("2026-10-10,2026-10-11,2026-10-12", 3, 3)] // through yesterday: still alive
    [InlineData("2026-10-08,2026-10-09,2026-10-11", 0, 2)] // broken two days ago
    [InlineData("2026-10-01,2026-10-02,2026-10-03,2026-10-13", 1, 3)]
    [InlineData("", 0, 0)]
    public void Streaks_count_consecutive_winning_days(string wins, int current, int best)
    {
        var dates = wins.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(DateOnly.Parse);
        Assert.Equal((current, best), GameService.Streaks(dates, GameWorld.Today));
    }

    [Fact]
    public async Task Practice_rounds_are_separate_from_the_calendar_and_from_stats()
    {
        var daily = await Daily();
        using var engine = _world.NewEngine();

        var practice = (await engine.Service.StartPracticeAsync(Scouting, default)).Result!;

        Assert.True(practice.Practice);
        Assert.Null(practice.Number);
        Assert.Null(practice.Date);
        Assert.Null(practice.NextPuzzleAt);
        Assert.NotEqual(daily.PuzzleId, practice.PuzzleId);
        Assert.NotEqual(await AnswerOf(daily.PuzzleId), await AnswerOf(practice.PuzzleId)); // never spoils today's answer

        await Guess(practice.PuzzleId, await AnswerOf(practice.PuzzleId));
        Assert.Equal(0, (await engine.Service.GetStatsAsync(Me, default)).Single(s => s.Game == Scouting).Played);
        Assert.Single((await engine.Service.GetArchiveAsync(Scouting, Me, default)).Result!);
    }

    [Fact]
    public async Task Today_shows_each_games_puzzle_and_this_players_progress()
    {
        var state = await Daily();
        await Guess(state.PuzzleId, WrongGuesses(await AnswerOf(state.PuzzleId), 1)[0]);

        using var engine = _world.NewEngine();
        var today = await engine.Service.GetTodayAsync(Me, default);

        Assert.Equal(["scouting-report", "career-path", "whos-that"], today.Select(t => t.Game));
        Assert.Equal((true, 1, GameStatus.Playing, 1, 8), (today[0].Available, today[0].Number, today[0].Status, today[0].Guesses, today[0].MaxGuesses));
        Assert.Equal((true, GameStatus.New), (today[1].Available, today[1].Status));
        Assert.False(today[2].Available); // nobody has made a Who's That puzzle yet
    }

    public void Dispose() => _world.Dispose();
}

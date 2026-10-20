using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

public class PuzzleSchedulerTests : IDisposable
{
    private readonly GameWorld _world = new();

    [Fact]
    public async Task Filling_the_queue_makes_one_numbered_puzzle_a_day_and_skips_days_already_filled()
    {
        using var engine = _world.NewEngine();
        var game = engine.Games.Find(ScoutingReportGame.GameSlug)!;

        Assert.Equal(4, await engine.Scheduler.FillQueueAsync(game, 4, default));
        Assert.Equal(0, await engine.Scheduler.FillQueueAsync(game, 4, default));
        Assert.Equal(2, await engine.Scheduler.FillQueueAsync(game, 6, default));

        var puzzles = await engine.Db.Puzzles.OrderBy(p => p.Date).ToListAsync();
        Assert.Equal(Enumerable.Range(0, 6).Select(i => (DateOnly?)GameWorld.Today.AddDays(i)), puzzles.Select(p => p.Date));
        Assert.Equal([1, 2, 3, 4, 5, 6], puzzles.Select(p => p.Number));
        Assert.All(puzzles, p => Assert.Equal(PuzzleStatus.Scheduled, p.Status));
    }

    [Fact]
    public async Task No_player_is_the_answer_twice_while_others_are_still_unused()
    {
        using var engine = _world.NewEngine();
        await engine.Scheduler.FillQueueAsync(engine.Games.Find(ScoutingReportGame.GameSlug)!, 5, default);

        // Five eligible players, five days: each is used exactly once.
        var answers = await engine.Db.Puzzles.Select(p => p.AnswerPlayerId).ToListAsync();
        Assert.Equal(5, answers.Distinct().Count());
    }

    [Fact]
    public async Task Numbers_keep_counting_days_across_a_gap()
    {
        using var engine = _world.NewEngine();
        var game = engine.Games.Find(ScoutingReportGame.GameSlug)!;
        await engine.Scheduler.GetOrCreateDailyAsync(game, GameWorld.Today, default);

        _world.Clock.Advance(TimeSpan.FromDays(10)); // nobody visited for ten days
        var later = await engine.Scheduler.GetOrCreateDailyAsync(game, engine.Calendar.Today, default);

        Assert.Equal(11, later!.Number);
    }

    [Fact]
    public async Task A_game_with_no_eligible_players_has_no_puzzle()
    {
        await using (var ctx = _world.Db.NewContext())
        {
            await ctx.Players.ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, false));
        }

        using var engine = _world.NewEngine();
        var game = engine.Games.Find(ScoutingReportGame.GameSlug)!;
        Assert.Null(await engine.Scheduler.GetOrCreateDailyAsync(game, GameWorld.Today, default));
        Assert.Equal(0, await engine.Scheduler.FillQueueAsync(game, 5, default));
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetDailyAsync(game.Slug, "player-one-1234", default)).Failure);
    }

    [Fact]
    public async Task Old_practice_rounds_are_purged_and_dailies_are_kept()
    {
        using var engine = _world.NewEngine();
        var game = engine.Games.Find(ScoutingReportGame.GameSlug)!;
        await engine.Scheduler.GetOrCreateDailyAsync(game, GameWorld.Today, default);
        await engine.Scheduler.CreatePracticeAsync(game, default);
        _world.Clock.Advance(TimeSpan.FromDays(8));
        await engine.Scheduler.CreatePracticeAsync(game, default);

        Assert.Equal(1, await engine.Scheduler.PurgePracticeAsync(TimeSpan.FromDays(7), default));
        Assert.Equal([PuzzleStatus.Scheduled, PuzzleStatus.Practice], await engine.Db.Puzzles.OrderBy(p => p.Id).Select(p => p.Status).ToListAsync());
    }

    public void Dispose() => _world.Dispose();
}

public class CareerPathGameTests : IDisposable
{
    private const string Me = "player-one-1234";
    private const string Slug = CareerPathGame.GameSlug;

    private readonly GameWorld _world = new();

    /// <summary>Makes today's puzzle with the given player as the answer.</summary>
    private async Task<GameState> PuzzleFor(string answer)
    {
        for (var seed = 1; seed < 200; seed++)
        {
            using var engine = _world.NewEngine(seed);
            var state = (await engine.Service.GetDailyAsync(Slug, Me, default)).Result!;
            var puzzle = await engine.Db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
            if (puzzle.AnswerPlayerId == _world.Id(answer)) return state;

            engine.Db.Puzzles.Remove(puzzle);
            await engine.Db.SaveChangesAsync();
        }
        throw new InvalidOperationException($"{answer} never came up");
    }

    private async Task<GameState> Guess(int puzzleId, string player)
    {
        using var engine = _world.NewEngine();
        var outcome = await engine.Service.GuessAsync(Slug, puzzleId, _world.Id(player), Me, default);
        Assert.True(outcome.Ok, outcome.Error);
        return outcome.Result!;
    }

    private static string?[] HintValues(GameState state) =>
        state.Clues!["hints"]!.AsArray().Select(h => h!["value"]?.GetValue<string>()).ToArray();

    [Fact]
    public async Task The_puzzle_shows_every_stop_in_order_with_years_and_seasons()
    {
        var state = await PuzzleFor("Kevin Durant");
        var stops = state.Payload["stops"]!.Deserialize<List<CareerStop>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(
        [
            new CareerStop("Seattle SuperSonics", "SEA", null, 2007, 2008, 1),
            new CareerStop("Oklahoma City Thunder", "OKC", "https://logos/OKC.svg", 2008, 2016, 8),
            new CareerStop("Golden State Warriors", "GSW", "https://logos/GSW.svg", 2016, 2019, 3),
            new CareerStop("Miami Heat", "MIA", "https://logos/MIA.svg", 2019, null, 7),
        ], stops);
        Assert.DoesNotContain("Durant", state.Payload.ToJsonString());
        Assert.DoesNotContain("Durant", state.Clues!.ToJsonString());
    }

    [Fact]
    public async Task Only_players_with_three_or_more_franchises_are_answers()
    {
        var answers = new HashSet<int?>();
        for (var seed = 1; seed <= 40; seed++)
        {
            using var engine = _world.NewEngine(seed);
            var practice = (await engine.Service.StartPracticeAsync(Slug, default)).Result!;
            answers.Add((await engine.Db.Puzzles.SingleAsync(p => p.Id == practice.PuzzleId)).AnswerPlayerId);
        }

        Assert.Equal(
            new[] { "Kevin Durant", "Same Road", "Other Road" }.Select(n => (int?)_world.Id(n)).Order(),
            answers.Order());
    }

    [Fact]
    public async Task Each_miss_opens_one_hint_and_says_which_stops_the_guess_shares()
    {
        var state = await PuzzleFor("Kevin Durant");
        Assert.Equal(["Position", "Drafted", "College", "Initials"], state.Clues!["hints"]!.AsArray().Select(h => h!["label"]!.GetValue<string>()));
        Assert.All(HintValues(state), Assert.Null);

        // Jokić has only played for Denver: no stops in common.
        var one = await Guess(state.PuzzleId, "Nikola Jokić");
        Assert.Equal(["F-G", null, null, null], HintValues(one));
        Assert.Empty(one.Guesses[0].Feedback!["shared"]!.AsArray());

        // Other Road also played for Miami, the fourth stop.
        var two = await Guess(state.PuzzleId, "Other Road");
        Assert.Equal(["F-G", "2007, pick 2", null, null], HintValues(two));
        Assert.Equal([3], two.Guesses[1].Feedback!["shared"]!.AsArray().Select(i => i!.GetValue<int>()));

        await Guess(state.PuzzleId, "Jayson Tatum");
        var four = await Guess(state.PuzzleId, "Jaylen Brown");
        Assert.Equal(["F-G", "2007, pick 2", "Texas", "K. D."], HintValues(four));
        Assert.Null(four.Answer);
    }

    [Fact]
    public async Task Anyone_with_the_same_path_of_franchises_counts_as_right()
    {
        var state = await PuzzleFor("Kevin Durant");

        // Same Road never played in Seattle, but the Sonics became the Thunder: the same franchises in the same order.
        var won = await Guess(state.PuzzleId, "Same Road");

        Assert.Equal(GameStatus.Won, won.Status);
        Assert.True(won.Guesses[0].Correct);
        Assert.Equal("Kevin Durant", won.Answer!["name"]!.GetValue<string>());
        Assert.Equal(1, won.Answer["alsoAccepted"]!.GetValue<int>());
        Assert.All(HintValues(won), Assert.NotNull); // a finished game shows every hint
    }

    [Fact]
    public void Hints_fall_back_when_a_fact_is_missing()
    {
        var undrafted = CareerPathGame.HintsFor(new Player { FullName = "Shai Gilgeous-Alexander Jr.", Country = "Canada" });

        Assert.Equal(
            [new CareerHint("Position", "Unknown"), new CareerHint("Drafted", "Undrafted"), new CareerHint("From", "Canada"), new CareerHint("Initials", "S. G.")],
            undrafted);
        Assert.Equal("1999", CareerPathGame.HintsFor(new Player { FullName = "A B", DraftYear = 1999 })[1].Value);
    }

    [Fact]
    public void A_run_of_stops_with_one_franchise_counts_once() =>
        Assert.Equal([4, 7, 4], CareerPathGame.FranchisePath([4, 4, 7, 7, 7, 4]));

    public void Dispose() => _world.Dispose();
}

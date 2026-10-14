using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportsTrivia.Application.Games;
using SportsTrivia.Application.Games.CareerPath;
using SportsTrivia.Application.Games.ScoutingReport;
using SportsTrivia.Application.Games.Silhouette;
using SportsTrivia.Domain;

namespace SportsTrivia.UnitTests;

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

public class CuratedPuzzleServiceTests : IDisposable
{
    private const string Me = "player-one-1234";
    private const string Slug = WhosThatGame.GameSlug;

    private readonly GameWorld _world = new();
    private readonly GameWorld.FakeImages _images = new();
    private readonly GameWorld.FakeRenderer _renderer = new();

    private GameWorld.Engine Engine() => _world.NewEngine(images: _images, renderer: _renderer);

    private async Task<CuratedPuzzle> Create(string player = "Kevin Durant", int year = 2017, string team = "Golden State Warriors", string? caption = "The dagger three")
    {
        using var engine = Engine();
        var outcome = await engine.Curated.CreateAsync(new NewWhosThatPuzzle(_world.Id(player), year, team, caption), Stream.Null, Stream.Null, default);
        Assert.True(outcome.Ok, outcome.Error);
        return outcome.Result!;
    }

    private async Task<CuratedPuzzle> Schedule(int id, DateOnly? date = null)
    {
        using var engine = Engine();
        var outcome = await engine.Curated.ScheduleAsync(id, date, default);
        Assert.True(outcome.Ok, outcome.Error);
        return outcome.Result!;
    }

    [Fact]
    public async Task A_new_puzzle_is_a_draft_with_three_stored_images()
    {
        var puzzle = await Create();

        Assert.Equal(("draft", null, null, "Kevin Durant", 2017, "Golden State Warriors", "The dagger three"),
            (puzzle.State, puzzle.Date, puzzle.Number, puzzle.PlayerName, puzzle.Year, puzzle.Team, puzzle.Caption));
        Assert.Equal(("/media/img1.png", "/media/img2.jpg", "/media/img3.jpg"), (puzzle.SilhouetteUrl, puzzle.BlurredUrl, puzzle.PhotoUrl));

        using var engine = Engine();
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetDailyAsync(Slug, Me, default)).Failure); // a draft is not playable
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetPuzzleAsync(Slug, puzzle.Id, Me, default)).Failure);
    }

    [Fact]
    public async Task Bad_input_is_refused_with_a_message_and_nothing_is_stored()
    {
        using var engine = Engine();
        var id = _world.Id("Kevin Durant");

        Assert.Equal(GameFailure.Invalid, (await engine.Curated.CreateAsync(new NewWhosThatPuzzle(999_999, 2017, "GSW", null), Stream.Null, Stream.Null, default)).Failure);
        Assert.Equal(GameFailure.Invalid, (await engine.Curated.CreateAsync(new NewWhosThatPuzzle(id, 17, "GSW", null), Stream.Null, Stream.Null, default)).Failure);
        Assert.Equal(GameFailure.Invalid, (await engine.Curated.CreateAsync(new NewWhosThatPuzzle(id, 2017, " ", null), Stream.Null, Stream.Null, default)).Failure);

        _renderer.RejectWith = "The cut-out has no clear background.";
        var rejected = await engine.Curated.CreateAsync(new NewWhosThatPuzzle(id, 2017, "GSW", null), Stream.Null, Stream.Null, default);
        Assert.Equal((GameFailure.Invalid, "The cut-out has no clear background."), (rejected.Failure, rejected.Error));

        Assert.Empty(_images.Saved);
        Assert.Empty(await engine.Curated.ListAsync(default));
    }

    [Fact]
    public async Task Scheduling_takes_the_first_free_day_or_a_chosen_one()
    {
        var a = await Schedule((await Create()).Id);
        var b = await Schedule((await Create("Nikola Jokić")).Id);
        var c = await Schedule((await Create("Jayson Tatum")).Id, GameWorld.Today.AddDays(5));

        Assert.Equal((GameWorld.Today, 1, "published"), (a.Date, a.Number, a.State));
        Assert.Equal((GameWorld.Today.AddDays(1), 2, "scheduled"), (b.Date, b.Number, b.State));
        Assert.Equal((GameWorld.Today.AddDays(5), 6), (c.Date, c.Number));

        using var engine = Engine();
        var d = (await Create("Jalen Brunson")).Id;
        Assert.Equal(GameFailure.Conflict, (await engine.Curated.ScheduleAsync(d, GameWorld.Today.AddDays(5), default)).Failure);
        Assert.Equal(GameFailure.Invalid, (await engine.Curated.ScheduleAsync(d, GameWorld.Today.AddDays(-1), default)).Failure);
        Assert.Equal(GameFailure.Conflict, (await engine.Curated.ScheduleAsync(a.Id, null, default)).Failure);
    }

    [Fact]
    public async Task The_ladder_releases_one_hint_per_miss_and_the_photo_only_at_the_end()
    {
        await Schedule((await Create()).Id);
        using var engine = Engine();

        var state = (await engine.Service.GetDailyAsync(Slug, Me, default)).Result!;
        Assert.Equal(5, state.MaxGuesses);
        Assert.Equal("/media/img1.png", state.Clues!["silhouetteUrl"]!.GetValue<string>());
        Assert.Null(state.Clues["year"]);
        Assert.Null(state.Clues["team"]);
        Assert.Null(state.Clues["blurredUrl"]);

        var rungs = new List<string>();
        foreach (var wrong in new[] { "Jayson Tatum", "Jalen Brunson", "Nikola Jokić" })
        {
            state = (await engine.Service.GuessAsync(Slug, state.PuzzleId, _world.Id(wrong), Me, default)).Result!;
            rungs.Add($"{state.Clues!["year"]}|{state.Clues["team"]}|{state.Clues["blurredUrl"]}");
            var sent = JsonSerializer.Serialize(state);
            Assert.DoesNotContain("img3", sent);    // the photo
            Assert.DoesNotContain("Durant", sent);
            Assert.DoesNotContain("dagger", sent);  // the caption
        }
        Assert.Equal(["2017||", "2017|Golden State Warriors|", "2017|Golden State Warriors|/media/img2.jpg"], rungs);

        var won = (await engine.Service.GuessAsync(Slug, state.PuzzleId, _world.Id("Kevin Durant"), Me, default)).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
        Assert.Equal(("Kevin Durant", "/media/img3.jpg", "The dagger three"),
            (won.Answer!["name"]!.GetValue<string>(), won.Answer["photoUrl"]!.GetValue<string>(), won.Answer["caption"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Upcoming_puzzles_can_trade_places_be_taken_back_or_deleted_but_a_live_one_cannot()
    {
        var live = await Schedule((await Create()).Id);
        var first = await Schedule((await Create("Nikola Jokić")).Id);
        var second = await Schedule((await Create("Jayson Tatum")).Id);
        using var engine = Engine();

        // Moving the later puzzle earlier swaps what the two days show.
        var moved = (await engine.Curated.MoveAsync(second.Id, earlier: true, default)).Result!;
        Assert.Equal((first.Date, "Jayson Tatum"), (moved.Date, moved.PlayerName));
        var list = await engine.Curated.ListAsync(default);
        Assert.Equal(["Jayson Tatum", "Nikola Jokić", "Kevin Durant"], list.Select(p => p.PlayerName)); // upcoming in order, then history
        Assert.Equal(GameFailure.Conflict, (await engine.Curated.MoveAsync(moved.Id, earlier: true, default)).Failure); // already first

        var back = (await engine.Curated.UnscheduleAsync(moved.Id, default)).Result!;
        Assert.Equal(("draft", null), (back.State, back.Date));

        Assert.True((await engine.Curated.DeleteAsync(back.Id, default)).Ok);
        Assert.Equal(3, _images.Deleted.Count);

        Assert.Equal(GameFailure.Conflict, (await engine.Curated.UnscheduleAsync(live.Id, default)).Failure);
        Assert.Equal(GameFailure.Conflict, (await engine.Curated.MoveAsync(live.Id, earlier: false, default)).Failure);
        Assert.Equal(GameFailure.Conflict, (await engine.Curated.DeleteAsync(live.Id, default)).Failure);
        Assert.Equal(GameFailure.NotFound, (await engine.Curated.DeleteAsync(999_999, default)).Failure);
    }

    [Fact]
    public async Task When_the_queue_runs_dry_the_puzzle_shown_longest_ago_comes_round_again()
    {
        await Schedule((await Create("Kevin Durant")).Id);
        await Schedule((await Create("Nikola Jokić")).Id);

        var shown = new List<(int? Number, string Answer)>();
        for (var day = 0; day < 5; day++)
        {
            using var engine = Engine();
            var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(engine.Games.Find(Slug)!, engine.Calendar.Today, default);
            shown.Add((puzzle!.Number, _world.Players.Values.Single(p => p.Id == puzzle.AnswerPlayerId).FullName));
            _world.Clock.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(
            [(1, "Kevin Durant"), (2, "Nikola Jokić"), (3, "Kevin Durant"), (4, "Nikola Jokić"), (5, "Kevin Durant")],
            shown);

        // A replay is a new puzzle, so someone who solved the original plays it afresh.
        await using var ctx = _world.Db.NewContext();
        Assert.Equal(5, await ctx.Puzzles.CountAsync());
    }

    public void Dispose() => _world.Dispose();
}

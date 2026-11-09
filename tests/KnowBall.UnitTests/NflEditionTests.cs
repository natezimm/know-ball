using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The NFL editions of Career Path and Scouting Report, beside the NBA ones in the same small world.</summary>
public class NflEditionTests : IDisposable
{
    private readonly GameWorld _world = new();
    private const string User = "user-nfl-0001";

    private GameWorld.Engine Engine(int seed = 1) => _world.NewEngine(seed, leagues: "nba,nfl");


    [Fact]
    public async Task Each_league_has_its_own_daily_puzzle_numbered_from_one()
    {
        using var engine = Engine();
        var nba = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None)).Result!;
        var nfl = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "NFL")).Result!;

        Assert.Equal(("nba", "nfl"), (nba.League, nfl.League));
        Assert.NotEqual(nba.PuzzleId, nfl.PuzzleId);
        Assert.Equal((1, 1), (nba.Number, nfl.Number));

        var again = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl")).Result!;
        Assert.Equal(nfl.PuzzleId, again.PuzzleId);
    }

    [Fact]
    public async Task An_edition_that_is_not_live_or_does_not_exist_is_not_found()
    {
        using (var dark = _world.NewEngine())
        {
            var outcome = await dark.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl");
            Assert.Equal(GameFailure.NotFound, outcome.Failure);
            Assert.Equal("Career Path has no NFL edition.", outcome.Error);
            Assert.Equal(GameFailure.NotFound, (await dark.Service.StartPracticeAsync("scouting-report", CancellationToken.None, "nfl")).Failure);
        }

        using var engine = Engine();
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "mlb")).Failure);
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetArchiveAsync("career-path", User, CancellationToken.None, "nhl")).Failure);
    }

    [Fact]
    public async Task Nfl_career_path_shows_seasons_by_their_own_year_and_only_uses_careers_the_record_covers()
    {
        using var engine = Engine();
        var state = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl")).Result!;

        // Old Timer began before 1990 and Boot Leg is a kicker, so Road Warrior is the only answer.
        var stops = state.Payload["stops"]!.AsArray().Select(s => $"{s!["abbreviation"]} {s["from"]}-{s["to"]}").ToList();
        Assert.Equal(["OAK 2005-2008", "NE 2009-2011", "PHI 2012-2014"], stops);

        var won = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Road Warrior"), User, CancellationToken.None)).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
        Assert.Equal("Road Warrior", won.Answer!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Nba_career_path_still_shows_the_year_each_season_ended()
    {
        using var engine = Engine();
        var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(engine.Games.Find("career-path")!, GameWorld.Today, CancellationToken.None);
        var stops = JsonDocument.Parse(puzzle!.PayloadJson).RootElement.GetProperty("stops");
        var first = stops[0];
        // Seattle 2007 is the 2007-08 season.
        if (first.GetProperty("abbreviation").GetString() == "SEA") Assert.Equal(2008, first.GetProperty("to").GetInt32());
        Assert.True(stops.GetArrayLength() > 0, puzzle.PayloadJson);
        Assert.All(stops.EnumerateArray().Where(s => s.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Number),
            s => Assert.True(s.GetProperty("to").GetInt32() > s.GetProperty("from").GetInt32()));
    }

    [Fact]
    public async Task A_guess_from_the_other_league_is_refused()
    {
        using var engine = Engine();
        var state = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl")).Result!;
        var outcome = await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Kevin Durant"), User, CancellationToken.None);
        Assert.Equal(GameFailure.Invalid, outcome.Failure);
    }

    [Fact]
    public async Task Nfl_scouting_report_never_picks_a_kicker()
    {
        var kicker = _world.Id("Kenny Kicker");
        for (var seed = 1; seed <= 25; seed++)
        {
            using var engine = Engine(seed);
            var state = (await engine.Service.StartPracticeAsync("scouting-report", CancellationToken.None, "nfl")).Result!;
            var answer = await engine.Db.Puzzles.Where(p => p.Id == state.PuzzleId).Select(p => p.AnswerPlayerId).SingleAsync();
            Assert.NotEqual(kicker, answer);
            Assert.Equal("nfl", state.League);
        }
    }

    [Fact]
    public async Task Nfl_scouting_report_scores_positions_by_unit_and_divisions_with_their_conference()
    {
        using var engine = Engine();
        var game = engine.Games.Find("scouting-report")!;
        var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(game, GameWorld.Today, CancellationToken.None, "nfl");
        var secret = JsonSerializer.Deserialize<ScoutingSecret>(puzzle!.SecretJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("nfl", secret.League);

        var corner = new ScoutingProfile("PHI", "Eagles", null, "NFC", "NFC East", "CB", 72, new DateOnly(1998, 1, 1), "21");
        var safety = new ScoutingProfile("GB", "Packers", null, "NFC", "NFC North", "S", 73, new DateOnly(1997, 2, 2), "24");
        var raider = new ScoutingProfile("LV", "Raiders", null, "AFC", "AFC West", "QB", 76, new DateOnly(1996, 2, 2), "4");
        var ram = new ScoutingProfile("LAR", "Rams", null, "NFC", "NFC West", "QB", 76, new DateOnly(1996, 2, 2), "4");

        var feedback = ScoutingReportGame.Compare(corner, safety, GameWorld.Today, league: "nfl");
        Assert.Equal(AttributeComparer.Close, feedback.Position.State);
        Assert.Equal(AttributeComparer.Ok, feedback.Conference.State);
        Assert.Equal(AttributeComparer.Miss, feedback.Division.State);

        var west = ScoutingReportGame.Compare(raider, ram, GameWorld.Today, league: "nfl");
        Assert.Equal(AttributeComparer.Miss, west.Division.State); // AFC West is not NFC West
        Assert.Equal(AttributeComparer.Ok, west.Position.State);

        Assert.Equal(AttributeComparer.Miss, AttributeComparer.FootballPosition("CB", "C").State);
    }

    [Theory]
    [InlineData("OLB", "MLB", "close")]
    [InlineData("DE", "NT", "close")]
    [InlineData("OT", "G", "close")]
    [InlineData("RB", "FB", "close")]
    [InlineData("FS", "CB", "close")]
    [InlineData("WR", "TE", "miss")]
    [InlineData("QB", "QB", "ok")]
    [InlineData("LB", "DE", "miss")]
    [InlineData("XX", "XX", "ok")]
    [InlineData("XX", "YY", "miss")]
    public void Football_positions_are_close_within_a_unit(string guess, string answer, string expected) =>
        Assert.Equal(expected, AttributeComparer.FootballPosition(guess, answer).State);

    [Fact]
    public async Task Stats_today_and_archive_keep_the_leagues_apart()
    {
        using var engine = Engine();
        var nfl = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl")).Result!;
        await engine.Service.GuessAsync("career-path", nfl.PuzzleId, _world.Id("Road Warrior"), User, CancellationToken.None);

        var today = await engine.Service.GetTodayAsync(User, CancellationToken.None);
        Assert.Equal(
            ["scouting-report nba", "scouting-report nfl", "career-path nba", "career-path nfl", "back-of-the-card nba", "back-of-the-card nfl"],
            today.Select(t => $"{t.Game} {t.League}").ToList());
        Assert.Equal(GameStatus.Won, today.Single(t => t is { Game: "career-path", League: "nfl" }).Status);
        Assert.Equal(GameStatus.New, today.Single(t => t is { Game: "career-path", League: "nba" }).Status);

        var stats = await engine.Service.GetStatsAsync(User, CancellationToken.None);
        Assert.Equal((1, 1), stats.Single(s => s is { Game: "career-path", League: "nfl" }) is var s1 ? (s1.Wins, s1.CurrentStreak) : default);
        Assert.Equal(0, stats.Single(s => s is { Game: "career-path", League: "nba" }).Played);

        var archive = (await engine.Service.GetArchiveAsync("career-path", User, CancellationToken.None, "nfl")).Result!;
        Assert.Equal(nfl.PuzzleId, Assert.Single(archive).PuzzleId);
        var nbaArchive = (await engine.Service.GetArchiveAsync("career-path", User, CancellationToken.None)).Result!;
        Assert.DoesNotContain(nbaArchive, a => a.PuzzleId == nfl.PuzzleId);
    }

    [Fact]
    public async Task Queues_fill_for_each_live_edition()
    {
        using var engine = Engine();
        var game = engine.Games.Find("scouting-report")!;
        Assert.Equal(["nba", "nfl"], engine.Games.LeaguesOf(game));
        Assert.Equal(["nba", "nfl"], engine.Games.LeaguesOf(engine.Games.Find("back-of-the-card")!));

        Assert.Equal(5, await engine.Scheduler.FillQueueAsync(game, 5, CancellationToken.None, "nfl"));
        var nflId = _world.Nfl.Id;
        Assert.Equal(5, await engine.Db.Puzzles.CountAsync(p => p.GameType == GameType.ScoutingReport && p.LeagueId == nflId));
    }

    /// <summary>Today's NFL Back of the Card with the given answer, and the answers it can be drawn from.</summary>
    private async Task<(GameState State, HashSet<int?> Drawn)> NflCardFor(string answer)
    {
        var drawn = new HashSet<int?>();
        for (var seed = 1; seed < 100; seed++)
        {
            using var engine = Engine(seed);
            var state = (await engine.Service.GetDailyAsync("back-of-the-card", User, CancellationToken.None, "nfl")).Result!;
            var puzzle = await engine.Db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
            drawn.Add(puzzle.AnswerPlayerId);
            if (puzzle.AnswerPlayerId == _world.Id(answer)) return (state, drawn);
            engine.Db.Puzzles.Remove(puzzle);
            await engine.Db.SaveChangesAsync();
        }
        throw new InvalidOperationException($"{answer} never came up");
    }

    private async Task<GameState> MissAll(GameState state, params string[] misses)
    {
        using var engine = Engine();
        foreach (var miss in misses)
            state = (await engine.Service.GuessAsync("back-of-the-card", state.PuzzleId, _world.Id(miss), User, CancellationToken.None)).Result!;
        return state;
    }

    private static FootballCardPayload Card(GameState state) =>
        state.Payload.Deserialize<FootballCardPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public async Task Nfl_back_of_the_card_prints_a_quarterbacks_season_totals_and_his_stamps()
    {
        var (state, _) = await NflCardFor("Pat Thrower");
        var payload = Card(state);

        Assert.Equal(["Cmp%", "Yds", "TD", "Int"], payload.Columns.Select(c => c.Label));
        Assert.Equal([2017, 2018, 2019, 2020, 2021], payload.Seasons.Select(s => s.Season));
        Assert.Equal(new double[] { 66.0, 5097, 50, 12 }, payload.Seasons[1].Values);
        Assert.Equal(new double[] { 0, 0, 0, 0 }, payload.Seasons[4].Values);
        // Career completion rate from the totals: 1,114 of 1,687.
        Assert.Equal((5, 63), (payload.Career.Seasons, payload.Career.Games));
        Assert.Equal(new double[] { 66.0, 14152, 114, 24 }, payload.Career.Values);
        Assert.DoesNotContain("KC", state.Payload.ToJsonString() + state.Clues!.ToJsonString());
        Assert.DoesNotContain("MVP", state.Payload.ToJsonString() + state.Clues!.ToJsonString());

        var open = await MissAll(state, "Cee Back", "Kenny Kicker", "Old Timer", "Boot Leg");
        Assert.Equal(["KC", "KC", "KC", "KC", "KC"], open.Clues!["teams"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(new[] { null, "MVP", "SB MVP", "Pro Bowl", null }, open.Clues!["stamps"]!.AsArray().Select(t => t?.GetValue<string>()));
        Assert.Equal("QB", open.Clues!["position"]!.GetValue<string>());
    }

    [Fact]
    public async Task Nfl_back_of_the_card_only_draws_long_careers_the_stats_cover()
    {
        var (state, drawn) = await NflCardFor("Road Warrior");
        var payload = Card(state);

        Assert.Equal(["Rec", "Yds", "TD", "Avg"], payload.Columns.Select(c => c.Label));
        Assert.Equal(new double[] { 290, 3900, 26, 13.4 }, payload.Career.Values);
        // Cee Back has four seasons, Kenny Kicker kicks and Old Timer's career began before the stats do.
        Assert.Subset(new HashSet<int?> { _world.Id("Pat Thrower"), _world.Id("Road Warrior") }, drawn);

        var open = await MissAll(state, "Cee Back");
        Assert.Equal(["OAK", "OAK", "OAK", "OAK", "NE"], open.Clues!["teams"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public void A_football_season_gets_its_best_award_as_its_stamp()
    {
        Assert.Equal("MVP", BackOfTheCardGame.FootballStamp(["ProBowl", "AllPro1", "MVP", "OPOY"]));
        Assert.Equal("DPOY", BackOfTheCardGame.FootballStamp(["ProBowl", "DPOY", "SBMVP"]));
        Assert.Equal("OROY", BackOfTheCardGame.FootballStamp(["OROY", "ProBowl"]));
        Assert.Equal("All-Pro 2nd", BackOfTheCardGame.FootballStamp(["AllPro2", "ProBowl"]));
        Assert.Equal("Pro Bowl", BackOfTheCardGame.FootballStamp(["ProBowl"]));
        Assert.Null(BackOfTheCardGame.FootballStamp(["AllStar"]));
    }

    [Fact]
    public void A_defenders_card_counts_half_sacks_and_a_backs_counts_his_catches()
    {
        PlayerSeasonStat Row(int season, string json) => new() { Season = season, IsSeasonTotal = true, Games = 16, StatsJson = json };
        var defender = BackOfTheCardGame.FootballCard("OLB", [BackOfTheCardGame.FootballLine.Read(Row(2020, """{"tkl":60,"sk":10.5,"dint":1,"ff":3}""")),
            BackOfTheCardGame.FootballLine.Read(Row(2021, """{"tkl":40,"sk":4}"""))]);
        Assert.Equal(["Tkl", "Sacks", "Int", "FF"], defender.Columns.Select(c => c.Label));
        Assert.Equal(new double[] { 100, 14.5, 1, 3 }, defender.Career.Values);

        var back = BackOfTheCardGame.FootballCard("RB", [BackOfTheCardGame.FootballLine.Read(Row(2020, """{"car":250,"ryd":1100,"rtd":9,"rec":40}"""))]);
        Assert.Equal(["Car", "Yds", "TD", "Rec"], back.Columns.Select(c => c.Label));
        Assert.Equal(new double[] { 250, 1100, 9, 40 }, back.Seasons[0].Values);
    }

    public void Dispose() => _world.Dispose();
}

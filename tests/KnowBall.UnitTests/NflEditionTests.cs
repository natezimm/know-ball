using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
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
        Assert.Equal(GameFailure.NotFound, (await engine.Service.GetDailyAsync("back-of-the-card", User, CancellationToken.None, "nfl")).Failure);
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
            ["scouting-report nba", "scouting-report nfl", "career-path nba", "career-path nfl", "back-of-the-card nba"],
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
        Assert.Equal(["nba"], engine.Games.LeaguesOf(engine.Games.Find("back-of-the-card")!));

        Assert.Equal(5, await engine.Scheduler.FillQueueAsync(game, 5, CancellationToken.None, "nfl"));
        var nflId = _world.Nfl.Id;
        Assert.Equal(5, await engine.Db.Puzzles.CountAsync(p => p.GameType == GameType.ScoutingReport && p.LeagueId == nflId));
    }

    public void Dispose() => _world.Dispose();
}

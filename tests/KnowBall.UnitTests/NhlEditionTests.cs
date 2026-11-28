using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The NHL editions of Career Path, Scouting Report and Back of the Card, beside the NBA and NFL ones in the same small world.</summary>
public class NhlEditionTests : IDisposable
{
    private readonly GameWorld _world = new();
    private const string User = "user-nhl-0001";

    private GameWorld.Engine Engine(int seed = 1) => _world.NewEngine(seed, leagues: "nba,nfl,nhl");

    [Fact]
    public async Task The_nhl_edition_has_its_own_daily_puzzle_and_stays_dark_until_it_is_live()
    {
        using (var dark = _world.NewEngine(leagues: "nba,nfl"))
        {
            var outcome = await dark.Service.GetDailyAsync("scouting-report", User, CancellationToken.None, "nhl");
            Assert.Equal(GameFailure.NotFound, outcome.Failure);
            Assert.Equal("Scouting Report has no NHL edition.", outcome.Error);
        }

        using var engine = Engine();
        var nfl = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nfl")).Result!;
        var nhl = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "NHL")).Result!;
        Assert.Equal(("nfl", "nhl"), (nfl.League, nhl.League));
        Assert.NotEqual(nfl.PuzzleId, nhl.PuzzleId);
        Assert.Equal(1, nhl.Number);
    }

    [Fact]
    public async Task Nhl_career_path_shows_seasons_by_the_year_they_ended_and_only_uses_careers_the_record_covers()
    {
        using var engine = Engine();
        var state = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "nhl")).Result!;

        // Pre Merger began before 1979, so Road Tripper is the only answer. Quebec 1990 is the 1990-91 season.
        var stops = state.Payload["stops"]!.AsArray().Select(s => $"{s!["abbreviation"]} {s["from"]}-{s["to"]}").ToList();
        Assert.Equal(["QUE 1990-1995", "COL 1995-1998", "TOR 1998-2002", "BOS 2002-2005", "EDM 2005-2007"], stops);

        // The Nordiques and the Avalanche are one franchise: a guess who only played in Colorado shares both stops.
        var miss = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Net Minder"), User, CancellationToken.None)).Result!;
        Assert.Equal([0, 1], miss.Guesses[^1].Feedback!["shared"]!.AsArray().Select(i => i!.GetValue<int>()));
        // Most NHL players never went to college, so the NHL's third hint is where he is from.
        Assert.Equal(["Position", "Drafted", "From", "Initials"], miss.Clues!["hints"]!.AsArray().Select(h => h!["label"]!.GetValue<string>()));
        Assert.Equal("RW", miss.Clues!["hints"]![0]!["value"]!.GetValue<string>());

        var won = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Road Tripper"), User, CancellationToken.None)).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
        Assert.Equal("Sweden", won.Clues!["hints"]![2]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void An_nhl_players_hint_falls_back_to_his_college_when_his_country_is_not_known()
    {
        var hints = CareerPathGame.HintsFor(new Player { FullName = "A B", College = "Boston University" }, "nhl");
        Assert.Equal(("College", "Boston University"), (hints[2].Label, hints[2].Value));
        Assert.Equal("College", CareerPathGame.HintsFor(new Player { FullName = "A B", College = "Duke", Country = "USA" })[2].Label);
    }

    [Fact]
    public async Task Nhl_scouting_report_picks_a_current_nhl_player_and_scores_forwards_as_close()
    {
        using var engine = Engine();
        var game = engine.Games.Find("scouting-report")!;
        var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(game, GameWorld.Today, CancellationToken.None, "nhl");
        var secret = JsonSerializer.Deserialize<ScoutingSecret>(puzzle!.SecretJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("nhl", secret.League);
        Assert.Contains(secret.Name, new[] { "Center Ice", "Left Winger", "Blue Liner", "Net Minder" });

        var winger = new ScoutingProfile("BOS", "Bruins", null, "Eastern", "Atlantic", "LW", 72, new DateOnly(1996, 5, 21), "63");
        var center = new ScoutingProfile("TOR", "Maple Leafs", null, "Eastern", "Atlantic", "C", 73, new DateOnly(1997, 9, 17), "34");
        var feedback = ScoutingReportGame.Compare(winger, center, GameWorld.Today, league: "nhl");
        Assert.Equal(AttributeComparer.Close, feedback.Position.State);
        Assert.Equal(AttributeComparer.Ok, feedback.Conference.State);
        Assert.Equal(AttributeComparer.Ok, feedback.Division.State);
    }

    [Theory]
    [InlineData("LW", "RW", "close")]
    [InlineData("C", "LW", "close")]
    [InlineData("C", "C", "ok")]
    [InlineData("D", "C", "miss")]
    [InlineData("G", "D", "miss")]
    [InlineData("D", "D", "ok")]
    public void Hockey_forwards_are_close_to_each_other(string guess, string answer, string expected) =>
        Assert.Equal(expected, AttributeComparer.HockeyPosition(guess, answer).State);

    /// <summary>Today's NHL Back of the Card with the given answer, and the answers it can be drawn from.</summary>
    private async Task<(GameState State, HashSet<int?> Drawn)> NhlCardFor(string answer)
    {
        var drawn = new HashSet<int?>();
        for (var seed = 1; seed < 100; seed++)
        {
            using var engine = Engine(seed);
            var state = (await engine.Service.GetDailyAsync("back-of-the-card", User, CancellationToken.None, "nhl")).Result!;
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
    public async Task Nhl_back_of_the_card_prints_a_skaters_totals_by_two_year_season_with_his_trophies()
    {
        var (state, drawn) = await NhlCardFor("Road Tripper");
        var payload = Card(state);

        Assert.True(payload.SplitYears);
        Assert.Equal(["G", "A", "Pts", "+/-"], payload.Columns.Select(c => c.Label));
        Assert.Equal([1990, 1991, 1992, 1993, 1995], payload.Seasons.Select(s => s.Season));
        Assert.Equal(new double[] { 35, 45, 80, -3 }, payload.Seasons[1].Values);
        Assert.Equal((5, 391), (payload.Career.Seasons, payload.Career.Games));
        Assert.Equal(new double[] { 158, 200, 358, 34 }, payload.Career.Values);
        // Pre Merger's card would start part way through his career.
        Assert.DoesNotContain(_world.Id("Pre Merger"), drawn);

        var open = await MissAll(state, "Center Ice", "Left Winger", "Blue Liner", "Pre Merger");
        Assert.Equal(["QUE", "QUE", "QUE", "QUE", "COL"], open.Clues!["teams"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(new[] { null, null, "Art Ross", null, "Stanley Cup" }, open.Clues!["stamps"]!.AsArray().Select(t => t?.GetValue<string>()));
        Assert.Equal(("From", "Sweden"), (open.Clues!["origin"]!["label"]!.GetValue<string>(), open.Clues!["origin"]!["value"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Nhl_back_of_the_card_prints_a_goalies_wins_average_and_save_percentage()
    {
        var (state, _) = await NhlCardFor("Net Minder");
        var payload = Card(state);

        Assert.Equal([("W", 0), ("GAA", 2), ("Sv%", 3), ("SO", 0)], payload.Columns.Select(c => (c.Label, c.Decimals)));
        Assert.Equal(new double[] { 35, 2.4, 0.92, 5 }, payload.Seasons[0].Values);
        // The career average and save percentage are weighted by starts: 240 of them.
        Assert.Equal(new double[] { 132, 2.53, 0.915, 15 }, payload.Career.Values);
    }

    [Fact]
    public void A_hockey_season_gets_its_best_trophy_as_its_stamp()
    {
        Assert.Equal("Hart", BackOfTheCardGame.HockeyStamp(["StanleyCup", "ArtRoss", "MVP"]));
        Assert.Equal("Conn Smythe", BackOfTheCardGame.HockeyStamp(["StanleyCup", "ConnSmythe", "Norris"]));
        Assert.Equal("Calder", BackOfTheCardGame.HockeyStamp(["ROY", "StanleyCup"]));
        Assert.Equal("Stanley Cup", BackOfTheCardGame.HockeyStamp(["LadyByng", "StanleyCup"]));
        Assert.Null(BackOfTheCardGame.HockeyStamp(["AllStar", "ProBowl"]));
    }

    public void Dispose() => _world.Dispose();
}

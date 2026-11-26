using System.Text.Json;
using KnowBall.Application.Games;
using KnowBall.Application.Games.ScoutingReport;

namespace KnowBall.UnitTests;

/// <summary>The MLB editions of Career Path and Scouting Report, beside the NBA and NFL ones in the same small world.</summary>
public class MlbEditionTests : IDisposable
{
    private readonly GameWorld _world = new();
    private const string User = "user-mlb-0001";

    private GameWorld.Engine Engine(int seed = 1) => _world.NewEngine(seed, leagues: "nba,nfl,mlb");

    [Fact]
    public async Task Mlb_has_its_own_daily_once_it_is_live()
    {
        using (var dark = _world.NewEngine(leagues: "nba,nfl"))
        {
            var outcome = await dark.Service.GetDailyAsync("career-path", User, CancellationToken.None, "mlb");
            Assert.Equal(GameFailure.NotFound, outcome.Failure);
        }

        using var engine = Engine();
        var mlb = (await engine.Service.GetDailyAsync("scouting-report", User, CancellationToken.None, "mlb")).Result!;
        var nfl = (await engine.Service.GetDailyAsync("scouting-report", User, CancellationToken.None, "nfl")).Result!;
        Assert.Equal(("mlb", 1), (mlb.League, mlb.Number));
        Assert.NotEqual(nfl.PuzzleId, mlb.PuzzleId);
        Assert.Equal("mlb", (await engine.Service.GetDailyAsync("back-of-the-card", User, CancellationToken.None, "mlb")).Result!.League);
    }

    [Fact]
    public async Task Mlb_career_path_shows_seasons_by_their_own_year_and_gives_a_birthplace_hint()
    {
        using var engine = Engine();
        var state = (await engine.Service.GetDailyAsync("career-path", User, CancellationToken.None, "mlb")).Result!;

        // A baseball season is named for its one year, like the NFL's; the Expos keep their own name.
        var stops = state.Payload["stops"]!.AsArray().Select(s => $"{s!["abbreviation"]} {s["from"]}-{s["to"]}").ToList();
        Assert.Equal(["MON 1996-1999", "BOS 2000-2003", "LAD 2004-2004"], stops);

        // A big leaguer from the Dominican Republic went to no college: the hint is where he was born.
        var miss = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Ace Starter"), User, CancellationToken.None)).Result!;
        miss = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Big Bopper"), User, CancellationToken.None)).Result!;
        miss = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Short Stop"), User, CancellationToken.None)).Result!;
        var hints = miss.Clues!["hints"]!.AsArray();
        Assert.Equal(("Born", "Santo Domingo, Dominican Republic"), (hints[2]!["label"]!.GetValue<string>(), hints[2]!["value"]!.GetValue<string>()));
        // Short Stop played for Boston, the second stop.
        Assert.Equal([1], miss.Guesses[^1].Feedback!["shared"]!.AsArray().Select(n => n!.GetValue<int>()));

        var won = (await engine.Service.GuessAsync("career-path", state.PuzzleId, _world.Id("Journey Man"), User, CancellationToken.None)).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
    }

    [Fact]
    public void Basketball_hints_keep_the_country_when_there_is_no_college()
    {
        var hints = KnowBall.Application.Games.CareerPath.CareerPathGame.HintsFor(
            new KnowBall.Domain.Player { FullName = "Same Road", Country = "France", BirthPlace = "Lyon, France" });
        Assert.Equal(("From", "France"), (hints[2].Label, hints[2].Value));
    }

    [Fact]
    public async Task Mlb_scouting_report_scores_the_league_division_and_position_groups()
    {
        using var engine = Engine();
        var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(engine.Games.Find("scouting-report")!, GameWorld.Today, CancellationToken.None, "mlb");
        var secret = JsonSerializer.Deserialize<ScoutingSecret>(puzzle!.SecretJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("mlb", secret.League);
        Assert.Contains(secret.Name, new[] { "Ace Starter", "Big Bopper", "Short Stop", "Shut Down" });

        var ace = new ScoutingProfile("NYY", "Yankees", null, "AL", "AL East", "SP", 77, new DateOnly(1992, 6, 15), "45");
        var closer = new ScoutingProfile("SF", "Giants", null, "NL", "NL West", "RP", 76, new DateOnly(1993, 3, 3), "38");
        var shortstop = new ScoutingProfile("BOS", "Red Sox", null, "AL", "AL East", "SS", 72, new DateOnly(1999, 9, 9), "2");

        var pitchers = ScoutingReportGame.Compare(closer, ace, GameWorld.Today, league: "mlb");
        Assert.Equal(AttributeComparer.Close, pitchers.Position.State);
        Assert.Equal(AttributeComparer.Miss, pitchers.Conference.State);

        var east = ScoutingReportGame.Compare(shortstop, ace, GameWorld.Today, league: "mlb");
        Assert.Equal((AttributeComparer.Ok, AttributeComparer.Ok, AttributeComparer.Miss), (east.Conference.State, east.Division.State, east.Position.State));
    }

    [Theory]
    [InlineData("SP", "RP", "close")]
    [InlineData("P", "SP", "close")]
    [InlineData("SS", "2B", "close")]
    [InlineData("1B", "3B", "close")]
    [InlineData("LF", "CF", "close")]
    [InlineData("OF", "RF", "close")]
    [InlineData("TWP", "SP", "close")]
    [InlineData("DH", "TWP", "close")]
    [InlineData("C", "1B", "miss")]
    [InlineData("SS", "CF", "miss")]
    [InlineData("DH", "1B", "miss")]
    [InlineData("CF", "CF", "ok")]
    [InlineData("XX", "YY", "miss")]
    public void Baseball_positions_are_close_in_the_same_part_of_the_field(string guess, string answer, string expected) =>
        Assert.Equal(expected, AttributeComparer.BaseballPosition(guess, answer).State);

    [Fact]
    public async Task Mlb_back_of_the_card_prints_hitting_or_pitching_columns_and_baseball_stamps()
    {
        using var engine = Engine();
        var puzzle = await engine.Scheduler.GetOrCreateDailyAsync(engine.Games.Find("back-of-the-card")!, GameWorld.Today, CancellationToken.None, "mlb");
        var payload = System.Text.Json.Nodes.JsonNode.Parse(puzzle!.PayloadJson)!;
        var secret = JsonSerializer.Deserialize<KnowBall.Application.Games.BackOfTheCard.CardSecret>(puzzle.SecretJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Contains(secret.Name, new[] { "Ace Starter", "Big Bopper" });
        Assert.Equal(5, payload["seasons"]!.AsArray().Count);
        Assert.DoesNotContain(secret.Name, puzzle.PayloadJson);

        var labels = payload["columns"]!.AsArray().Select(c => c!["label"]!.GetValue<string>()).ToList();
        if (secret.Name == "Ace Starter")
        {
            Assert.Equal(["W", "L", "ERA", "SO"], labels);
            // 2021: 49 earned runs over 210 innings; the career ERA comes from the totals, not an average of seasons.
            Assert.Equal([18, 5, 2.1, 250], payload["seasons"]![2]!["values"]!.AsArray().Select(v => v!.GetValue<double>()));
            Assert.Equal(Math.Round(283 * 27 / 2490.0, 2), payload["career"]!["values"]![2]!.GetValue<double>());
            Assert.Equal([null, null, "Cy Young", "All-Star", null], secret.Stamps);
        }
        else
        {
            Assert.Equal(["H", "HR", "RBI", "AVG"], labels);
            Assert.Equal([170, 40, 110, 0.304], payload["seasons"]![1]!["values"]!.AsArray().Select(v => v!.GetValue<double>()));
            Assert.Equal(Math.Round(700 / 2390.0, 3), payload["career"]!["values"]![3]!.GetValue<double>());
            Assert.Equal([null, "Silver Slugger", null, "MVP", "Gold Glove"], secret.Stamps);
        }
    }

    [Fact]
    public void A_reliever_card_prints_saves_in_place_of_losses()
    {
        var line = new KnowBall.Application.Games.BackOfTheCard.BackOfTheCardGame.FootballLine(2024, 60,
            new Dictionary<string, double> { ["w"] = 3, ["l"] = 4, ["sv"] = 38, ["k"] = 80, ["outs"] = 195, ["er"] = 13 });
        var card = KnowBall.Application.Games.BackOfTheCard.BackOfTheCardGame.BaseballCard("RP", [line]);
        Assert.Equal(["W", "SV", "ERA", "SO"], card.Columns.Select(c => c.Label));
        Assert.Equal([3, 38, 1.8, 80], card.Seasons[0].Values);
        Assert.Equal(["W", "L", "ERA", "SO"], KnowBall.Application.Games.BackOfTheCard.BackOfTheCardGame.BaseballCard("P", [line]).Columns.Select(c => c.Label));
        Assert.Equal("AVG", KnowBall.Application.Games.BackOfTheCard.BackOfTheCardGame.BaseballCard("TWP", [line]).Columns[^1].Label);
    }

    [Fact]
    public void Only_the_nba_names_a_season_for_two_years()
    {
        Assert.True(LeagueCodes.SeasonEndsNextYear("nba"));
        Assert.False(LeagueCodes.SeasonEndsNextYear("nfl"));
        Assert.False(LeagueCodes.SeasonEndsNextYear("mlb"));
    }

    public void Dispose() => _world.Dispose();
}

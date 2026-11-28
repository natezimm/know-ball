using System.Text.Json;
using KnowBall.Application.Games;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The NHL editions of Career Path and Scouting Report, beside the NBA and NFL ones in the same small world.</summary>
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

    public void Dispose() => _world.Dispose();
}

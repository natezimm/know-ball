using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application.Games;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

public class BackOfTheCardGameTests : IDisposable
{
    private const string Me = "player-one-1234";
    private const string Slug = BackOfTheCardGame.GameSlug;

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

    private static bool[] OpenSteps(GameState state) =>
        state.Clues!["steps"]!.AsArray().Select(s => s!["open"]!.GetValue<bool>()).ToArray();

    private static string?[] Strings(JsonNode? array) =>
        array!.AsArray().Select(n => n?.GetValue<string>()).ToArray();

    [Fact]
    public async Task The_card_prints_every_season_and_the_career_line_but_nothing_that_names_him()
    {
        var state = await PuzzleFor("Kevin Durant");
        var payload = state.Payload.Deserialize<CardPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal([2007, 2008, 2013, 2016, 2019, 2020], payload.Seasons.Select(s => s.Season));
        Assert.Equal(new CardSeason(2013, 81, 32.0, 7.4, 5.5), payload.Seasons[2]);
        Assert.Equal(new CardCareer(6, 427, 26.0, 6.7, 4.3), payload.Career);

        var visible = state.Payload.ToJsonString() + state.Clues!.ToJsonString();
        Assert.DoesNotContain("Durant", visible);
        Assert.DoesNotContain("OKC", visible);
        Assert.DoesNotContain("MVP", visible);
        Assert.Equal(["Teams", "Position", "Awards", "Draft"], state.Clues!["steps"]!.AsArray().Select(s => s!["label"]!.GetValue<string>()));
        Assert.All(OpenSteps(state), Assert.False);
        Assert.Null(state.Answer);
    }

    [Fact]
    public async Task Only_players_with_five_or_more_seasons_are_answers()
    {
        var answers = new HashSet<int?>();
        for (var seed = 1; seed <= 40; seed++)
        {
            using var engine = _world.NewEngine(seed);
            var practice = (await engine.Service.StartPracticeAsync(Slug, default)).Result!;
            answers.Add((await engine.Db.Puzzles.SingleAsync(p => p.Id == practice.PuzzleId)).AnswerPlayerId);
        }

        Assert.Equal(
            new[] { "Kevin Durant", "Nikola Jokić", "One Stop" }.Select(n => (int?)_world.Id(n)).Order(),
            answers.Order());
    }

    [Fact]
    public async Task Each_miss_uncovers_the_next_part_of_the_card()
    {
        var state = await PuzzleFor("Kevin Durant");

        var one = await Guess(state.PuzzleId, "Nikola Jokić");
        Assert.Equal([true, false, false, false], OpenSteps(one));
        Assert.Equal(["SEA", "OKC", "OKC", "GSW", "GSW/MIA", "MIA"], Strings(one.Clues!["teams"]));
        Assert.Null(one.Clues["position"]);
        Assert.Null(one.Guesses[0].Feedback);

        var two = await Guess(state.PuzzleId, "Jayson Tatum");
        Assert.Equal(("F-G", "6′11″"), (two.Clues!["position"]!.GetValue<string>(), two.Clues["height"]!.GetValue<string>()));
        Assert.Null(two.Clues["stamps"]);

        var three = await Guess(state.PuzzleId, "Jalen Brunson");
        Assert.Equal(["ROY", null, "MVP", "All-NBA", null, null], Strings(three.Clues!["stamps"]));
        Assert.Null(three.Clues["drafted"]);

        var four = await Guess(state.PuzzleId, "Jaylen Brown");
        Assert.All(OpenSteps(four), Assert.True);
        Assert.Equal("2007, pick 2", four.Clues!["drafted"]!.GetValue<string>());
        Assert.Equal(("College", "Texas"), (four.Clues["origin"]!["label"]!.GetValue<string>(), four.Clues["origin"]!["value"]!.GetValue<string>()));
        Assert.Null(four.Answer);
    }

    [Fact]
    public async Task A_win_shows_the_whole_card_and_the_answer()
    {
        var state = await PuzzleFor("Kevin Durant");

        var won = await Guess(state.PuzzleId, "Kevin Durant");

        Assert.Equal(GameStatus.Won, won.Status);
        Assert.All(OpenSteps(won), Assert.True);
        Assert.Equal("Kevin Durant", won.Answer!["name"]!.GetValue<string>());
        Assert.Equal("https://heads/kevin-durant.png", won.Answer["headshotUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Eight_misses_lose_and_reveal_the_answer()
    {
        var state = await PuzzleFor("Nikola Jokić");
        string[] wrong = ["Kevin Durant", "Jayson Tatum", "Jalen Brunson", "Jaylen Brown", "Same Road", "Other Road", "One Stop"];
        foreach (var name in wrong) state = await Guess(state.PuzzleId, name);
        Assert.Equal(GameStatus.Playing, state.Status);

        // The eighth guess has to be someone new; the engine refuses a repeat.
        using (var engine = _world.NewEngine())
        {
            var extra = new Player { League = await engine.Db.Leagues.FirstAsync(), FullName = "Extra Man", FirstName = "Extra", LastName = "Man", NormalizedName = "extra man", ExternalId = "extra-man" };
            engine.Db.Players.Add(extra);
            await engine.Db.SaveChangesAsync();
            var lost = (await engine.Service.GuessAsync(Slug, state.PuzzleId, extra.Id, Me, default)).Result!;
            Assert.Equal(GameStatus.Lost, lost.Status);
            Assert.Equal("Nikola Jokić", lost.Answer!["name"]!.GetValue<string>());
        }
    }

    [Fact]
    public void A_season_gets_its_best_award_as_its_stamp()
    {
        Assert.Equal("MVP", BackOfTheCardGame.Stamp(["AllStar", "AllNBA1", "MVP"]));
        Assert.Equal("All-NBA", BackOfTheCardGame.Stamp(["AllStar", "AllNBA3"]));
        Assert.Equal("6MOY", BackOfTheCardGame.Stamp(["SMOY"]));
        Assert.Null(BackOfTheCardGame.Stamp(["AllDefense1", "AllRookie1"]));
    }

    [Fact]
    public void Seasons_without_a_full_line_are_left_off_the_card()
    {
        var (seasons, teams) = BackOfTheCardGame.Lines(
        [
            new PlayerSeasonStat { Season = 1990, IsSeasonTotal = true, Games = 0, PointsPerGame = 0 },
            new PlayerSeasonStat { Season = 1991, IsSeasonTotal = true, Games = 10, PointsPerGame = null },
            new PlayerSeasonStat { Season = 1992, IsSeasonTotal = true, Games = 10, PointsPerGame = 4.04, Team = new Team { Abbreviation = "BOS" } },
        ], []);

        Assert.Equal([new CardSeason(1992, 10, 4.0, 0, 0)], seasons);
        Assert.Equal(["BOS"], teams);
    }

    [Fact]
    public void A_traded_season_lists_the_clubs_in_the_order_he_played_for_them()
    {
        var sea = new Team { Id = 1, Abbreviation = "SEA" };
        var mil = new Team { Id = 2, Abbreviation = "MIL" };
        // The source lists the later club first.
        var (_, teams) = BackOfTheCardGame.Lines(
        [
            new PlayerSeasonStat { Id = 1, Season = 2002, IsSeasonTotal = true, Games = 80, PointsPerGame = 20.4 },
            new PlayerSeasonStat { Id = 2, Season = 2002, IsSeasonTotal = false, Games = 28, PointsPerGame = 19.6, Team = mil, TeamId = 2 },
            new PlayerSeasonStat { Id = 3, Season = 2002, IsSeasonTotal = false, Games = 52, PointsPerGame = 20.8, Team = sea, TeamId = 1 },
        ],
        [
            new PlayerTeamStint { TeamId = 1, StartSeason = 1990, EndSeason = 2002, Order = 1 },
            new PlayerTeamStint { TeamId = 2, StartSeason = 2002, EndSeason = 2002, Order = 2 },
        ]);

        Assert.Equal(["SEA/MIL"], teams);
    }

    public void Dispose() => _world.Dispose();
}

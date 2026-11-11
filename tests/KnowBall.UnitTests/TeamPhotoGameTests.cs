using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games;
using KnowBall.Application.Games.TeamPhoto;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

public class TeamPhotoGameTests : IDisposable
{
    private const string Me = "player-one-1234";
    private const string Slug = TeamPhotoGame.GameSlug;

    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(GameWorld.Start);
    private readonly Dictionary<string, int> _ids = [];

    // 2015-16 Golden State, a Finals team with games started; the two guards stand in front.
    private static readonly (string Name, string Pos, int Games, int Started, double Minutes)[] Warriors =
    [
        ("Draymond Green", "PF", 81, 81, 34.7), ("Klay Thompson", "SG", 80, 80, 33.3), ("Stephen Curry", "PG", 79, 79, 34.2),
        ("Andrew Bogut", "C", 70, 66, 20.7), ("Harrison Barnes", "SF", 66, 59, 30.9),
        ("Andre Iguodala", "SF", 65, 1, 26.6), ("Shaun Livingston", "PG", 78, 3, 19.5), ("Festus Ezeli", "C", 46, 13, 16.7),
    ];

    // 1977-78 Seattle, from before games started were kept: the five with the most minutes start.
    private static readonly (string Name, string Pos, int Games, double Minutes)[] Sonics =
    [
        ("Marvin Webster", "C", 82, 35.5), ("Gus Williams", "PG", 79, 32.6), ("Jack Sikma", "PF", 82, 27.3),
        ("Dennis Johnson", "SG", 81, 27.3), ("Paul Silas", "PF", 82, 26.5), ("Fred Brown", "SG", 72, 27.3),
    ];

    public TeamPhotoGameTests()
    {
        using var ctx = _db.NewContext();
        var nba = new League { Code = "nba", Name = "NBA", Sport = "Basketball" };
        Team Team(string abbr, string city, string name, int? from = null, int? to = null) =>
            new() { League = nba, ExternalId = abbr, Abbreviation = abbr, City = city, Name = name, ActiveFrom = from, ActiveTo = to, LogoUrl = to is null ? $"https://logos/{abbr}.svg" : null };
        var gsw = Team("GSW", "Golden State", "Warriors");
        var sea = Team("SEA", "Seattle", "SuperSonics", 1967, 2007);
        var chi = Team("CHI", "Chicago", "Bulls");
        var bos = Team("BOS", "Boston", "Celtics");
        var nyk = Team("NYK", "New York", "Knicks");
        ctx.Teams.AddRange(gsw, sea, chi, bos, nyk);
        ctx.TeamSeasons.AddRange(
            new TeamSeason { Team = gsw, Season = 2015, Wins = 73, Losses = 9, MadePlayoffs = true },
            new TeamSeason { Team = bos, Season = 2011, Wins = 39, Losses = 27, MadePlayoffs = true },
            new TeamSeason { Team = nyk, Season = 2011, Wins = 36, Losses = 30, MadePlayoffs = false });

        foreach (var w in Warriors) Add(ctx, nba, gsw, 2015, w.Name, w.Pos, w.Games, w.Minutes, w.Started);
        foreach (var s in Sonics) Add(ctx, nba, sea, 1977, s.Name, s.Pos, s.Games, s.Minutes, null);

        // 1997-98 Chicago: by starts Kukoč is fifth, but the curated list puts Pippen back in.
        Add(ctx, nba, chi, 1997, "Michael Jordan", "SG", 82, 38.8, 82);
        Add(ctx, nba, chi, 1997, "Ron Harper", "PG", 82, 28.9, 82);
        Add(ctx, nba, chi, 1997, "Dennis Rodman", "PF", 80, 35.7, 66);
        Add(ctx, nba, chi, 1997, "Luc Longley", "C", 58, 23.9, 58);
        Add(ctx, nba, chi, 1997, "Toni Kukoč", "SF", 74, 30.3, 52, "kukocto01");
        Add(ctx, nba, chi, 1997, "Scottie Pippen", "SF", 44, 37.5, 44, "pippesc01");

        // Practice teams: a playoff team with a clear five, and one that missed the playoffs.
        foreach (var (name, i) in new[] { "Rajon Rondo", "Ray Allen", "Paul Pierce", "Kevin Garnett", "Jermaine O'Neal", "Brandon Bass" }.Select((n, i) => (n, i)))
            Add(ctx, nba, bos, 2011, name, i < 2 ? "G" : "F", 60, 30 - i, i < 5 ? 60 - i : 5);
        foreach (var (name, i) in new[] { "Carmelo Anthony", "Amar'e Stoudemire", "Tyson Chandler", "Iman Shumpert", "Landry Fields", "J.R. Smith" }.Select((n, i) => (n, i)))
            Add(ctx, nba, nyk, 2011, name, "F", 60, 30 - i, i < 5 ? 60 - i : 5);

        // Someone who never played for any of these teams.
        Add(ctx, nba, null, 2015, "Kevin Durant", "SF", 72, 35.8, 72);
        ctx.SaveChanges();
        foreach (var p in ctx.Players) _ids[p.FullName] = p.Id;
    }

    private static void Add(Microsoft.EntityFrameworkCore.DbContext ctx, League nba, Team? team, int season, string name, string pos, int games, double minutes, int? started, string? bref = null)
    {
        var space = name.IndexOf(' ');
        var player = new Player
        {
            League = nba, ExternalId = name.ToLowerInvariant().Replace(' ', '-'), FirstName = name[..space], LastName = name[(space + 1)..],
            FullName = name, NormalizedName = name.ToLowerInvariant(), Position = "G-F", Popularity = 50,
            HeadshotUrl = $"https://heads/{name.Replace(' ', '-')}.png",
        };
        var stats = started is { } gs ? $$"""{"gs":{{gs}},"pos":"{{pos}}"}""" : $$"""{"pos":"{{pos}}"}""";
        if (team is not null)
            player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Team = team, Games = games, MinutesPerGame = minutes, PointsPerGame = 10, ReboundsPerGame = 5, AssistsPerGame = 3, StatsJson = stats });
        if (bref is not null) player.ExternalIds.Add(new PlayerExternalId { Source = "bref", Value = bref });
        ctx.Add(player);
    }

    private (GameService Service, PuzzleScheduler Scheduler, Infrastructure.Persistence.TriviaDbContext Ctx) Engine(int seed = 1)
    {
        var ctx = _db.NewContext();
        var calendar = new PuzzleCalendar(_clock);
        var registry = new GameRegistry([new TeamPhotoGame(ctx)]);
        var scheduler = new PuzzleScheduler(ctx, calendar, _clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        return (new GameService(ctx, registry, scheduler, calendar, _clock), scheduler, ctx);
    }

    private async Task<GameState> Daily()
    {
        var (service, _, ctx) = Engine();
        using (ctx)
        {
            var outcome = await service.GetDailyAsync(Slug, Me, default);
            Assert.True(outcome.Ok, outcome.Error);
            return outcome.Result!;
        }
    }

    private async Task<GameOutcome<GameState>> Guess(int puzzleId, string name)
    {
        var (service, _, ctx) = Engine();
        using (ctx) return await service.GuessAsync(Slug, puzzleId, _ids[name], Me, default);
    }

    /// <summary>Today's puzzle, remade until it is the given team-season.</summary>
    private async Task<GameState> DailyFor(string key)
    {
        for (var seed = 1; seed < 100; seed++)
        {
            var (service, _, ctx) = Engine(seed);
            using (ctx)
            {
                var state = (await service.GetDailyAsync(Slug, Me, default)).Result!;
                await using var db = _db.NewContext();
                var puzzle = await db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
                if (GameJsonKey(puzzle.SecretJson) == key) return state;
                db.Puzzles.Remove(puzzle);
                await db.SaveChangesAsync();
            }
        }
        throw new InvalidOperationException($"{key} never came up");
    }

    private static string? GameJsonKey(string secret) => JsonNode.Parse(secret)!["key"]!.GetValue<string>();

    private static List<string> Names(JsonNode answer) =>
        answer["slots"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToList();

    [Fact]
    public void The_finals_list_has_a_champion_and_a_runner_up_for_every_season_since_the_merger()
    {
        var teams = TeamPhotoGame.FinalsTeams;

        Assert.Equal(100, teams.Count);
        Assert.Equal(Enumerable.Range(1976, 50), teams.Select(t => t.Season).Distinct());
        Assert.All(teams.GroupBy(t => t.Season), g => Assert.Equal(["lost", "won"], g.Select(t => t.Finals).Order()));
        Assert.Equal(100, teams.Select(t => t.Key).Distinct().Count());
        Assert.All(teams.Where(t => t.Swap is not null), t => Assert.False(string.IsNullOrWhiteSpace(t.Why)));
        Assert.Contains(teams, t => t.Key == "2015-GSW" && t.Finals == "lost");
    }

    [Fact]
    public void The_lineup_is_the_five_with_the_most_starts_or_before_starts_were_kept_the_most_minutes()
    {
        RosterLine L(int id, int games, double minutes, int? started, string pos = "SF") => new(id, games, minutes, started, pos, 0, 0, 0);

        var byStarts = new[] { L(1, 80, 20, 80), L(2, 80, 40, 10), L(3, 80, 30, 70), L(4, 70, 25, 60), L(5, 60, 30, 50), L(6, 60, 30, 40) };
        Assert.Equal([1, 3, 4, 5, 6], TeamPhotoGame.Lineup(byStarts, null).Select(l => l.PlayerId));
        Assert.Equal([1, 3, 4, 5, 2], TeamPhotoGame.Lineup(byStarts, new Dictionary<int, int> { [6] = 2 }).Select(l => l.PlayerId));

        var byMinutes = new[] { L(1, 80, 20, null), L(2, 80, 40, null), L(3, 80, 30, 70), L(4, 70, 25, null), L(5, 60, 30, null), L(6, 10, 30, null) };
        Assert.Equal([2, 3, 5, 4, 1], TeamPhotoGame.Lineup(byMinutes, null).Select(l => l.PlayerId));

        Assert.True(TeamPhotoGame.IsClear([L(1, 80, 20, 80), L(3, 80, 30, 70), L(4, 70, 25, 60), L(5, 60, 30, 50), L(8, 60, 30, 45), L(7, 50, 10, 5)]));
        Assert.False(TeamPhotoGame.IsClear([.. byStarts.Where(l => l.PlayerId != 2), L(2, 80, 40, 35)])); // fifth 40, sixth 35
        Assert.False(TeamPhotoGame.IsClear([.. byStarts.Where(l => l.PlayerId != 6), L(6, 60, 30, 25)])); // fifth only 25
        Assert.False(TeamPhotoGame.IsClear(byMinutes));

        var five = new[] { L(1, 1, 1, 1, "SG"), L(2, 1, 1, 1, "C"), L(3, 1, 1, 1, "PG"), L(4, 1, 1, 1, "PF"), L(5, 1, 1, 1, "SF-PF") };
        Assert.Equal([5, 4, 2, 3, 1], TeamPhotoGame.Order(five).Select(l => l.PlayerId));
        Assert.Equal(("front", "front", "back"), (TeamPhotoGame.RowOf("G"), TeamPhotoGame.RowOf("SG-SF"), TeamPhotoGame.RowOf("F-G")));
    }

    [Fact]
    public async Task The_photo_shows_the_team_its_record_and_blank_spots_but_no_names()
    {
        var state = await DailyFor("2015-GSW");
        var payload = state.Payload;

        Assert.Equal((4, GameStatus.Playing), (state.MaxGuesses, state.Status));
        Assert.Equal(("Golden State", "Warriors", "https://logos/GSW.svg"),
            (payload["team"]!["city"]!.GetValue<string>(), payload["team"]!["name"]!.GetValue<string>(), payload["team"]!["logoUrl"]!.GetValue<string>()));
        Assert.Equal((2015, 73, 9, "lost"),
            (payload["season"]!.GetValue<int>(), payload["wins"]!.GetValue<int>(), payload["losses"]!.GetValue<int>(), payload["finals"]!.GetValue<string>()));
        var slots = payload["slots"]!.AsArray();
        Assert.Equal(["SF back", "PF back", "C back", "PG front", "SG front"], slots.Select(s => $"{s!["position"]} {s["row"]}"));
        Assert.Equal(10.0, slots[0]!["points"]!.GetValue<double>());

        var json = payload.ToJsonString();
        Assert.DoesNotContain("Curry", json);
        Assert.DoesNotContain("player", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(state.Answer);
        Assert.Null(state.Clues);
    }

    [Fact]
    public async Task Starters_fill_their_spot_bench_players_are_free_and_four_outsiders_lose()
    {
        var state = await DailyFor("2015-GSW");

        var curry = (await Guess(state.PuzzleId, "Stephen Curry")).Result!;
        Assert.Equal((true, 3), (curry.Guesses[^1].Correct, curry.Guesses[^1].Feedback!["slot"]!.GetValue<int>()));

        var bench = (await Guess(state.PuzzleId, "Andre Iguodala")).Result!;
        Assert.False(bench.Guesses[^1].Correct);
        Assert.False(bench.Guesses[^1].IsMiss);
        Assert.True(bench.Guesses[^1].Feedback!["bench"]!.GetValue<bool>());

        foreach (var outsider in new[] { "Kevin Durant", "Michael Jordan", "Jack Sikma" })
        {
            var miss = (await Guess(state.PuzzleId, outsider)).Result!;
            Assert.True(miss.Guesses[^1].IsMiss);
            Assert.Equal(GameStatus.Playing, miss.Status);
            Assert.Null(miss.Answer);
        }

        var lost = (await Guess(state.PuzzleId, "Ray Allen")).Result!;
        Assert.Equal(GameStatus.Lost, lost.Status);
        Assert.Equal(["Harrison Barnes", "Draymond Green", "Andrew Bogut", "Stephen Curry", "Klay Thompson"], Names(lost.Answer!));

        await using var db = _db.NewContext();
        var result = await db.UserResults.SingleAsync();
        Assert.Equal((false, 0, 4), (result.Solved, result.Score, result.HintsUsed));
    }

    [Fact]
    public async Task Naming_all_five_wins_and_scores_by_the_misses()
    {
        var state = await DailyFor("2015-GSW");
        await Guess(state.PuzzleId, "Kevin Durant");
        await Guess(state.PuzzleId, "Shaun Livingston");
        foreach (var starter in new[] { "Stephen Curry", "Klay Thompson", "Draymond Green", "Harrison Barnes" })
            Assert.Equal(GameStatus.Playing, (await Guess(state.PuzzleId, starter)).Result!.Status);

        var won = (await Guess(state.PuzzleId, "Andrew Bogut")).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
        Assert.Equal(5, Names(won.Answer!).Count);

        await using var db = _db.NewContext();
        var result = await db.UserResults.SingleAsync();
        Assert.Equal((true, 2, 1), (result.Solved, result.Score, result.HintsUsed)); // one miss: score 2
        Assert.False((await Guess(state.PuzzleId, "Festus Ezeli")).Ok);
    }

    [Fact]
    public async Task A_former_team_from_before_starts_were_kept_uses_minutes()
    {
        var state = await DailyFor("1977-SEA");
        Assert.Equal("Seattle", state.Payload["team"]!["city"]!.GetValue<string>());
        Assert.Null(state.Payload["wins"]); // no record imported for this season

        foreach (var name in new[] { "Marvin Webster", "Gus Williams", "Jack Sikma", "Dennis Johnson" })
            await Guess(state.PuzzleId, name);
        var won = (await Guess(state.PuzzleId, "Paul Silas")).Result!;
        Assert.Equal(GameStatus.Won, won.Status);
    }

    [Fact]
    public async Task The_curated_list_can_put_a_starter_back_in()
    {
        var state = await DailyFor("1997-CHI");
        var pippen = (await Guess(state.PuzzleId, "Scottie Pippen")).Result!;
        Assert.True(pippen.Guesses[^1].Correct);
        var kukoc = (await Guess(state.PuzzleId, "Toni Kukoč")).Result!;
        Assert.True(kukoc.Guesses[^1].Feedback!["bench"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Queued_dailies_use_every_team_before_one_comes_round_again()
    {
        var (_, scheduler, ctx) = Engine();
        using (ctx) Assert.Equal(7, await scheduler.FillQueueAsync(new TeamPhotoGame(ctx), 7, default));

        await using var db = _db.NewContext();
        var keys = (await db.Puzzles.OrderBy(p => p.Date).ToListAsync()).Select(p => GameJsonKey(p.SecretJson)).ToList();
        // Three Finals teams have data here: each once, then again in the same order.
        Assert.Equal(3, keys.Take(3).Distinct().Count());
        Assert.Equal(keys.Take(3), keys.Skip(3).Take(3));
        Assert.Equal(keys[0], keys[6]);
        Assert.All(await db.Puzzles.ToListAsync(), p => Assert.Null(p.AnswerPlayerId));
    }

    [Fact]
    public async Task Practice_draws_a_playoff_team_with_a_clear_five_and_leaves_finals_teams_to_the_dailies()
    {
        for (var seed = 1; seed <= 5; seed++)
        {
            var (service, _, ctx) = Engine(seed);
            using (ctx)
            {
                var practice = (await service.StartPracticeAsync(Slug, default)).Result!;
                Assert.True(practice.Practice);
                Assert.Equal(("Boston", 2011, (string?)null),
                    (practice.Payload["team"]!["city"]!.GetValue<string>(), practice.Payload["season"]!.GetValue<int>(), practice.Payload["finals"]?.GetValue<string>()));
            }
        }
    }

    public void Dispose() => _db.Dispose();
}

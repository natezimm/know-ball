using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games;
using KnowBall.Application.Games.TeamPhoto;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The NFL edition of Team Photo: the offense's five skill players of a Super Bowl team.</summary>
public class TeamPhotoNflTests : IDisposable
{
    private const string Me = "player-nfl-photo-1";
    private const string Slug = TeamPhotoGame.GameSlug;

    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(GameWorld.Start);
    private readonly Dictionary<string, int> _ids = [];

    public TeamPhotoNflTests()
    {
        using var ctx = _db.NewContext();
        var nfl = new League { Code = "nfl", Name = "NFL", Sport = "Football" };
        Team Team(string abbr, string city, string name, int? from = null, int? to = null) =>
            new() { League = nfl, ExternalId = abbr, Abbreviation = abbr, City = city, Name = name, ActiveFrom = from, ActiveTo = to, LogoUrl = $"https://logos/{abbr}.png" };
        var kc = Team("KC", "Kansas City", "Chiefs");
        var bal = Team("BAL", "Baltimore", "Ravens");
        var buf = Team("BUF", "Buffalo", "Bills");
        var nyj = Team("NYJ", "New York", "Jets");
        var stl = Team("STL", "St. Louis", "Rams", 1995, 2015);
        ctx.Teams.AddRange(kc, bal, buf, nyj, stl);
        ctx.TeamSeasons.AddRange(
            new TeamSeason { Team = kc, Season = 2019, Wins = 12, Losses = 4, MadePlayoffs = true },
            new TeamSeason { Team = bal, Season = 2000, Wins = 12, Losses = 4, MadePlayoffs = true },
            new TeamSeason { Team = buf, Season = 2020, Wins = 13, Losses = 3, MadePlayoffs = true },
            new TeamSeason { Team = nyj, Season = 2020, Wins = 2, Losses = 14, MadePlayoffs = false },
            new TeamSeason { Team = stl, Season = 1999, Wins = 13, Losses = 3, MadePlayoffs = true });

        // 2019 Kansas City: the leader at each place, a backup behind each, and a lineman and kicker with no stat line.
        Add(ctx, nfl, kc, 2019, "Patrick Mahomes", "QB", 14, """{"att":484,"pyd":4031,"ptd":26,"int":5}""", popularity: 99);
        Add(ctx, nfl, kc, 2019, "Matt Moore", "QB", 6, """{"att":91,"pyd":659,"ptd":4}""");
        Add(ctx, nfl, kc, 2019, "Damien Williams", "RB", 11, """{"car":111,"ryd":498,"rtd":5,"rec":30}""");
        Add(ctx, nfl, kc, 2019, "LeSean McCoy", "RB", 13, """{"car":101,"ryd":465,"rtd":4}""");
        Add(ctx, nfl, kc, 2019, "Tyreek Hill", "WR", 12, """{"rec":58,"wyd":860,"wtd":7}""");
        Add(ctx, nfl, kc, 2019, "Sammy Watkins", "WR", 13, """{"rec":52,"wyd":673,"wtd":3}""");
        Add(ctx, nfl, kc, 2019, "Mecole Hardman", "WR", 16, """{"rec":26,"wyd":538,"wtd":6}""");
        Add(ctx, nfl, kc, 2019, "Travis Kelce", "TE", 16, """{"rec":97,"wyd":1229,"wtd":5}""");
        Add(ctx, nfl, kc, 2019, "Eric Fisher", "T", 8, null);
        Add(ctx, nfl, kc, 2019, "Harrison Butker", "K", 16, null);

        // 2000 Baltimore: Banks threw more, but the curated list puts Dilfer in.
        Add(ctx, nfl, bal, 2000, "Tony Banks", "QB", 11, """{"att":274,"pyd":1578}""", gsis: "00-0000722");
        Add(ctx, nfl, bal, 2000, "Trent Dilfer", "QB", 11, """{"att":226,"pyd":1502}""", gsis: "00-0004293");
        Add(ctx, nfl, bal, 2000, "Jamal Lewis", "RB", 16, """{"car":309,"ryd":1364}""");
        Add(ctx, nfl, bal, 2000, "Qadry Ismail", "WR", 15, """{"rec":49,"wyd":655}""");
        Add(ctx, nfl, bal, 2000, "Travis Taylor", "WR", 9, """{"rec":28,"wyd":276}""");
        Add(ctx, nfl, bal, 2000, "Shannon Sharpe", "TE", 16, """{"rec":67,"wyd":810}""");
        // Traded to Baltimore midseason: his Ravens line leads their receivers, though his whole season is on a line with no team.
        Add(ctx, nfl, bal, 2000, "Midseason Arrival", "WR", 8, """{"rec":40,"wyd":700}""", split: (nyj, """{"rec":30,"wyd":400}"""));

        // 1999 St. Louis has no tight end on record, so it makes no photo.
        Add(ctx, nfl, stl, 1999, "Kurt Warner", "QB", 16, """{"att":499}""");
        Add(ctx, nfl, stl, 1999, "Marshall Faulk", "RB", 16, """{"car":253}""");
        Add(ctx, nfl, stl, 1999, "Isaac Bruce", "WR", 16, """{"wyd":1165}""");
        Add(ctx, nfl, stl, 1999, "Torry Holt", "WR", 16, """{"wyd":788}""");

        // Practice: a playoff team with a clear five, and a team that missed the playoffs.
        foreach (var (team, prefix) in new[] { (buf, "Bills"), (nyj, "Jets") })
        {
            Add(ctx, nfl, team, 2020, $"{prefix} Passer", "QB", 16, """{"att":572}""");
            Add(ctx, nfl, team, 2020, $"{prefix} Backup", "QB", 4, """{"att":20}""");
            Add(ctx, nfl, team, 2020, $"{prefix} Runner", "RB", 16, """{"car":200}""");
            Add(ctx, nfl, team, 2020, $"{prefix} Wideout", "WR", 16, """{"wyd":1500}""");
            Add(ctx, nfl, team, 2020, $"{prefix} Flanker", "WR", 16, """{"wyd":1000}""");
            Add(ctx, nfl, team, 2020, $"{prefix} End", "TE", 16, """{"wyd":400}""");
        }
        ctx.SaveChanges();
        foreach (var p in ctx.Players) _ids[p.FullName] = p.Id;
    }

    private static void Add(Microsoft.EntityFrameworkCore.DbContext ctx, League league, Team team, int season, string name, string pos, int games,
        string? stats, string? gsis = null, double popularity = 50, (Team Team, string Stats)? split = null)
    {
        var space = name.IndexOf(' ');
        var player = new Player
        {
            League = league, ExternalId = name.ToLowerInvariant().Replace(' ', '-'), FirstName = name[..space], LastName = name[(space + 1)..],
            FullName = name, NormalizedName = name.ToLowerInvariant(), Position = pos, Popularity = popularity,
            HeadshotUrl = $"https://heads/{name.Replace(' ', '-')}.png",
        };
        // Linemen and kickers have no stat line, only the stint that puts them on the team.
        if (split is { } other)
        {
            // A season split by a trade, stored as the importer does: a line per team and the whole season on a line with no team.
            player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Team = other.Team, IsSeasonTotal = false, Games = 8, StatsJson = other.Stats });
            player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Team = team, IsSeasonTotal = false, Games = games, StatsJson = stats! });
            player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Games = games + 8, StatsJson = """{"rec":70,"wyd":1100}""" });
        }
        else if (stats is not null) player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Team = team, Games = games, StatsJson = stats });
        player.Stints.Add(new PlayerTeamStint { Team = team, StartSeason = season - 1, EndSeason = season + 1, Seasons = 3 });
        if (gsis is not null) player.ExternalIds.Add(new PlayerExternalId { Source = "gsis", Value = gsis });
        ctx.Add(player);
    }

    private (GameService Service, PuzzleScheduler Scheduler, Infrastructure.Persistence.TriviaDbContext Ctx) Engine(int seed = 1)
    {
        var ctx = _db.NewContext();
        var calendar = new PuzzleCalendar(_clock);
        var registry = new GameRegistry([new TeamPhotoGame(ctx)],
            Microsoft.Extensions.Options.Options.Create(new GameLeagueOptions { Leagues = "nba,nfl" }));
        var scheduler = new PuzzleScheduler(ctx, calendar, _clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        return (new GameService(ctx, registry, scheduler, calendar, _clock), scheduler, ctx);
    }

    private async Task<GameOutcome<GameState>> Guess(int puzzleId, string name)
    {
        var (service, _, ctx) = Engine();
        using (ctx) return await service.GuessAsync(Slug, puzzleId, _ids[name], Me, default);
    }

    /// <summary>Today's NFL puzzle, remade until it is the given team-season.</summary>
    private async Task<GameState> DailyFor(string key)
    {
        for (var seed = 1; seed < 100; seed++)
        {
            var (service, _, ctx) = Engine(seed);
            using (ctx)
            {
                var outcome = await service.GetDailyAsync(Slug, Me, default, "nfl");
                Assert.True(outcome.Ok, outcome.Error);
                var state = outcome.Result!;
                await using var db = _db.NewContext();
                var puzzle = await db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
                if (JsonNode.Parse(puzzle.SecretJson)!["key"]!.GetValue<string>() == key) return state;
                db.Puzzles.Remove(puzzle);
                await db.SaveChangesAsync();
            }
        }
        throw new InvalidOperationException($"{key} never came up");
    }

    private static List<string> Names(JsonNode answer) =>
        answer["slots"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToList();

    [Fact]
    public void The_super_bowl_list_has_a_winner_and_a_loser_for_every_season_since_1999()
    {
        var teams = TeamPhotoGame.SuperBowlTeams;

        Assert.Equal(54, teams.Count);
        Assert.Equal(Enumerable.Range(1999, 27), teams.Select(t => t.Season).Distinct());
        Assert.All(teams.GroupBy(t => t.Season), g => Assert.Equal(["lost", "won"], g.Select(t => t.Finals).Order()));
        Assert.Equal(54, teams.Select(t => t.Key).Distinct().Count());
        Assert.All(teams.Where(t => t.Swap is not null), t => Assert.False(string.IsNullOrWhiteSpace(t.Why)));
        Assert.Contains(teams, t => t.Key == "2019-KC" && t.Finals == "won");
        Assert.Contains(teams, t => t.Key == "1999-STL" && t.Finals == "won");
        Assert.Contains(teams, t => t.Key == "2021-LAR" && t.Finals == "won");
    }

    [Fact]
    public void The_five_lead_their_position_and_a_season_is_clear_without_an_injury_muddle()
    {
        TeamPhotoGame.FootballRosterLine L(int id, string pos, int games, string key, double value) => new(id, pos, games, new Dictionary<string, double> { [key] = value });

        var lines = new[]
        {
            L(1, "QB", 16, "att", 500), L(2, "QB", 4, "att", 100), L(3, "RB", 16, "car", 250), L(4, "HB", 16, "car", 260),
            L(5, "WR", 16, "wyd", 900), L(6, "WR", 16, "wyd", 1200), L(7, "WR", 16, "wyd", 300), L(8, "TE", 16, "wyd", 600),
            L(9, "TE", 16, "wyd", 0), L(10, "T", 16, "wyd", 5000),
        };
        var five = TeamPhotoGame.FootballLineup(lines, null)!;
        // Receivers at the back with the tight end between them, then the quarterback and running back in front.
        Assert.Equal(["6 WR back", "8 TE back", "5 WR back", "1 QB front", "4 RB front"], five.Select(f => $"{f.Line.PlayerId} {f.Position} {f.Row}"));
        Assert.Equal([6, 8, 5, 2, 4], TeamPhotoGame.FootballLineup(lines, new Dictionary<int, int> { [1] = 2 })!.Select(f => f.Line.PlayerId));
        Assert.True(TeamPhotoGame.IsClearFootball(lines));

        // A fullback carries the ball when there is no running back; no tight end at all makes no photo.
        Assert.Equal(11, TeamPhotoGame.FootballLineup([.. lines.Where(l => l.PlayerId is not (3 or 4)), L(11, "FB", 16, "car", 40)], null)![4].Line.PlayerId);
        Assert.Null(TeamPhotoGame.FootballLineup([.. lines.Where(l => l.PlayerId != 8)], null));

        // Two quarterbacks sharing the season, or a starter who missed half of it, is not clear.
        Assert.False(TeamPhotoGame.IsClearFootball([.. lines.Where(l => l.PlayerId != 2), L(2, "QB", 8, "att", 300)]));
        Assert.False(TeamPhotoGame.IsClearFootball([.. lines.Where(l => l.PlayerId != 8), L(8, "TE", 6, "wyd", 600)]));

        Assert.Equal(new Dictionary<string, double> { ["pyd"] = 0, ["ptd"] = 0, ["int"] = 0 }, TeamPhotoGame.SlotStats("QB", lines[0]));
        Assert.Equal(["rec", "wyd", "wtd"], TeamPhotoGame.SlotStats("TE", lines[7]).Keys);
    }

    [Fact]
    public async Task The_photo_shows_the_record_the_super_bowl_and_each_places_line_but_no_names()
    {
        var state = await DailyFor("2019-KC");
        var payload = state.Payload;

        Assert.Equal(("nfl", 4, GameStatus.Playing), (state.League, state.MaxGuesses, state.Status));
        Assert.Equal((2019, 12, 4, 0, "won"),
            (payload["season"]!.GetValue<int>(), payload["wins"]!.GetValue<int>(), payload["losses"]!.GetValue<int>(),
                payload["ties"]!.GetValue<int>(), payload["finals"]!.GetValue<string>()));
        var slots = payload["slots"]!.AsArray();
        Assert.Equal(["WR back", "TE back", "WR back", "QB front", "RB front"], slots.Select(s => $"{s!["position"]} {s["row"]}"));
        Assert.Equal((14, 4031.0, 26.0, 5.0),
            (slots[3]!["games"]!.GetValue<int>(), slots[3]!["stats"]!["pyd"]!.GetValue<double>(), slots[3]!["stats"]!["ptd"]!.GetValue<double>(),
                slots[3]!["stats"]!["int"]!.GetValue<double>()));
        Assert.Equal(1229.0, slots[1]!["stats"]!["wyd"]!.GetValue<double>());

        var json = payload.ToJsonString();
        Assert.DoesNotContain("Mahomes", json);
        Assert.DoesNotContain("player", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(state.Answer);
    }

    [Fact]
    public async Task Linemen_and_backups_are_the_bench_and_four_outsiders_lose()
    {
        var state = await DailyFor("2019-KC");

        var kelce = (await Guess(state.PuzzleId, "Travis Kelce")).Result!;
        Assert.Equal((true, 1), (kelce.Guesses[^1].Correct, kelce.Guesses[^1].Feedback!["slot"]!.GetValue<int>()));
        foreach (var bench in new[] { "Eric Fisher", "Harrison Butker", "Matt Moore" })
        {
            var free = (await Guess(state.PuzzleId, bench)).Result!;
            Assert.False(free.Guesses[^1].IsMiss);
        }

        foreach (var outsider in new[] { "Tony Banks", "Kurt Warner", "Bills Passer" })
            Assert.True((await Guess(state.PuzzleId, outsider)).Result!.Guesses[^1].IsMiss);
        var lost = (await Guess(state.PuzzleId, "Jets End")).Result!;
        Assert.Equal(GameStatus.Lost, lost.Status);
        Assert.Equal(["Tyreek Hill", "Travis Kelce", "Sammy Watkins", "Patrick Mahomes", "Damien Williams"], Names(lost.Answer!));
    }

    [Fact]
    public async Task The_curated_list_can_put_the_super_bowl_quarterback_in_and_a_traded_player_counts_his_line_for_this_team()
    {
        var state = await DailyFor("2000-BAL");
        Assert.True((await Guess(state.PuzzleId, "Trent Dilfer")).Result!.Guesses[^1].Correct);
        Assert.False((await Guess(state.PuzzleId, "Tony Banks")).Result!.Guesses[^1].IsMiss);

        // His 700 yards for Baltimore lead Ismail's 655; the receivers stand back left and back right.
        var arrival = (await Guess(state.PuzzleId, "Midseason Arrival")).Result!;
        Assert.Equal((true, 0), (arrival.Guesses[^1].Correct, arrival.Guesses[^1].Feedback!["slot"]!.GetValue<int>()));
        Assert.Equal(700.0, arrival.Payload["slots"]![0]!["stats"]!["wyd"]!.GetValue<double>());
        Assert.False((await Guess(state.PuzzleId, "Travis Taylor")).Result!.Guesses[^1].IsMiss);
    }

    [Fact]
    public async Task Queued_dailies_skip_super_bowl_teams_without_a_full_five()
    {
        var (_, scheduler, ctx) = Engine();
        using (ctx) Assert.Equal(4, await scheduler.FillQueueAsync(new TeamPhotoGame(ctx), 4, default, "nfl"));

        await using var db = _db.NewContext();
        var keys = (await db.Puzzles.OrderBy(p => p.Date).ToListAsync()).Select(p => JsonNode.Parse(p.SecretJson)!["key"]!.GetValue<string>()).ToList();
        Assert.Equal(["2000-BAL", "2019-KC"], keys.Take(2).Order());
        Assert.DoesNotContain("1999-STL", keys);
    }

    [Fact]
    public async Task A_season_without_its_record_makes_no_photo_until_the_history_is_reloaded()
    {
        // The record and the stat lines of careers that began before 1999 arrive in the same history load; until then a
        // photo could put a backup in a star's place.
        await using (var db = _db.NewContext())
        {
            db.TeamSeasons.RemoveRange(db.TeamSeasons.Where(t => t.Season == 2019 || t.Season == 2020));
            await db.SaveChangesAsync();
        }

        var (service, scheduler, ctx) = Engine();
        using (ctx)
        {
            Assert.Equal(2, await scheduler.FillQueueAsync(new TeamPhotoGame(ctx), 2, default, "nfl"));
            Assert.Equal(GameFailure.NotFound, (await service.StartPracticeAsync(Slug, default, "nfl")).Failure);
        }
        await using var check = _db.NewContext();
        Assert.All(await check.Puzzles.ToListAsync(), p => Assert.Equal("2000-BAL", JsonNode.Parse(p.SecretJson)!["key"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Practice_draws_a_playoff_team_with_a_clear_five_and_leaves_super_bowl_teams_to_the_dailies()
    {
        for (var seed = 1; seed <= 5; seed++)
        {
            var (service, _, ctx) = Engine(seed);
            using (ctx)
            {
                var practice = (await service.StartPracticeAsync(Slug, default, "nfl")).Result!;
                Assert.Equal(("nfl", "Buffalo", 2020, (string?)null),
                    (practice.League, practice.Payload["team"]!["city"]!.GetValue<string>(), practice.Payload["season"]!.GetValue<int>(),
                        practice.Payload["finals"]?.GetValue<string>()));
            }
        }
    }

    public void Dispose() => _db.Dispose();
}

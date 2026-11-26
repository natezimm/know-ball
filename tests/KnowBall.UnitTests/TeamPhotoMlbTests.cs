using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games;
using KnowBall.Application.Games.TeamPhoto;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The MLB edition of Team Photo: a World Series team's eight fielders and its top starting pitcher.</summary>
public class TeamPhotoMlbTests : IDisposable
{
    private const string Me = "player-mlb-photo-1";
    private const string Slug = TeamPhotoGame.GameSlug;

    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(GameWorld.Start);
    private readonly Dictionary<string, int> _ids = [];

    public TeamPhotoMlbTests()
    {
        using var ctx = _db.NewContext();
        var mlb = new League { Code = "mlb", Name = "MLB", Sport = "Baseball" };
        Team Team(string id, string abbr, string city, string name, int? from = null, int? to = null) =>
            new() { League = mlb, ExternalId = id, Abbreviation = abbr, City = city, Name = name, ActiveFrom = from, ActiveTo = to };
        var cubs = Team("112", "CHC", "Chicago", "Cubs", 1960);
        // The Expos are filed under the Nationals' id, keyed with their first season.
        var nats = Team("120", "WSH", "Washington", "Nationals", 2005);
        var expos = Team("120-1969", "MON", "Montreal", "Expos", 1969, 2004);
        var mets = Team("121", "NYM", "New York", "Mets", 1962);
        ctx.Teams.AddRange(cubs, nats, expos, mets);
        ctx.TeamSeasons.AddRange(
            new TeamSeason { Team = cubs, Season = 2016, Wins = 103, Losses = 58, MadePlayoffs = true },
            new TeamSeason { Team = expos, Season = 1981, Wins = 60, Losses = 48, MadePlayoffs = true },
            new TeamSeason { Team = mets, Season = 2006, Wins = 97, Losses = 65, MadePlayoffs = true },
            new TeamSeason { Team = nats, Season = 2006, Wins = 71, Losses = 91, MadePlayoffs = false });

        // 2016 Chicago: Bryant played third more than anywhere, though he played the outfield too; Zobrist covered
        // second and left, and Lester and Arrieta split the starts. A reliever and a bench catcher have lines too.
        Hitter(ctx, mlb, cubs, 2016, "Kris Bryant", 155, """{"3B":103,"LF":44,"RF":11,"1B":5}""", 603, 176, 39, 102, popularity: 95);
        Hitter(ctx, mlb, cubs, 2016, "Anthony Rizzo", 155, """{"1B":155}""", 583, 170, 32, 109, popularity: 90);
        Hitter(ctx, mlb, cubs, 2016, "Javier Baez", 142, """{"2B":66,"3B":40,"SS":38}""", 421, 115, 14, 59);
        Hitter(ctx, mlb, cubs, 2016, "Ben Zobrist", 147, """{"2B":118,"LF":40,"RF":2}""", 523, 142, 18, 76);
        Hitter(ctx, mlb, cubs, 2016, "Addison Russell", 151, """{"SS":151}""", 525, 125, 21, 95);
        Hitter(ctx, mlb, cubs, 2016, "Dexter Fowler", 125, """{"CF":123}""", 456, 126, 13, 48);
        Hitter(ctx, mlb, cubs, 2016, "Jason Heyward", 142, """{"RF":136,"CF":6}""", 530, 122, 7, 49);
        Hitter(ctx, mlb, cubs, 2016, "Chris Coghlan", 48, """{"LF":30,"RF":10}""", 136, 34, 3, 14);
        Hitter(ctx, mlb, cubs, 2016, "Jorge Soler", 86, """{"LF":66}""", 227, 54, 12, 31);
        Hitter(ctx, mlb, cubs, 2016, "Miguel Montero", 86, """{"C":78}""", 241, 52, 8, 33);
        Hitter(ctx, mlb, cubs, 2016, "Willson Contreras", 76, """{"C":56,"LF":12}""", 252, 71, 12, 35);
        Pitcher(ctx, mlb, cubs, 2016, "Jon Lester", 32, 32, 19, 5, 0, 609, 57, 197, popularity: 85);
        Pitcher(ctx, mlb, cubs, 2016, "Jake Arrieta", 31, 31, 18, 8, 0, 594, 68, 190);
        Pitcher(ctx, mlb, cubs, 2016, "Hector Rondon", 54, 0, 2, 3, 18, 155, 17, 58);

        // 1981 Montreal made the playoffs but has no lines, so it makes no photo. 2006 New York: a clear nine for practice; 2006 Washington missed the playoffs.
        foreach (var (team, prefix, season) in new[] { (mets, "Mets", 2006), (nats, "Nats", 2006) })
        {
            foreach (var (pos, i) in new[] { "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF" }.Select((p, i) => (p, i)))
                Hitter(ctx, mlb, team, season, $"{prefix} {pos}", 150, $$"""{"{{pos}}":140}""", 550, 150 + i, 20, 80);
            Pitcher(ctx, mlb, team, season, $"{prefix} Ace", 32, 32, 15, 7, 0, 600, 60, 200);
        }
        ctx.SaveChanges();
        foreach (var p in ctx.Players) _ids[p.FullName] = p.Id;
    }

    private static Player NewPlayer(League league, string name, double popularity) => new()
    {
        League = league, ExternalId = name.ToLowerInvariant().Replace(' ', '-'), FirstName = name[..name.IndexOf(' ')],
        LastName = name[(name.IndexOf(' ') + 1)..], FullName = name, NormalizedName = name.ToLowerInvariant(), Popularity = popularity,
        HeadshotUrl = $"https://heads/{name.Replace(' ', '-')}.png",
    };

    private static void Hitter(DbContext ctx, League league, Team team, int season, string name, int games, string fielding,
        int ab, int h, int hr, int rbi, double popularity = 50)
    {
        var player = NewPlayer(league, name, popularity);
        player.SeasonStats.Add(new PlayerSeasonStat
        {
            Season = season, Team = team, IsSeasonTotal = true, Games = games,
            StatsJson = $$"""{"ab":{{ab}},"h":{{h}},"hr":{{hr}},"rbi":{{rbi}},"fpos":{{fielding}}}""",
        });
        ctx.Add(player);
    }

    private static void Pitcher(DbContext ctx, League league, Team team, int season, string name, int games, int starts,
        int w, int l, int sv, int outs, int er, int k, double popularity = 50)
    {
        var player = NewPlayer(league, name, popularity);
        player.SeasonStats.Add(new PlayerSeasonStat
        {
            Season = season, Team = team, IsSeasonTotal = true, Games = games,
            StatsJson = $$"""{"gs":{{starts}},"w":{{w}},"l":{{l}},"sv":{{sv}},"outs":{{outs}},"er":{{er}},"k":{{k}},"fpos":""" + $$"""{"P":{{games}}}""" + "}",
        });
        ctx.Add(player);
    }

    private (GameService Service, PuzzleScheduler Scheduler, Infrastructure.Persistence.TriviaDbContext Ctx) Engine(int seed = 1)
    {
        var ctx = _db.NewContext();
        var calendar = new PuzzleCalendar(_clock);
        var registry = new GameRegistry([new TeamPhotoGame(ctx)],
            Microsoft.Extensions.Options.Options.Create(new GameLeagueOptions { Leagues = "nba,nfl,mlb" }));
        var scheduler = new PuzzleScheduler(ctx, calendar, _clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        return (new GameService(ctx, registry, scheduler, calendar, _clock), scheduler, ctx);
    }

    private async Task<GameState> Guess(int puzzleId, string name)
    {
        var (service, _, ctx) = Engine();
        using (ctx) return (await service.GuessAsync(Slug, puzzleId, _ids[name], Me, default)).Result!;
    }

    [Fact]
    public void The_world_series_list_has_a_winner_and_a_loser_for_every_season_since_1960_but_1994()
    {
        var teams = TeamPhotoGame.WorldSeriesTeams;

        Assert.Equal(130, teams.Count);
        Assert.Equal(Enumerable.Range(1960, 66).Where(y => y != 1994), teams.Select(t => t.Season).Distinct());
        Assert.All(teams.GroupBy(t => t.Season), g => Assert.Equal(["lost", "won"], g.Select(t => t.Finals).Order()));
        Assert.Equal(130, teams.Select(t => t.Key).Distinct().Count());
        // Teams are named by MLBAM id, which carries over a move or a new name.
        Assert.All(teams, t => Assert.True(int.TryParse(t.Team, out var id) && id is >= 108 and <= 158, t.Key));
        Assert.Contains(teams, t => t.Key == "2016-112" && t.Finals == "won");
        Assert.Contains(teams, t => t.Key == "2019-120" && t.Finals == "won");
        Assert.Contains(teams, t => t.Key == "1969-121" && t.Finals == "won");
    }

    [Fact]
    public void Each_place_goes_to_the_most_games_there_one_place_a_player()
    {
        TeamPhotoGame.BaseballRosterLine L(int id, int games, Dictionary<string, int> fielding, double starts = 0) =>
            new(id, games, fielding, new Dictionary<string, double> { ["gs"] = starts });

        var lines = new[]
        {
            L(1, 150, new() { ["C"] = 130 }), L(2, 150, new() { ["1B"] = 120, ["LF"] = 30 }), L(3, 150, new() { ["2B"] = 140 }),
            L(4, 150, new() { ["3B"] = 100, ["1B"] = 45 }), L(5, 150, new() { ["SS"] = 150 }), L(6, 150, new() { ["LF"] = 90 }),
            L(7, 150, new() { ["CF"] = 145 }), L(8, 150, new() { ["RF"] = 140 }), L(9, 32, new() { ["P"] = 32 }, 32),
            L(10, 30, new() { ["P"] = 30 }, 30), L(11, 60, new() { ["1B"] = 60, ["DH"] = 80 }),
        };
        var nine = TeamPhotoGame.BaseballLineup(lines, null)!;
        Assert.Equal(
            ["6 LF back", "7 CF back", "8 RF back", "4 3B middle", "5 SS middle", "3 2B middle", "2 1B middle", "9 SP front", "1 C front"],
            nine.Select(n => $"{n.Line.PlayerId} {n.Position} {n.Row}"));
        Assert.Equal(10, TeamPhotoGame.BaseballLineup(lines, new Dictionary<int, int> { [9] = 10 })![7].Line.PlayerId);
        Assert.True(TeamPhotoGame.IsClearBaseball(lines, 162));
        // A left fielder in 90 of 162 games is clear; in 90 of 200 he is not. No catcher at all makes no photo.
        Assert.False(TeamPhotoGame.IsClearBaseball(lines, 200));
        Assert.Null(TeamPhotoGame.BaseballLineup([.. lines.Where(l => l.PlayerId != 1)], null));

        // A pitcher's games are his games pitched, though his row counts two he only pinch-ran in.
        var ace = new TeamPhotoGame.BaseballRosterLine(9, 34, new Dictionary<string, int>(), new Dictionary<string, double> { ["gs"] = 32, ["pg"] = 32 });
        Assert.Equal((32, 150), (TeamPhotoGame.SlotGames("SP", ace), TeamPhotoGame.SlotGames("SS", lines[4])));
    }

    [Fact]
    public async Task The_photo_stands_the_nine_as_on_the_field_and_names_no_one()
    {
        var (service, _, ctx) = Engine();
        GameState state;
        using (ctx) state = (await service.GetDailyAsync(Slug, Me, default, "mlb")).Result!;
        var payload = state.Payload;

        Assert.Equal(("mlb", 4), (state.League, state.MaxGuesses));
        Assert.Equal((2016, 103, 58, "won", "Cubs"),
            (payload["season"]!.GetValue<int>(), payload["wins"]!.GetValue<int>(), payload["losses"]!.GetValue<int>(),
                payload["finals"]!.GetValue<string>(), payload["team"]!["name"]!.GetValue<string>()));
        var slots = payload["slots"]!.AsArray();
        Assert.Equal(["LF", "CF", "RF", "3B", "SS", "2B", "1B", "SP", "C"], slots.Select(s => s!["position"]!.GetValue<string>()));
        Assert.Equal(["back", "back", "back", "middle", "middle", "middle", "middle", "front", "front"], slots.Select(s => s!["row"]!.GetValue<string>()));
        Assert.Equal((39, 102, 0.292), (slots[3]!["stats"]!["hr"]!.GetValue<int>(), slots[3]!["stats"]!["rbi"]!.GetValue<int>(), slots[3]!["stats"]!["avg"]!.GetValue<double>()));
        Assert.Equal((19, 5, 2.53, 197), (slots[7]!["stats"]!["w"]!.GetValue<int>(), slots[7]!["stats"]!["l"]!.GetValue<int>(),
            slots[7]!["stats"]!["era"]!.GetValue<double>(), slots[7]!["stats"]!["k"]!.GetValue<int>()));
        Assert.DoesNotContain("Bryant", payload.ToJsonString());
        // Keyed like the World Series list, so tomorrow's daily knows this team-season was used.
        await using (var db = _db.NewContext())
            Assert.Equal("2016-112", JsonNode.Parse((await db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId)).SecretJson)!["key"]!.GetValue<string>());

        // Bryant is at third though he played left too; Zobrist then takes second, Baez is on the bench and costs nothing.
        var baez = await Guess(state.PuzzleId, "Javier Baez");
        Assert.Equal(true, baez.Guesses[^1].Feedback!["bench"]!.GetValue<bool>());
        foreach (var name in new[] { "Jorge Soler", "Dexter Fowler", "Jason Heyward", "Kris Bryant", "Addison Russell", "Ben Zobrist", "Anthony Rizzo", "Jon Lester" })
            state = await Guess(state.PuzzleId, name);
        Assert.Equal(GameStatus.Playing, state.Status);
        state = await Guess(state.PuzzleId, "Miguel Montero");
        Assert.Equal(GameStatus.Won, state.Status);
    }

    [Fact]
    public async Task Practice_is_a_clear_playoff_team_that_did_not_reach_the_world_series()
    {
        var (service, _, ctx) = Engine();
        using (ctx)
        {
            var practice = (await service.StartPracticeAsync(Slug, default, "mlb")).Result!;
            Assert.Equal(("Mets", 2006, System.Text.Json.JsonValueKind.Null),
                (practice.Payload["team"]!["name"]!.GetValue<string>(), practice.Payload["season"]!.GetValue<int>(), practice.Payload["finals"]?.GetValueKind() ?? System.Text.Json.JsonValueKind.Null));
        }
    }

    public void Dispose() => _db.Dispose();
}

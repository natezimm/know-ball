using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games;
using KnowBall.Application.Games.TeamPhoto;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

/// <summary>The NHL edition of Team Photo: the starting six of a Stanley Cup Final team.</summary>
public class TeamPhotoNhlTests : IDisposable
{
    private const string Me = "player-nhl-photo-1";
    private const string Slug = TeamPhotoGame.GameSlug;

    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(GameWorld.Start);
    private readonly Dictionary<string, int> _ids = [];

    public TeamPhotoNhlTests()
    {
        using var ctx = _db.NewContext();
        var nhl = new League { Code = "nhl", Name = "NHL", Sport = "Hockey" };
        Team Team(string abbr, string city, string name) =>
            new() { League = nhl, ExternalId = abbr, Abbreviation = abbr, City = city, Name = name, LogoUrl = $"https://logos/{abbr}.svg" };
        var fla = Team("FLA", "Florida", "Panthers");
        var tor = Team("TOR", "Toronto", "Maple Leafs");
        var buf = Team("BUF", "Buffalo", "Sabres");
        ctx.Teams.AddRange(fla, tor, buf);
        ctx.TeamSeasons.AddRange(
            new TeamSeason { Team = fla, Season = 2023, Wins = 52, Losses = 24, OvertimeLosses = 6, MadePlayoffs = true },
            new TeamSeason { Team = tor, Season = 2022, Wins = 50, Losses = 21, OvertimeLosses = 11, MadePlayoffs = true },
            new TeamSeason { Team = buf, Season = 2022, Wins = 42, Losses = 33, OvertimeLosses = 7, MadePlayoffs = false });

        // 2023 Florida: the top scorer at each forward spot, two defencemen, a starter in goal, and a bench.
        Add(ctx, nhl, fla, 2023, "Matthew Tkachuk", "LW", 80, """{"g":26,"a":62,"pts":88}""", popularity: 90);
        Add(ctx, nhl, fla, 2023, "Aleksander Barkov", "C", 73, """{"g":23,"a":57,"pts":80}""", popularity: 85);
        Add(ctx, nhl, fla, 2023, "Sam Reinhart", "C", 82, """{"g":57,"a":37,"pts":94}""", popularity: 80);
        Add(ctx, nhl, fla, 2023, "Carter Verhaeghe", "LW", 76, """{"g":34,"a":38,"pts":72}""");
        Add(ctx, nhl, fla, 2023, "Gustav Forsling", "D", 79, """{"g":10,"a":29,"pts":39}""");
        Add(ctx, nhl, fla, 2023, "Brandon Montour", "D", 66, """{"g":8,"a":25,"pts":33}""");
        Add(ctx, nhl, fla, 2023, "Aaron Ekblad", "D", 51, """{"g":5,"a":20,"pts":25}""");
        Add(ctx, nhl, fla, 2023, "Sergei Bobrovsky", "G", 58, """{"gs":58,"w":36,"gaa":2.37,"svp":0.915}""");
        Add(ctx, nhl, fla, 2023, "Anthony Stolarz", "G", 27, """{"gs":24,"w":16,"gaa":2.03,"svp":0.925}""");

        // Practice: a playoff team with a clear six, and one that missed the playoffs.
        foreach (var (team, prefix) in new[] { (tor, "Leafs"), (buf, "Sabres") })
        {
            Add(ctx, nhl, team, 2022, $"{prefix} Left", "LW", 80, """{"pts":60}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Centre", "C", 80, """{"pts":90}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Right", "RW", 80, """{"pts":70}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Rearguard", "D", 80, """{"pts":40}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Blueliner", "D", 80, """{"pts":30}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Keeper", "G", 60, """{"gs":60,"w":35}""");
            Add(ctx, nhl, team, 2022, $"{prefix} Backup", "G", 22, """{"gs":22,"w":10}""");
        }
        ctx.SaveChanges();
        foreach (var p in ctx.Players) _ids[p.FullName] = p.Id;
    }

    private static void Add(Microsoft.EntityFrameworkCore.DbContext ctx, League league, Team team, int season, string name, string pos, int games,
        string stats, double popularity = 50)
    {
        var space = name.IndexOf(' ');
        var player = new Player
        {
            League = league, ExternalId = name.ToLowerInvariant().Replace(' ', '-'), FirstName = name[..space], LastName = name[(space + 1)..],
            FullName = name, NormalizedName = name.ToLowerInvariant(), Position = pos, Popularity = popularity,
        };
        player.SeasonStats.Add(new PlayerSeasonStat { Season = season, Team = team, IsSeasonTotal = true, Games = games, StatsJson = stats });
        player.Stints.Add(new PlayerTeamStint { Team = team, StartSeason = season, EndSeason = season, Seasons = 1 });
        ctx.Add(player);
    }

    private (GameService Service, PuzzleScheduler Scheduler, Infrastructure.Persistence.TriviaDbContext Ctx) Engine(int seed = 1)
    {
        var ctx = _db.NewContext();
        var calendar = new PuzzleCalendar(_clock);
        var registry = new GameRegistry([new TeamPhotoGame(ctx)],
            Microsoft.Extensions.Options.Options.Create(new GameLeagueOptions { Leagues = "nba,nfl,nhl" }));
        var scheduler = new PuzzleScheduler(ctx, calendar, _clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        return (new GameService(ctx, registry, scheduler, calendar, _clock), scheduler, ctx);
    }

    [Fact]
    public void The_stanley_cup_list_has_a_winner_and_a_loser_for_every_season_since_1979_but_the_lockout()
    {
        var teams = TeamPhotoGame.StanleyCupTeams;

        Assert.Equal(90, teams.Count);
        Assert.Equal(Enumerable.Range(1979, 46).Where(s => s != 2004), teams.Select(t => t.Season).Distinct());
        Assert.All(teams.GroupBy(t => t.Season), g => Assert.Equal(["lost", "won"], g.Select(t => t.Finals).Order()));
        Assert.Equal(90, teams.Select(t => t.Key).Distinct().Count());
        Assert.Contains(teams, t => t.Key == "1979-NYI" && t.Finals == "won");
        Assert.Contains(teams, t => t.Key == "1990-MNS" && t.Finals == "lost");
        Assert.Contains(teams, t => t.Key == "2024-FLA" && t.Finals == "won");
    }

    [Fact]
    public void The_six_lead_their_position_and_a_missing_wing_is_filled_by_the_next_forward()
    {
        TeamPhotoGame.FootballRosterLine L(int id, string pos, int games, double pts, double gs = 0) =>
            new(id, pos, games, new Dictionary<string, double> { ["pts"] = pts, ["gs"] = gs });

        var lines = new[]
        {
            L(1, "C", 82, 90), L(2, "C", 82, 70), L(3, "LW", 82, 60), L(4, "D", 82, 40), L(5, "D", 82, 50), L(6, "D", 82, 20),
            L(7, "G", 60, 0, 58), L(8, "G", 30, 0, 24),
        };
        var six = TeamPhotoGame.HockeyLineup(lines, null)!;
        // No right wing: the second centre stands there. Defencemen either side of the goalie in front.
        Assert.Equal(["3 LW back", "1 C back", "2 RW back", "5 D front", "7 G front", "4 D front"], six.Select(f => $"{f.Line.PlayerId} {f.Position} {f.Row}"));
        Assert.True(TeamPhotoGame.IsClearHockey(lines));
        Assert.False(TeamPhotoGame.IsClearHockey([.. lines, L(9, "G", 50, 0, 45)]));
        Assert.Null(TeamPhotoGame.HockeyLineup(lines.Where(l => l.Position != "G").ToList(), null));
    }

    [Fact]
    public async Task A_stanley_cup_final_team_is_named_six_strong_with_its_overtime_losses()
    {
        var (service, _, ctx) = Engine();
        using (ctx)
        {
            var state = (await service.GetDailyAsync(Slug, Me, default, "nhl")).Result!;
            Assert.Equal("nhl", state.League);
            var payload = state.Payload;
            Assert.Equal(("FLA", 2023, "won"), (payload["team"]!["abbreviation"]!.GetValue<string>(), payload["season"]!.GetValue<int>(), payload["finals"]!.GetValue<string>()));
            Assert.Equal((52, 24, 6), (payload["wins"]!.GetValue<int>(), payload["losses"]!.GetValue<int>(), payload["overtimeLosses"]!.GetValue<int>()));
            Assert.Equal(["LW", "C", "RW", "D", "G", "D"], payload["slots"]!.AsArray().Select(s => s!["position"]!.GetValue<string>()));
            Assert.Equal(0.915, payload["slots"]![4]!["stats"]!["svp"]!.GetValue<double>());

            // A bench player costs nothing.
            var bench = (await service.GuessAsync(Slug, state.PuzzleId, _ids["Aaron Ekblad"], Me, default)).Result!;
            Assert.Equal(GameStatus.Playing, bench.Status);
            Assert.True(bench.Guesses[^1].Feedback!["bench"]!.GetValue<bool>());

            GameState last = bench;
            foreach (var name in new[] { "Matthew Tkachuk", "Sam Reinhart", "Aleksander Barkov", "Gustav Forsling", "Sergei Bobrovsky", "Brandon Montour" })
                last = (await service.GuessAsync(Slug, state.PuzzleId, _ids[name], Me, default)).Result!;
            Assert.Equal(GameStatus.Won, last.Status);
            // Barkov is a centre but Reinhart outscored him, so Barkov takes the empty right wing.
            Assert.Equal(["Matthew Tkachuk", "Sam Reinhart", "Aleksander Barkov", "Gustav Forsling", "Sergei Bobrovsky", "Brandon Montour"],
                last.Answer!["slots"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
        }
    }

    [Fact]
    public async Task Practice_uses_a_playoff_team_with_a_clear_six()
    {
        var (service, _, ctx) = Engine();
        using (ctx)
        {
            var practice = (await service.StartPracticeAsync(Slug, default, "nhl")).Result!;
            Assert.Equal(("nhl", "TOR", 2022, (string?)null),
                (practice.League, practice.Payload["team"]!["abbreviation"]!.GetValue<string>(), practice.Payload["season"]!.GetValue<int>(),
                    practice.Payload["finals"]?.GetValue<string>()));
        }
    }

    public void Dispose() => _db.Dispose();
}

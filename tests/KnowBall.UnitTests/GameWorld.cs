using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Games;
using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Text;
using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.UnitTests;

/// <summary>
/// A small league for the game tests: a few teams (one of them defunct), players with every
/// clue filled in, and the engine wired up the way the API wires it.
/// </summary>
public sealed class GameWorld : IDisposable
{
    /// <summary>10:00 in New York on 13 October 2026.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 14, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = new(2026, 10, 13);

    public TestDb Db { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Start);
    public League League { get; }
    public Dictionary<string, Team> Teams { get; } = [];
    public Dictionary<string, Player> Players { get; } = [];

    public GameWorld()
    {
        using var ctx = Db.NewContext();
        League = new League { Code = "nba", Name = "NBA", Sport = "Basketball" };
        ctx.Leagues.Add(League);

        Team T(string abbr, string city, string name, string conference, string division) =>
            Teams[abbr] = new Team { League = League, ExternalId = abbr, Abbreviation = abbr, City = city, Name = name, Conference = conference, Division = division, LogoUrl = $"https://logos/{abbr}.svg" };

        T("BOS", "Boston", "Celtics", "East", "Atlantic");
        T("NYK", "New York", "Knicks", "East", "Atlantic");
        T("DEN", "Denver", "Nuggets", "West", "Northwest");
        T("OKC", "Oklahoma City", "Thunder", "West", "Northwest");
        T("MIA", "Miami", "Heat", "East", "Southeast");
        T("GSW", "Golden State", "Warriors", "West", "Pacific");
        Teams["SEA"] = new Team { League = League, ExternalId = "bref-SEA", Abbreviation = "SEA", City = "Seattle", Name = "SuperSonics", Franchise = Teams["OKC"], ActiveFrom = 1967, ActiveTo = 2007 };
        ctx.Teams.AddRange(Teams.Values);

        AddPlayer("Jayson Tatum", "BOS", "F-G", 80, "1998-03-03", "0", 60, [("BOS", 2017, null)]);
        AddPlayer("Jalen Brunson", "NYK", "G", 74, "1996-08-31", "11", 55, [("NYK", 2022, null)]);
        AddPlayer("Nikola Jokić", "DEN", "C", 83, "1995-02-19", "15", 95, [("DEN", 2015, null)]);
        AddPlayer("Jaylen Brown", "BOS", "G-F", 78, "1996-10-24", "7", 50, [("BOS", 2016, null)]);
        // Two players who took the same road: Seattle/Oklahoma City, then Golden State, then Miami.
        AddPlayer("Kevin Durant", "MIA", "F-G", 83, "1988-09-29", "35", 99, [("SEA", 2007, 2007), ("OKC", 2008, 2015), ("GSW", 2016, 2018), ("MIA", 2019, null)], college: "Texas", draft: (2007, 2));
        AddPlayer("Same Road", null, "G", 75, "1985-05-05", null, 30, [("OKC", 2010, 2012), ("GSW", 2013, 2014), ("MIA", 2015, 2016)], active: false, country: "France");
        AddPlayer("Other Road", null, "C", 84, "1980-01-01", null, 40, [("DEN", 2001, 2004), ("NYK", 2005, 2008), ("MIA", 2009, 2010)], active: false);
        AddPlayer("One Stop", null, "G", 73, "1975-01-01", null, 80, [("BOS", 1996, 2008)], active: false);

        // Season lines for the card game. Durant's 2019 season was split by a trade, so its combined row has no team.
        AddSeasons("Kevin Durant",
            (2007, "SEA", 80, 20.3, 4.4, 2.4), (2008, "OKC", 74, 25.3, 6.5, 2.8), (2013, "OKC", 81, 32.0, 7.4, 5.5),
            (2016, "GSW", 62, 25.1, 8.3, 4.8), (2019, null, 60, 26.0, 7.0, 5.0), (2020, "MIA", 70, 27.0, 7.1, 5.6));
        Players["Kevin Durant"].SeasonStats.AddRange(
        [
            new PlayerSeasonStat { Season = 2019, Team = Teams["GSW"], IsSeasonTotal = false, Games = 30, PointsPerGame = 25.0 },
            new PlayerSeasonStat { Season = 2019, Team = Teams["MIA"], IsSeasonTotal = false, Games = 30, PointsPerGame = 27.0 },
        ]);
        Players["Kevin Durant"].Awards.AddRange(
        [
            new Award { Season = 2007, Type = "ROY" },
            new Award { Season = 2013, Type = "MVP" },
            new Award { Season = 2013, Type = "AllStar" },
            new Award { Season = 2016, Type = "AllStar" },
            new Award { Season = 2016, Type = "AllNBA1" },
        ]);
        AddSeasons("Nikola Jokić",
            (2015, "DEN", 80, 10.0, 7.0, 2.4), (2016, "DEN", 73, 16.7, 9.8, 4.9), (2017, "DEN", 75, 18.5, 10.7, 6.1),
            (2018, "DEN", 80, 20.1, 10.8, 7.3), (2019, "DEN", 73, 19.9, 9.7, 7.0));
        AddSeasons("One Stop",
            (1996, "BOS", 82, 12.0, 3.0, 5.0), (1997, "BOS", 82, 14.0, 3.1, 6.0), (1998, "BOS", 50, 15.0, 3.2, 6.5),
            (1999, "BOS", 82, 16.0, 3.3, 7.0), (2000, "BOS", 82, 13.0, 3.0, 6.0));
        // Too few seasons to make a card.
        AddSeasons("Same Road", (2010, "OKC", 60, 8.0, 2.0, 3.0), (2013, "GSW", 50, 9.0, 2.0, 3.0), (2015, "MIA", 40, 7.0, 2.0, 2.0));

        ctx.Players.AddRange(Players.Values);
        ctx.SaveChanges();
    }

    private void AddSeasons(string name, params (int Season, string? Team, int Games, double Points, double Rebounds, double Assists)[] lines)
    {
        foreach (var (season, team, games, points, rebounds, assists) in lines)
        {
            Players[name].SeasonStats.Add(new PlayerSeasonStat
            {
                Season = season,
                Team = team is null ? null : Teams[team],
                IsSeasonTotal = true,
                Games = games,
                PointsPerGame = points,
                ReboundsPerGame = rebounds,
                AssistsPerGame = assists,
            });
        }
    }

    private void AddPlayer(
        string name, string? team, string position, int height, string born, string? number, double popularity,
        (string Team, int Start, int? End)[] stints, bool active = true, string? college = null, string? country = null, (int Year, int Pick)? draft = null)
    {
        var space = name.IndexOf(' ');
        var player = new Player
        {
            League = League,
            ExternalId = NameNormalizer.Normalize(name).Replace(' ', '-'),
            FirstName = name[..space],
            LastName = name[(space + 1)..],
            FullName = name,
            NormalizedName = NameNormalizer.Normalize(name),
            CurrentTeam = team is null ? null : Teams[team],
            Position = position,
            HeightInches = height,
            BirthDate = DateOnly.Parse(born),
            JerseyNumber = number,
            Popularity = popularity,
            IsActive = active,
            College = college,
            Country = country,
            DraftYear = draft?.Year,
            DraftNumber = draft?.Pick,
            FromYear = stints[0].Start,
            ToYear = active ? 2025 : stints[^1].End,
            HeadshotUrl = $"https://heads/{NameNormalizer.Normalize(name).Replace(' ', '-')}.png",
        };
        for (var i = 0; i < stints.Length; i++)
        {
            var (abbr, start, end) = stints[i];
            player.Stints.Add(new PlayerTeamStint { Team = Teams[abbr], StartSeason = start, EndSeason = end, Seasons = (end ?? 2025) - start + 1, Order = i + 1 });
        }
        Players[name] = player;
    }

    public int Id(string name) => Players[name].Id;

    /// <summary>The engine over a fresh context, like one web request. <paramref name="seed"/> makes puzzle choice repeatable.</summary>
    public Engine NewEngine(int seed = 1)
    {
        var ctx = Db.NewContext();
        var calendar = new PuzzleCalendar(Clock);
        var registry = new GameRegistry([new ScoutingReportGame(ctx), new CareerPathGame(ctx), new BackOfTheCardGame(ctx)]);
        var scheduler = new PuzzleScheduler(ctx, calendar, Clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        var service = new GameService(ctx, registry, scheduler, calendar, Clock);
        return new Engine(ctx, registry, scheduler, service, calendar);
    }

    public void Dispose() => Db.Dispose();

    public sealed record Engine(
        TriviaDbContext Db, GameRegistry Games, PuzzleScheduler Scheduler, GameService Service, PuzzleCalendar Calendar) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }
}

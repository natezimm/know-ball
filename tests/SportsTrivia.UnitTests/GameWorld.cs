using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SportsTrivia.Application.Games;
using SportsTrivia.Application.Games.CareerPath;
using SportsTrivia.Application.Games.ScoutingReport;
using SportsTrivia.Application.Games.Silhouette;
using SportsTrivia.Application.Text;
using SportsTrivia.Domain;
using SportsTrivia.Infrastructure.Persistence;

namespace SportsTrivia.UnitTests;

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
        ctx.Players.AddRange(Players.Values);
        ctx.SaveChanges();
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
    public Engine NewEngine(int seed = 1, IImageStore? images = null, ISilhouetteRenderer? renderer = null)
    {
        var ctx = Db.NewContext();
        var calendar = new PuzzleCalendar(Clock);
        var whosThat = new WhosThatGame();
        var registry = new GameRegistry([new ScoutingReportGame(ctx), new CareerPathGame(ctx), whosThat]);
        var scheduler = new PuzzleScheduler(ctx, calendar, Clock, NullLogger<PuzzleScheduler>.Instance) { Random = new Random(seed) };
        var service = new GameService(ctx, registry, scheduler, calendar, Clock);
        var curated = new CuratedPuzzleService(ctx, calendar, scheduler, whosThat, images ?? new FakeImages(), renderer ?? new FakeRenderer(), Clock);
        return new Engine(ctx, registry, scheduler, service, curated, calendar);
    }

    public void Dispose() => Db.Dispose();

    public sealed record Engine(
        TriviaDbContext Db, GameRegistry Games, PuzzleScheduler Scheduler, GameService Service, CuratedPuzzleService Curated, PuzzleCalendar Calendar) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    public sealed class FakeImages : IImageStore
    {
        public List<string> Saved { get; } = [];
        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken)
        {
            var url = $"/media/img{Saved.Count + 1}{extension}";
            Saved.Add(url);
            return Task.FromResult(url);
        }

        public Task DeleteAsync(string url, CancellationToken cancellationToken)
        {
            Deleted.Add(url);
            return Task.CompletedTask;
        }
    }

    public sealed class FakeRenderer : ISilhouetteRenderer
    {
        public string? RejectWith { get; set; }

        public Task<SilhouetteImages> RenderAsync(Stream photo, Stream cutout, CancellationToken cancellationToken) =>
            RejectWith is null
                ? Task.FromResult(new SilhouetteImages([1], [2], [3]))
                : throw new InvalidImageException(RejectWith);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Import;
using KnowBall.Domain;

namespace KnowBall.UnitTests;

public class HistoryImportServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));

    private static readonly HistoryTeam Thunder = new("OKC", new SourceTeam("okc-id", "Oklahoma City", "Thunder", "OKC", "West", "Northwest"), 2008, null, null);
    private static readonly HistoryTeam Sonics = new("SEA", new SourceTeam("bref-SEA", "Seattle", "SuperSonics", "SEA"), 1967, 2007, "OKC");
    private static readonly HistoryTeam Warriors = new("GSW", new SourceTeam("gsw-id", "Golden State", "Warriors", "GSW", "West", "Pacific"), 1971, null, null);
    private static readonly HistoryTeam Nets = new("BRK", new SourceTeam("bkn-id", "Brooklyn", "Nets", "BKN", "East", "Atlantic"), 2012, null, null);
    private static readonly HistoryTeam[] Teams = [Thunder, Sonics, Warriors, Nets];

    private static HistorySeason Line(int season, string? team, double points, int games = 70, bool total = true) =>
        new(season, team, total, games, 34, points, 7, 5);

    private static HistoryPlayer Durant(params HistorySeason[] extra) => new(
        "duranke01", "Kevin", "Durant", "F-G", 83, 240, new DateOnly(1988, 9, 29), "Texas", 2007, 2025, false, 2007, 2,
        [Line(2007, "SEA", 20.3), Line(2008, "OKC", 25.3), Line(2015, "OKC", 28.2), Line(2016, "GSW", 25.1), Line(2018, "GSW", 26), .. extra],
        [new HistoryAward(2013, AwardTypes.Mvp), new HistoryAward(2009, AwardTypes.AllStar), new HistoryAward(2013, AwardTypes.AllLeague + "1")]);

    private static HistoryPlayer Retired() => new(
        "oldgu01", "Old", "Guard", "G", 74, 180, new DateOnly(1970, 1, 1), null, 1992, 1999, true, null, null,
        [Line(1992, "SEA", 9), Line(1999, "SEA", 4)], []);

    private static HistorySnapshot Snapshot(params HistoryPlayer[] players) => new("nba", "bref", 2025, Teams, players);

    private async Task<ImportRun> Import(HistorySnapshot snapshot)
    {
        await using var ctx = _db.NewContext();
        return await new HistoryImportService(ctx, _clock, NullLogger<HistoryImportService>.Instance).RunAsync(new FakeHistory(snapshot));
    }

    private async Task<ImportRun> ImportRoster(SourceSnapshot snapshot)
    {
        await using var ctx = _db.NewContext();
        return await new PlayerImportService(ctx, _clock, NullLogger<PlayerImportService>.Instance).RunAsync(new FakeRoster(snapshot));
    }

    private async Task<Player> Load(string name)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Players
            .Include(p => p.CurrentTeam).Include(p => p.ExternalIds).Include(p => p.Awards).Include(p => p.SeasonStats)
            .Include(p => p.Stints.OrderBy(s => s.Order)).ThenInclude(s => s.Team)
            .AsSplitQuery()
            .SingleAsync(p => p.FullName == name);
    }

    private static string Path(Player p) =>
        string.Join(", ", p.Stints.Select(s => $"{s.Team!.Abbreviation} {s.StartSeason}-{s.EndSeason?.ToString() ?? "now"} ({s.Seasons})"));

    [Fact]
    public async Task First_load_creates_players_with_facts_ids_awards_and_season_lines()
    {
        var run = await Import(Snapshot(Durant(), Retired()));

        Assert.Equal(ImportStatus.Succeeded, run.Status);
        Assert.Equal(("bref", "nba", 2025), (run.Source, run.LeagueCode, run.Season));
        Assert.Equal((4, 2, 0), (run.TeamsUpserted, run.PlayersInserted, run.PlayersUpdated));

        var kd = await Load("Kevin Durant");
        Assert.Equal(("duranke01", "kevin durant", "F-G", 83), (kd.ExternalId, kd.NormalizedName, kd.Position, kd.HeightInches));
        Assert.Equal((new DateOnly(1988, 9, 29), "Texas", 2007, 2025, 2007, 2), (kd.BirthDate, kd.College, kd.FromYear, kd.ToYear, kd.DraftYear, kd.DraftNumber));
        Assert.Equal(("bref", "duranke01"), (kd.ExternalIds.Single().Source, kd.ExternalIds.Single().Value));
        Assert.Equal(3, kd.Awards.Count);
        Assert.Equal(5, kd.SeasonStats.Count);
        Assert.All(kd.SeasonStats, s => Assert.True(s.IsSeasonTotal));
        Assert.True((await Load("Old Guard")).IsHallOfFame);
    }

    [Fact]
    public async Task Stints_follow_the_season_lines_and_the_last_one_stays_open_for_a_current_player()
    {
        await Import(Snapshot(Durant(Line(2025, "GSW", 24)), Retired()));

        var kd = await Load("Kevin Durant");
        Assert.Equal("SEA 2007-2007 (1), OKC 2008-2015 (2), GSW 2016-now (3)", Path(kd));
        Assert.True(kd.IsActive);
        Assert.Equal("GSW", kd.CurrentTeam!.Abbreviation);

        var old = await Load("Old Guard");
        Assert.Equal("SEA 1992-1999 (2)", Path(old));
        Assert.False(old.IsActive);
        Assert.Null(old.CurrentTeam);
    }

    [Fact]
    public async Task Old_team_identities_point_at_the_franchise_they_became()
    {
        await Import(Snapshot(Durant()));

        await using var ctx = _db.NewContext();
        var teams = await ctx.Teams.Include(t => t.Franchise).ToDictionaryAsync(t => t.Abbreviation);
        Assert.Equal("OKC", teams["SEA"].Franchise!.Abbreviation);
        Assert.Equal((1967, 2007), (teams["SEA"].ActiveFrom, teams["SEA"].ActiveTo));
        Assert.Null(teams["OKC"].FranchiseId);
        Assert.Null(teams["OKC"].ActiveTo);
    }

    [Fact]
    public async Task Loading_the_same_record_again_changes_nothing()
    {
        await Import(Snapshot(Durant(Line(2025, "GSW", 24)), Retired()));
        var second = await Import(Snapshot(Durant(Line(2025, "GSW", 24)), Retired()));

        Assert.Equal(ImportStatus.Succeeded, second.Status);
        Assert.Equal((0, 0), (second.PlayersInserted, second.PlayersUpdated));

        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.Players.CountAsync());
        Assert.Equal(4, await ctx.PlayerTeamStints.CountAsync());
        Assert.Equal(8, await ctx.PlayerSeasonStats.CountAsync());
    }

    [Fact]
    public async Task A_split_season_keeps_its_team_lines_and_its_combined_line()
    {
        var traded = Durant(Line(2022, null, 29, 47), Line(2022, "BRK", 29.7, 39, total: false), Line(2022, "GSW", 26, 8, total: false));
        await Import(Snapshot(traded));

        var season = (await Load("Kevin Durant")).SeasonStats.Where(s => s.Season == 2022).OrderBy(s => s.TeamId ?? 0).ToList();
        Assert.Equal(3, season.Count);
        Assert.Single(season, s => s.IsSeasonTotal && s.TeamId is null && s.Games == 47);
    }

    [Fact]
    public async Task A_better_career_scores_higher_and_a_current_scorer_gets_credit_for_now()
    {
        await Import(Snapshot(Durant(Line(2025, "GSW", 24)), Retired()));

        var kd = (await Load("Kevin Durant")).Popularity;
        var old = (await Load("Old Guard")).Popularity;
        Assert.True(kd > 40, $"Durant scored {kd}");
        Assert.InRange(old, 10, 15); // Hall of Fame bonus plus a few hundred points
    }

    [Fact]
    public async Task The_record_fills_in_a_player_the_roster_feed_created_first()
    {
        // Opening night: the feed knows a rookie the record has not caught up with.
        var feed = new SourceSnapshot("nba", 2026,
            [Warriors.Team, Nets.Team],
            [new SourcePlayer("espn-1", "Kevin", "Durant", "bkn-id", Position: "F", JerseyNumber: "7", BirthDate: new DateOnly(1988, 9, 29))],
            IdSource: "espn", InSeason: false, Supplement: true);
        await ImportRoster(feed);

        var run = await Import(Snapshot(Durant(Line(2025, "GSW", 24))));

        Assert.Equal((0, 1), (run.PlayersInserted, run.PlayersUpdated));
        var kd = await Load("Kevin Durant");
        Assert.Equal(["bref", "espn"], kd.ExternalIds.Select(x => x.Source).Order());
        Assert.Equal("F-G", kd.Position);       // the record's position replaces the feed's
        Assert.Equal("7", kd.JerseyNumber);     // the feed's number is kept
        Assert.Equal("BKN", kd.CurrentTeam!.Abbreviation);
        // The record ends with Golden State; the feed says Brooklyn now, so that spell is added and left open.
        Assert.Equal("SEA 2007-2007 (1), OKC 2008-2015 (2), GSW 2016-2025 (3), BKN 2026-now (0)", Path(kd));
    }

    [Fact]
    public async Task The_roster_feed_layered_on_the_record_moves_players_without_rewriting_their_facts()
    {
        await Import(Snapshot(Durant(Line(2025, "GSW", 24)), Retired()));

        var feed = new SourceSnapshot("nba", 2026,
            [Warriors.Team, Nets.Team],
            [new SourcePlayer("espn-1", "Kevin", "Durant", "bkn-id", Position: "F", HeightInches: 82, JerseyNumber: "7",
                Country: "USA", BirthDate: new DateOnly(1988, 9, 29), HeadshotUrl: "https://espn/kd.png")],
            IdSource: "espn", InSeason: false, Supplement: true);
        var run = await ImportRoster(feed);

        Assert.Equal((0, 1, 1, 0), (run.PlayersInserted, run.PlayersUpdated, run.TeamChanges, run.PlayersDeactivated));
        var kd = await Load("Kevin Durant");
        Assert.Equal(("F-G", 83), (kd.Position, kd.HeightInches));                 // history's facts stand
        Assert.Equal(("7", "USA", "https://espn/kd.png"), (kd.JerseyNumber, kd.Country, kd.HeadshotUrl)); // gaps are filled
        Assert.True(kd.Popularity > 40);                                            // and so does its popularity
        // A move seen before the season starts ended the old spell with last season.
        Assert.Equal("SEA 2007-2007 (1), OKC 2008-2015 (2), GSW 2016-2025 (3), BKN 2026-now (0)", Path(kd));

        var again = await ImportRoster(feed);
        Assert.Equal((0, 0, 0), (again.PlayersInserted, again.PlayersUpdated, again.TeamChanges));
    }

    [Fact]
    public async Task A_player_back_with_his_last_team_after_a_year_out_carries_on_the_same_spell()
    {
        // Out injured for all of 2025-26, so the record stops at 2018 and he is not marked active.
        var injured = Durant() with { ToYear = 2018 };
        await Import(Snapshot(injured));
        Assert.False((await Load("Kevin Durant")).IsActive);

        await ImportRoster(new SourceSnapshot("nba", 2026, [Warriors.Team],
            [new SourcePlayer("espn-1", "Kevin", "Durant", "gsw-id", BirthDate: new DateOnly(1988, 9, 29))],
            IdSource: "espn", InSeason: false, Supplement: true));

        var kd = await Load("Kevin Durant");
        Assert.True(kd.IsActive);
        Assert.Equal("SEA 2007-2007 (1), OKC 2008-2015 (2), GSW 2016-now (2)", Path(kd));

        // And the record, loaded again, agrees.
        await Import(Snapshot(injured));
        Assert.Equal("SEA 2007-2007 (1), OKC 2008-2015 (2), GSW 2016-now (2)", Path(await Load("Kevin Durant")));
    }

    [Fact]
    public async Task An_empty_or_failing_source_is_recorded_and_changes_nothing()
    {
        await Import(Snapshot(Durant()));
        var empty = await Import(Snapshot());

        Assert.Equal(ImportStatus.Failed, empty.Status);
        Assert.Contains("no players", empty.Error);
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.Players.CountAsync());
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeHistory(HistorySnapshot snapshot) : IHistorySource
    {
        public string Name => "bref";
        public Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeRoster(SourceSnapshot snapshot) : IPlayerSource
    {
        public string Name => "espn";
        public Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}

public class CrosswalkImportServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));

    public CrosswalkImportServiceTests()
    {
        using var ctx = _db.NewContext();
        var league = new League { Code = "nba", Name = "NBA", Sport = "Basketball" };
        Player P(string id, string name, string? country = null) => new()
        {
            League = league, ExternalId = id, FullName = name, NormalizedName = name.ToLowerInvariant(), Country = country,
            ExternalIds = [new PlayerExternalId { Source = "bref", Value = id }],
        };
        ctx.Players.AddRange(P("jamesle01", "LeBron James", "USA"), P("jokicni01", "Nikola Jokic"), P("nobody01", "No Body"));
        ctx.SaveChanges();
    }

    private async Task<ImportRun> Run(params CrosswalkEntry[] entries)
    {
        await using var ctx = _db.NewContext();
        return await new CrosswalkImportService(ctx, _clock, NullLogger<CrosswalkImportService>.Instance).RunAsync(new Fake(entries));
    }

    private static CrosswalkEntry Entry(string bref, string? nba = null, string? espn = null, string? place = null, string? country = null, double? lat = null, double? lon = null)
    {
        var ids = new Dictionary<string, string>();
        if (nba is not null) ids["nba"] = nba;
        if (espn is not null) ids["espn"] = espn;
        return new CrosswalkEntry("bref", bref, ids, place, country, lat, lon);
    }

    [Fact]
    public async Task Adds_ids_a_headshot_and_a_birthplace()
    {
        var run = await Run(
            Entry("jamesle01", "2544", "1966", "Akron", "United States", 41.07, -81.52),
            Entry("jokicni01", espn: "3112335", place: "Sombor", country: "Serbia"),
            Entry("stranger01", "1"));

        Assert.Equal((ImportStatus.Succeeded, 2), (run.Status, run.PlayersUpdated));

        await using var ctx = _db.NewContext();
        var lebron = await ctx.Players.Include(p => p.ExternalIds).SingleAsync(p => p.ExternalId == "jamesle01");
        Assert.Equal(["1966", "2544", "jamesle01"], lebron.ExternalIds.Select(x => x.Value).Order());
        Assert.Equal("https://cdn.nba.com/headshots/nba/latest/260x190/2544.png", lebron.HeadshotUrl);
        Assert.Equal(("Akron", 41.07, -81.52), (lebron.BirthPlace, lebron.BirthLatitude, lebron.BirthLongitude));
        Assert.Equal("USA", lebron.Country); // a country already known is not replaced

        var jokic = await ctx.Players.SingleAsync(p => p.ExternalId == "jokicni01");
        Assert.Equal(("Sombor", "Serbia"), (jokic.BirthPlace, jokic.Country));
        Assert.Null(jokic.HeadshotUrl); // no NBA id, so no league headshot
    }

    [Fact]
    public async Task Running_it_again_updates_nobody()
    {
        await Run(Entry("jamesle01", "2544", "1966", "Akron", "United States", 41.07, -81.52));
        var again = await Run(Entry("jamesle01", "2544", "1966", "Akron", "United States", 41.07, -81.52));

        Assert.Equal(0, again.PlayersUpdated);
    }

    [Fact]
    public async Task An_id_that_already_belongs_to_someone_else_is_skipped()
    {
        var run = await Run(Entry("jamesle01", "2544"), Entry("jokicni01", "2544"));

        Assert.Equal(ImportStatus.Succeeded, run.Status);
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.PlayerExternalIds.CountAsync(x => x.Source == "nba"));
        Assert.Null((await ctx.Players.SingleAsync(p => p.ExternalId == "jokicni01")).HeadshotUrl);
    }

    [Fact]
    public async Task An_empty_crosswalk_is_a_failure()
    {
        var run = await Run();
        Assert.Equal(ImportStatus.Failed, run.Status);
    }

    public void Dispose() => _db.Dispose();

    private sealed class Fake(IReadOnlyList<CrosswalkEntry> entries) : ICrosswalkSource
    {
        public string Name => "wikidata";
        public Task<IReadOnlyList<CrosswalkEntry>> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(entries);
    }
}

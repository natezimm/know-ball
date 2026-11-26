using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Import;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

/// <summary>
/// Runs the MLB Stats API sources over the small made-up responses in tests/fixtures/mlbstats, saved under the paths
/// <see cref="MlbStatsOptions.Directory"/> reads. tests/fixtures/mlbstats is made by hand; its players are made up.
/// </summary>
public class MlbStatsSourceTests
{
    public static readonly string FixtureDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BrefCsvSourceTests.FixtureDirectory)!, "mlbstats");

    private static readonly DateTimeOffset MidSeason = new(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static MlbStatsOptions Options() => new() { Directory = FixtureDirectory, FirstSeason = 2024, FirstDraft = 2024, MinSeasons = 2 };

    private static async Task<HistorySnapshot> History(DateTimeOffset now) =>
        await new MlbStatsHistorySource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<MlbStatsHistorySource>.Instance).FetchAsync(CancellationToken.None);

    private static async Task<SourceSnapshot> Roster(DateTimeOffset now) =>
        await new MlbStatsRosterSource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<MlbStatsRosterSource>.Instance).FetchAsync(CancellationToken.None);

    private static string Path(HistoryPlayer p) =>
        string.Join(", ", p.Seasons.Select(s => $"{s.Season} {s.TeamKey}{(s.IsSeasonTotal ? "" : "*")}"));

    [Fact]
    public async Task History_reads_every_season_with_bio_facts_awards_and_card_lines()
    {
        var snapshot = await History(MidSeason);

        Assert.Equal(("mlb", "mlbam", 2025), (snapshot.LeagueCode, snapshot.IdSource, snapshot.LatestSeason));

        var alpha = snapshot.Players.Single(p => p.ExternalId == "1001");
        Assert.Equal(("Alpha", "Slugger", "RF"), (alpha.FirstName, alpha.LastName, alpha.Position));
        Assert.Equal((74, 210, new DateOnly(1995, 4, 1)), (alpha.HeightInches!.Value, alpha.WeightLbs!.Value, alpha.BirthDate!.Value));
        Assert.Equal((2020, 2025), (alpha.FromYear, alpha.ToYear)); // his debut, before the first season read
        Assert.Equal(("Oak Park, IL", "USA", "99"), (alpha.BirthPlace, alpha.Country, alpha.JerseyNumber));
        Assert.Contains("/people/1001/headshot/", alpha.HeadshotUrl);
        Assert.Equal("2024 147, 2025 147", Path(alpha));
        Assert.Equal(
            [new HistoryAward(2024, AwardTypes.AllStar), new HistoryAward(2025, AwardTypes.AllStar), new HistoryAward(2025, AwardTypes.Mvp)],
            alpha.Awards);
        // Counting stats under the card's short keys, zeros left out; the games at each position and the main one.
        Assert.Equal(
            """{"ab":550,"r":90,"h":160,"2b":30,"3b":2,"hr":35,"rbi":100,"sb":10,"bb":70,"so":150,"fpos":{"RF":140,"LF":10},"pos":"RF"}""",
            alpha.Seasons[0].StatsJson);
        Assert.Equal(150, alpha.Seasons[0].Games);

        // A September call-up with one season is left out; a rookie playing now is kept, with his draft slot and college.
        Assert.DoesNotContain(snapshot.Players, p => p.ExternalId == "1003");
        var delta = snapshot.Players.Single(p => p.ExternalId == "1004");
        Assert.Equal(("Delta", "Rookie Jr."), (delta.FirstName, delta.LastName));
        Assert.Equal((2024, 3, "Vanderbilt"), (delta.DraftYear, delta.DraftNumber, delta.College));

        // A Gold Glove counts; a catcher's fame comes from the whole career.
        var echo = snapshot.Players.Single(p => p.ExternalId == "1005");
        Assert.Equal([new HistoryAward(2024, AwardTypes.GoldGlove)], echo.Awards);
        Assert.True(alpha.Popularity > echo.Popularity);
    }

    [Fact]
    public async Task A_traded_pitchers_season_is_split_by_team_in_the_order_played()
    {
        var snapshot = await History(MidSeason);
        var bravo = snapshot.Players.Single(p => p.ExternalId == "1002");

        // The season list files his 2025 under the Yankees; his year-by-year lines put the Nationals first. Minor-league
        // lines are ignored.
        Assert.Equal("2024 120, 2025 120*, 2025 147*, 2025 ", Path(bravo));
        var total = bravo.Seasons[^1];
        Assert.Equal(30, total.Games);
        Assert.Equal("""{"gs":30,"w":14,"l":8,"outs":540,"er":60,"ha":150,"pbb":45,"k":220,"pg":30,"fpos":{"P":30},"pos":"P"}""", total.StatsJson);
        Assert.Equal("""{"gs":18,"w":8,"l":5,"outs":330,"er":38,"ha":90,"pbb":25,"k":130,"pg":18,"fpos":{"P":18},"pos":"P"}""", bravo.Seasons[1].StatsJson);
        // A pitcher who starts is filed as a starter.
        Assert.Equal("SP", bravo.Position);
        Assert.Equal(("Santo Domingo, Dominican Republic", "Dominican Republic"), (bravo.BirthPlace, bravo.Country));
    }

    [Fact]
    public async Task Teams_keep_their_ids_and_a_former_name_points_at_the_team_it_became()
    {
        var snapshot = await History(MidSeason);

        var athletics = snapshot.Teams.Single(t => t.Key == "133");
        Assert.Equal(("", "Athletics", "ATH", "AL", "AL West"), (athletics.Team.City, athletics.Team.Name, athletics.Team.Abbreviation, athletics.Team.Conference, athletics.Team.Division));
        Assert.Equal((2025, (int?)null, (string?)null), (athletics.ActiveFrom!.Value, athletics.ActiveTo, athletics.FranchiseKey));

        var oakland = snapshot.Teams.Single(t => t.Key == "133-2024");
        Assert.Equal(("Oakland", "Athletics", "OAK", "133"), (oakland.Team.City, oakland.Team.Name, oakland.Team.Abbreviation, oakland.FranchiseKey));
        Assert.Equal((2024, 2024), (oakland.ActiveFrom!.Value, oakland.ActiveTo!.Value));
        // The same club name keeps its logo.
        Assert.Equal("https://www.mlbstatic.com/team-logos/133.svg", oakland.Team.LogoUrl);

        var echo = snapshot.Players.Single(p => p.ExternalId == "1005");
        Assert.Equal("2024 133-2024, 2025 133", Path(echo));

        Assert.Equal("Nationals Park", snapshot.Teams.Single(t => t.Key == "120").Team.Arena);
    }

    [Fact]
    public async Task History_reads_each_teams_record_and_playoff_berth()
    {
        var snapshot = await History(MidSeason);

        Assert.Equal(
            ["2024 147 94-68 True", "2024 133-2024 69-93 False", "2024 120 71-91 False", "2025 147 94-68 True", "2025 133 76-86 False", "2025 120 66-96 False"],
            snapshot.TeamSeasons!.Select(r => $"{r.Season} {r.TeamKey} {r.Wins}-{r.Losses} {r.MadePlayoffs}"));
    }

    [Fact]
    public async Task Before_opening_day_the_record_ends_with_last_season()
    {
        var snapshot = await History(new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2025, snapshot.LatestSeason);

        // In April the 2026 files are not there yet, so the record still ends with 2025.
        snapshot = await History(new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2025, snapshot.LatestSeason);
    }

    [Fact]
    public async Task Roster_lists_major_leaguers_on_current_teams_and_leaves_out_prospects()
    {
        var snapshot = await Roster(MidSeason);

        Assert.Equal(("mlb", 2025, true, true), (snapshot.LeagueCode, snapshot.Season, snapshot.InSeason, snapshot.Supplement));
        Assert.Equal("mlbam", snapshot.IdSource);
        Assert.Equal(["120", "133", "147"], snapshot.Teams.Select(t => t.ExternalId).Order());
        Assert.Equal(["1001", "1002", "1004", "1005"], snapshot.Players.Select(p => p.ExternalId).Order());
        var delta = snapshot.Players.Single(p => p.ExternalId == "1004");
        Assert.Equal(("120", "2", "SS", 2025), (delta.TeamExternalId, delta.JerseyNumber, delta.Position, delta.FromYear!.Value));
    }

    [Fact]
    public async Task After_the_World_Series_rosters_are_for_next_season()
    {
        var snapshot = await Roster(new DateTimeOffset(2025, 11, 20, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal((2026, false), (snapshot.Season, snapshot.InSeason));
        Assert.Equal(3, snapshot.Teams.Count);
    }

    [Fact]
    public void Stats_api_addresses_match_the_saved_file_paths()
    {
        Assert.Equal("teams?sportId=1&season=2024", MlbStatsFiles.UrlFor(MlbStatsFiles.TeamsPath(2024), 5000));
        Assert.Equal("stats?stats=season&group=fielding&season=1999&sportId=1&playerPool=ALL&gameType=R&limit=5000&offset=0",
            MlbStatsFiles.UrlFor(MlbStatsFiles.StatsPath("fielding", 1999), 5000));
        Assert.Equal("awards/ALCY/recipients?sportId=1", MlbStatsFiles.UrlFor(MlbStatsFiles.AwardPath("ALCY"), 5000));
        Assert.Equal("teams/147/roster?rosterType=40Man&hydrate=person", MlbStatsFiles.UrlFor(MlbStatsFiles.RosterPath("147"), 5000));
        Assert.Equal("people?personIds=1,2&hydrate=stats(group=[hitting,pitching,fielding],type=[yearByYear],sportId=1)", MlbStatsFiles.CareersUrl(["1", "2"]));
    }

    [Fact]
    public async Task Loading_the_record_and_the_rosters_twice_changes_nothing()
    {
        using var db = new TestDb();
        var clock = new FakeTimeProvider(MidSeason);
        var history = new FakeHistory(await History(MidSeason));
        var roster = new FakeRoster(await Roster(MidSeason));

        for (var i = 0; i < 2; i++)
        {
            await using (var ctx = db.NewContext())
                await new HistoryImportService(ctx, clock, NullLogger<HistoryImportService>.Instance).RunAsync(history);
            await using (var ctx = db.NewContext())
                await new PlayerImportService(ctx, clock, NullLogger<PlayerImportService>.Instance).RunAsync(roster);
        }

        await using var check = db.NewContext();
        var runs = await check.ImportRuns.OrderBy(r => r.Id).ToListAsync();
        Assert.All(runs, r => Assert.Equal(KnowBall.Domain.ImportStatus.Succeeded, r.Status));
        Assert.Equal((0, 0), (runs[2].PlayersInserted, runs[2].PlayersUpdated));
        Assert.Equal((0, 0, 0), (runs[3].PlayersInserted, runs[3].PlayersUpdated, runs[3].TeamChanges));

        var bravo = await check.Players.Include(p => p.Stints.OrderBy(s => s.Order)).ThenInclude(s => s.Team).Include(p => p.CurrentTeam)
            .SingleAsync(p => p.ExternalId == "1002");
        Assert.Equal("NYY", bravo.CurrentTeam!.Abbreviation);
        Assert.Equal(["WSH 2024-2025", "NYY 2025-now"], bravo.Stints.Select(s => $"{s.Team!.Abbreviation} {s.StartSeason}-{s.EndSeason?.ToString() ?? "now"}"));
        Assert.Equal(("Santo Domingo, Dominican Republic", "SP"), (bravo.BirthPlace, bravo.Position));
        var athletics = await check.Teams.Include(t => t.Franchise).SingleAsync(t => t.ExternalId == "133-2024");
        Assert.Equal("ATH", athletics.Franchise!.Abbreviation);
    }

    private sealed class FakeHistory(HistorySnapshot snapshot) : IHistorySource
    {
        public string Name => MlbStatsHistorySource.SourceName;
        public Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FakeRoster(SourceSnapshot snapshot) : IPlayerSource
    {
        public string Name => MlbStatsRosterSource.SourceName;
        public Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}

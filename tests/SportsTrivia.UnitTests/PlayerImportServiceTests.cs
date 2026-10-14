using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SportsTrivia.Application.Import;
using SportsTrivia.Domain;

namespace SportsTrivia.UnitTests;

public class PlayerImportServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero));

    private static readonly SourceTeam Lakers = new("LAL-ID", "Los Angeles", "Lakers", "LAL", "West", "Pacific");
    private static readonly SourceTeam Mavs = new("DAL-ID", "Dallas", "Mavericks", "DAL", "West", "Southwest");

    private static SourcePlayer Luka(string? team = "DAL-ID") =>
        new("77", "Luka", "Dončić", team, Position: "G", PointsPerGame: 28.1, ReboundsPerGame: 8.2, AssistsPerGame: 7.7);

    private static SourcePlayer LeBron() =>
        new("23", "LeBron", "James", "LAL-ID", Position: "F", PointsPerGame: 24.4, FromYear: 2003, DraftNumber: 1);

    private async Task<ImportRun> Import(int season, params SourcePlayer[] players) =>
        await Import(new SourceSnapshot("nba", season, [Lakers, Mavs], players));

    private async Task<ImportRun> Import(SourceSnapshot snapshot) => await Import(new FakeSource(snapshot));

    private async Task<ImportRun> Import(IPlayerSource source)
    {
        await using var ctx = _db.NewContext();
        var service = new PlayerImportService(ctx, _clock, NullLogger<PlayerImportService>.Instance);
        return await service.RunAsync(source);
    }

    [Fact]
    public async Task First_import_creates_league_teams_players_stints_and_stats()
    {
        var run = await Import(2025, Luka(), LeBron());

        Assert.Equal(ImportStatus.Succeeded, run.Status);
        Assert.Equal(2, run.PlayersInserted);
        Assert.Equal(2, run.TeamsUpserted);
        Assert.Equal(0, run.TeamChanges); // first teams are arrivals, not moves

        await using var ctx = _db.NewContext();
        var luka = await ctx.Players.Include(p => p.CurrentTeam).Include(p => p.Stints).Include(p => p.SeasonStats).SingleAsync(p => p.ExternalId == "77");
        Assert.Equal("Luka Dončić", luka.FullName);
        Assert.Equal("luka doncic", luka.NormalizedName);
        Assert.Equal("DAL", luka.CurrentTeam!.Abbreviation);
        Assert.Single(luka.Stints);
        Assert.Equal(2025, luka.Stints[0].StartSeason);
        Assert.Null(luka.Stints[0].EndSeason);
        Assert.Equal(28.1, luka.SeasonStats.Single().PointsPerGame);
        Assert.True(luka.Popularity > 0);
        Assert.Equal("NBA", (await ctx.Leagues.SingleAsync()).Name);
    }

    [Fact]
    public async Task Rerunning_the_same_data_changes_nothing()
    {
        await Import(2025, Luka(), LeBron());
        var second = await Import(2025, Luka(), LeBron());

        Assert.Equal(ImportStatus.Succeeded, second.Status);
        Assert.Equal(0, second.PlayersInserted);
        Assert.Equal(0, second.PlayersUpdated);
        Assert.Equal(0, second.TeamChanges);

        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.Players.CountAsync());
        Assert.Equal(2, await ctx.PlayerTeamStints.CountAsync());
        Assert.Equal(2, await ctx.Teams.CountAsync());
    }

    [Fact]
    public async Task Trade_closes_old_stint_and_opens_new_one()
    {
        await Import(2024, Luka("DAL-ID"), LeBron());
        var run = await Import(2025, Luka("LAL-ID"), LeBron());

        Assert.Equal(1, run.TeamChanges);
        await using var ctx = _db.NewContext();
        var stints = await ctx.PlayerTeamStints.Include(s => s.Team)
            .Where(s => s.Player!.ExternalId == "77").OrderBy(s => s.Order).ToListAsync();

        Assert.Collection(stints,
            s => { Assert.Equal("DAL", s.Team!.Abbreviation); Assert.Equal(2024, s.StartSeason); Assert.Equal(2025, s.EndSeason); Assert.Equal(1, s.Order); },
            s => { Assert.Equal("LAL", s.Team!.Abbreviation); Assert.Equal(2025, s.StartSeason); Assert.Null(s.EndSeason); Assert.Equal(2, s.Order); });
    }

    [Fact]
    public async Task Player_missing_from_feed_is_deactivated_and_comes_back_later()
    {
        await Import(2025, Luka(), LeBron());
        var run = await Import(2025, Luka());
        Assert.Equal(1, run.PlayersDeactivated);

        await using (var ctx = _db.NewContext())
        {
            var lebron = await ctx.Players.Include(p => p.Stints).SingleAsync(p => p.ExternalId == "23");
            Assert.False(lebron.IsActive);
            Assert.Null(lebron.CurrentTeamId);
            Assert.All(lebron.Stints, s => Assert.NotNull(s.EndSeason));
        }

        var back = await Import(2026, Luka(), LeBron());
        Assert.Equal(1, back.PlayersUpdated);
        await using (var ctx = _db.NewContext())
            Assert.True((await ctx.Players.SingleAsync(p => p.ExternalId == "23")).IsActive);
    }

    [Fact]
    public async Task Refuses_to_wipe_the_league_when_the_feed_shrinks_suddenly()
    {
        var full = Enumerable.Range(1, 40).Select(i => new SourcePlayer(i.ToString(), "P", $"Player{i}", "LAL-ID")).ToArray();
        await Import(2025, full);

        var run = await Import(2025, full.Take(5).ToArray());

        Assert.Equal(ImportStatus.Failed, run.Status);
        Assert.Contains("refusing", run.Error);
        await using var ctx = _db.NewContext();
        Assert.Equal(40, await ctx.Players.CountAsync(p => p.IsActive));
    }

    [Fact]
    public async Task Failing_source_records_a_failed_run_and_leaves_data_alone()
    {
        await Import(2025, Luka());
        var run = await Import(new ThrowingSource());

        Assert.Equal(ImportStatus.Failed, run.Status);
        Assert.Equal("upstream is down", run.Error);
        Assert.NotNull(run.FinishedAt);

        await using var ctx = _db.NewContext();
        var runs = await ctx.ImportRuns.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(new[] { ImportStatus.Succeeded, ImportStatus.Failed }, runs.Select(r => r.Status));
        Assert.Equal(1, await ctx.Players.CountAsync());
    }

    [Fact]
    public async Task Empty_feed_fails()
    {
        var run = await Import(2025);
        Assert.Equal(ImportStatus.Failed, run.Status);
        Assert.Contains("no players", run.Error);
    }

    [Fact]
    public async Task Duplicate_rows_in_feed_are_ignored()
    {
        var run = await Import(2025, Luka(), Luka(), LeBron());
        Assert.Equal(2, run.PlayersInserted);
    }

    [Fact]
    public async Task Unknown_team_leaves_player_teamless()
    {
        var run = await Import(2025, Luka("NOPE"));
        Assert.Equal(ImportStatus.Succeeded, run.Status);
        await using var ctx = _db.NewContext();
        Assert.Null((await ctx.Players.SingleAsync()).CurrentTeamId);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeSource(SourceSnapshot snapshot) : IPlayerSource
    {
        public string Name => "fake";
        public Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class ThrowingSource : IPlayerSource
    {
        public string Name => "broken";
        public Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("upstream is down");
    }
}

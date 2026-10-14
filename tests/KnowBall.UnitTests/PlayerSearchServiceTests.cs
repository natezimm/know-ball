using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Import;
using KnowBall.Application.Players;

namespace KnowBall.UnitTests;

public class PlayerSearchServiceTests : IDisposable
{
    private readonly TestDb _db = new();

    public PlayerSearchServiceTests()
    {
        var teams = new[] { new SourceTeam("DEN", "Denver", "Nuggets", "DEN"), new SourceTeam("LAL", "Los Angeles", "Lakers", "LAL") };
        var players = new[]
        {
            new SourcePlayer("1", "Nikola", "Jokić", "DEN", Position: "C", Popularity: 97),
            new SourcePlayer("2", "LeBron", "James", "LAL", Popularity: 100),
            new SourcePlayer("3", "Bronny", "James", "LAL", Popularity: 40),
            new SourcePlayer("4", "Jamal", "Murray", "DEN", Popularity: 70),
            new SourcePlayer("5", "James", "Harden", null, Popularity: 85),
            new SourcePlayer("6", "Luka", "Dončić", "LAL", Popularity: 96),
        };
        using var ctx = _db.NewContext();
        new PlayerImportService(ctx, new FakeTimeProvider(), NullLogger<PlayerImportService>.Instance)
            .RunAsync(new Fake(new SourceSnapshot("nba", 2025, teams, players))).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyList<PlayerSearchResult>> Search(string q, int limit = 10)
    {
        await using var ctx = _db.NewContext();
        return await new PlayerSearchService(ctx).SearchAsync(q, "nba", limit);
    }

    [Fact]
    public async Task Accent_insensitive()
    {
        var r = await Search("jokic");
        var jokic = Assert.Single(r);
        Assert.Equal("Nikola Jokić", jokic.FullName);
        Assert.Equal("DEN", jokic.TeamAbbreviation);
        Assert.Equal("Denver Nuggets", jokic.TeamName);

        Assert.Single(await Search("DONČIĆ"));
    }

    [Fact]
    public async Task Matches_start_of_any_word_but_not_the_middle()
    {
        Assert.Single(await Search("murr"));
        Assert.Empty(await Search("urray"));
    }

    [Fact]
    public async Task First_name_prefix_ranks_above_last_name_then_by_popularity()
    {
        var names = (await Search("jam")).Select(r => r.FullName).ToList();
        // "James Harden" and "Jamal Murray" start with the term; the two James surnames follow.
        Assert.Equal(new[] { "James Harden", "Jamal Murray", "LeBron James", "Bronny James" }, names);
    }

    [Fact]
    public async Task Free_agent_has_no_team()
    {
        var harden = Assert.Single(await Search("harden"));
        Assert.Null(harden.TeamAbbreviation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("j")]
    [InlineData("  ")]
    public async Task Too_short_returns_nothing(string q) => Assert.Empty(await Search(q));

    [Fact]
    public async Task Respects_limit() => Assert.Equal(2, (await Search("jam", limit: 2)).Count);

    public void Dispose() => _db.Dispose();

    private sealed class Fake(SourceSnapshot s) : IPlayerSource
    {
        public string Name => "fake";
        public Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(s);
    }
}

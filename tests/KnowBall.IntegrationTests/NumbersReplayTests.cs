using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

[Collection(ApiCollection.Name)]
public class NumbersReplayTests(ApiFactory factory)
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", Guid.NewGuid().ToString());
        return client;
    }

    private static object Request(Guid id, bool repeats = false) => new {
        leagues = new[] { "nba" }, topics = new[] { "stats" }, fromYear = 2023, throughYear = 2023,
        requestId = id, allowRepeats = repeats
    };

    private static async Task<JsonElement> Post(HttpClient client, string endpoint, object request)
    {
        var response = await client.PostAsJsonAsync("/api/numbers-dont-lie/" + endpoint, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HashSet<string>> Families(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var row = await db.NumbersPlays.AsNoTracking().SingleAsync(p => p.Id == id);
        return JsonSerializer.Deserialize<JsonElement[]>(row.SecretJson)!
            .Select(q => q.GetProperty("factFamily").GetString()!).ToHashSet();
    }

    [Fact]
    public async Task Narrow_filters_report_exhaustion_and_require_explicit_replay_without_breaking_start_retries()
    {
        using var client = Client();
        var request = Request(Guid.NewGuid());
        var coverage = await Post(client, "practice/coverage", request);
        Assert.Equal(6, coverage.GetProperty("eligibleFacts").GetInt32());
        Assert.Equal(6, coverage.GetProperty("freshFacts").GetInt32());
        Assert.True(coverage.GetProperty("canStartFresh").GetBoolean());
        Assert.DoesNotContain("factFamily", coverage.GetRawText());
        var first = await Post(client, "practice", request);
        var firstId = first.GetProperty("id").GetGuid();
        var retry = await Post(client, "practice", request);
        Assert.Equal(firstId, retry.GetProperty("id").GetGuid());
        var restored = await client.GetFromJsonAsync<JsonElement>($"/api/numbers-dont-lie/quizzes/{firstId}");
        Assert.Equal(first.GetProperty("filters").GetRawText(), restored.GetProperty("filters").GetRawText());
        Assert.Equal(2023, restored.GetProperty("filters").GetProperty("fromYear").GetInt32());
        Assert.Equal("stats", restored.GetProperty("filters").GetProperty("topics")[0].GetString());

        var secondRequestId = Guid.NewGuid();
        coverage = await Post(client, "practice/coverage", Request(secondRequestId));
        Assert.Equal(1, coverage.GetProperty("freshFacts").GetInt32());
        Assert.False(coverage.GetProperty("canStartFresh").GetBoolean());
        Assert.True(coverage.GetProperty("canStartWithRepeats").GetBoolean());
        var blocked = await client.PostAsJsonAsync("/api/numbers-dont-lie/practice", Request(secondRequestId));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var replay = await Post(client, "practice", Request(secondRequestId, true));
        var firstFamilies = await Families(firstId);
        var replayFamilies = await Families(replay.GetProperty("id").GetGuid());
        Assert.Equal(5, replayFamilies.Count);
        Assert.Single(replayFamilies.Except(firstFamilies)); // uses the remaining fresh fact before repeats
        Assert.Equal(4, replayFamilies.Intersect(firstFamilies).Count());
        using var other = Client();
        var otherCoverage = await Post(other, "practice/coverage", Request(Guid.NewGuid()));
        Assert.Equal(6, otherCoverage.GetProperty("freshFacts").GetInt32());
    }

    [Fact]
    public async Task Successive_broad_practice_rounds_do_not_share_fact_families()
    {
        using var client = Client();
        var seen = new HashSet<string>();
        for (var i = 0; i < 4; i++)
        {
            var state = await Post(client, "practice", new {
                leagues = new[] { "nba" }, topics = new[] { "stats", "awards", "teams", "seasons" }, requestId = Guid.NewGuid()
            });
            var families = await Families(state.GetProperty("id").GetGuid());
            Assert.Empty(families.Intersect(seen));
            seen.UnionWith(families);
        }
        Assert.Equal(20, seen.Count);
    }

    [Fact]
    public async Task Expired_practice_reservations_leave_the_recent_window_and_empty_years_are_not_relaxed()
    {
        using var client = Client();
        var first = await Post(client, "practice", Request(Guid.NewGuid()));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var row = await db.NumbersPlays.SingleAsync(p => p.Id == first.GetProperty("id").GetGuid());
            row.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var coverage = await Post(client, "practice/coverage", Request(Guid.NewGuid()));
        Assert.Equal(6, coverage.GetProperty("freshFacts").GetInt32());
        var empty = new { leagues = new[] { "nba" }, topics = new[] { "stats" }, fromYear = 1900, throughYear = 1901,
            requestId = Guid.NewGuid(), allowRepeats = true };
        coverage = await Post(client, "practice/coverage", empty);
        Assert.Equal(0, coverage.GetProperty("eligibleFacts").GetInt32());
        Assert.False(coverage.GetProperty("canStartWithRepeats").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/numbers-dont-lie/practice", empty)).StatusCode);
    }
}

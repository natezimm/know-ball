using System.Net.Http.Json;
using System.Text.Json;
using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

[Collection(ApiCollection.Name)]
public class NumbersStreakTests(ApiFactory factory)
{
    private (HttpClient Client, string Owner) Client()
    {
        var owner = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", owner);
        return (client, owner);
    }

    [Fact]
    public async Task Daily_streak_counts_consecutive_finished_days_up_to_today_or_yesterday()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
        var (finishedToday, ownerA) = Client();
        var (notYetToday, ownerB) = Client();
        using var a = finishedToday; using var b = notYetToday;
        NumbersPlay Finished(string owner, int daysAgo) => new() { Id = Guid.NewGuid(), Owner = owner,
            StartKey = today.AddDays(-daysAgo).ToString("yyyy-MM-dd"), Date = today.AddDays(-daysAgo), Position = 5 };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            // Today, 1 and 2 days ago, then a gap, then 4 days ago.
            db.NumbersPlays.AddRange(Finished(ownerA, 0), Finished(ownerA, 1), Finished(ownerA, 2), Finished(ownerA, 4));
            // Yesterday and the day before, plus an unfinished round 3 days ago that must not count.
            var unfinished = Finished(ownerB, 3);
            unfinished.Position = 2;
            db.NumbersPlays.AddRange(Finished(ownerB, 1), Finished(ownerB, 2), unfinished);
            await db.SaveChangesAsync();
        }
        Assert.Equal(3, (await a.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily")).GetProperty("dailyStreak").GetInt32());
        var started = await b.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily");
        Assert.False(started.GetProperty("complete").GetBoolean());
        Assert.Equal(2, started.GetProperty("dailyStreak").GetInt32());
    }
}

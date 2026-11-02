using System.Text.Json;
using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace KnowBall.UnitTests;

public class NumbersScoringTests
{
    private static int Streak(object view) => JsonSerializer.SerializeToElement(view).GetProperty("dailyStreak").GetInt32();

    private static NumbersPlay Finished(string owner, DateOnly date) =>
        new() { Id = Guid.NewGuid(), Owner = owner, StartKey = date.ToString("yyyy-MM-dd"), Date = date, Position = 5 };

    [Fact]
    public async Task Daily_streak_uses_the_eastern_day_not_utc()
    {
        using var testDb = new TestDb();
        // 22:30 on November 2 in New York is already November 3 in UTC.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero));
        await using (var db = testDb.NewContext())
        {
            db.NumbersPlays.AddRange(Finished("anon-1", new(2026, 11, 1)), Finished("anon-1", new(2026, 10, 31)));
            await db.SaveChangesAsync();
        }
        await using var context = testDb.NewContext();
        var service = new NumbersPlayService(context, clock);
        // Today (Eastern) is unfinished, so the streak still runs through yesterday.
        Assert.Equal(2, Streak(await service.Daily("anon-1", CancellationToken.None)));

        // Two Eastern days later without playing, the streak is gone.
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, Streak(await service.Daily("anon-1", CancellationToken.None)));
    }

    [Fact]
    public async Task Practice_rounds_have_no_daily_streak_and_score_out_of_fifteen()
    {
        using var testDb = new TestDb();
        await using var context = testDb.NewContext();
        var service = new NumbersPlayService(context, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero)));
        var view = JsonSerializer.SerializeToElement(await service.Start("anon-2",
            new(["nba"], ["awards"], null, null, Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("dailyStreak").ValueKind);
        Assert.Equal(15, view.GetProperty("maxPoints").GetInt32());
        Assert.Equal(0, view.GetProperty("points").GetInt32());
    }
}

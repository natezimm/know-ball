using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class NumbersPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Migrated_schema_preserves_content_and_enforces_daily_uniqueness()
    {
        // Runs on SQLite locally and migrated Postgres in the existing CI matrix.
        _ = factory.CreateClient();
        var id = Guid.NewGuid().ToString("N");
        var date = new DateOnly(2088, 1, 1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            db.NumbersCatalogues.Add(new() { Id = id, ContentJson = "{}", ImportedAt = DateTimeOffset.UtcNow });
            db.NumbersQuizzes.Add(new() { Id = Guid.NewGuid(), Date = date, CatalogueId = id, QuestionsJson = "[1,2,3,4,5]", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            Assert.Equal("[1,2,3,4,5]", (await db.NumbersQuizzes.SingleAsync(q => q.Date == date)).QuestionsJson);
            db.NumbersQuizzes.Add(new() { Id = Guid.NewGuid(), Date = date, CatalogueId = id, QuestionsJson = "[]", CreatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            await db.NumbersQuizzes.Where(q => q.CatalogueId == id).ExecuteDeleteAsync();
            await db.NumbersCatalogues.Where(c => c.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Purge_deletes_only_expired_practice_rounds()
    {
        // Runs on SQLite locally and migrated Postgres in CI, so the date comparison is checked on both.
        _ = factory.CreateClient();
        var owner = "purge-" + Guid.NewGuid().ToString("N")[..16];
        var now = DateTimeOffset.UtcNow;
        NumbersPlay Play(string key, DateOnly? date = null, DateTimeOffset? expires = null) =>
            new() { Id = Guid.NewGuid(), Owner = owner, StartKey = key, Date = date, ExpiresAt = expires };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            db.NumbersPlays.AddRange(
                Play("expired", expires: now.AddMinutes(-1)),
                Play("long-expired", expires: now.AddDays(-400)),
                Play("active", expires: now.AddDays(29)),
                Play("daily", date: DateOnly.FromDateTime(now.UtcDateTime).AddDays(-400)));
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<NumbersPlayService>().PurgeExpiredAsync();
            Assert.True(deleted >= 2); // other tests' expired rounds may go too
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var left = await db.NumbersPlays.Where(p => p.Owner == owner).Select(p => p.StartKey).ToListAsync();
            Assert.Equal(["active", "daily"], left.Order().ToArray());
            await db.NumbersPlays.Where(p => p.Owner == owner).ExecuteDeleteAsync();
        }
    }
}

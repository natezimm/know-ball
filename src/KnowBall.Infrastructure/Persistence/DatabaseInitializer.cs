using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowBall.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Bump whenever the model changes. SQLite databases are created straight from the model with
    /// no migrations, so an older file cannot be brought up to date and has to be rebuilt.
    /// </summary>
    public const int SqliteSchemaVersion = 5;

    /// <summary>Applies migrations (Postgres) or creates the schema (SQLite).</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (db.Database.IsNpgsql())
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(ct);
            }
            return;
        }

        if (await db.Database.EnsureCreatedAsync(ct))
        {
            // PRAGMA takes no parameters; the value is a constant.
            await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SqliteSchemaVersion}", ct);
            return;
        }

        var version = await db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").SingleAsync(ct);
        if (version != SqliteSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The SQLite database at '{db.Database.GetDbConnection().DataSource}' was made by an older version of the app " +
                $"(schema {version}, now {SqliteSchemaVersion}). SQLite databases are throwaway: delete the file and run the importer again.");
        }
    }
}

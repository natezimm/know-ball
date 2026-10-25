using KnowBall.Infrastructure.Persistence;

namespace KnowBall.Importer;

/// <summary>Independent queue failure boundary; disabled until approved content is configured.</summary>
public sealed class NumbersQueueJob(IServiceScopeFactory scopes, IConfiguration configuration,
    TimeProvider clock, ILogger<NumbersQueueJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("NumbersDontLie:QueueEnabled")) return;
        try
        {
            var catalogue = configuration["NumbersDontLie:CatalogueId"];
            if (string.IsNullOrWhiteSpace(catalogue) ||
                !DateOnly.TryParseExact(configuration["NumbersDontLie:LaunchDate"], "yyyy-MM-dd", out var launch))
                throw new InvalidOperationException("Numbers queue requires CatalogueId and ISO LaunchDate.");
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), eastern).DateTime);
            await using var scope = scopes.CreateAsyncScope();
            var queue = new NumbersQueue(scope.ServiceProvider.GetRequiredService<TriviaDbContext>(), clock);
            var count = await queue.FillAsync(catalogue, today < launch ? launch : today, launch, ct: ct);
            logger.LogInformation("Numbers queue added {Count} daily snapshots", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Numbers queue top-up failed; existing snapshots remain available");
        }
    }
}

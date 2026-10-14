using Microsoft.Extensions.Options;

namespace KnowBall.Importer;

/// <summary>Runs <see cref="ImportJob"/> once a day at <see cref="ImportOptions.DailyAt"/> local time.</summary>
public sealed class NightlyImportWorker(
    ImportJob job,
    IOptions<ImportOptions> options,
    TimeProvider clock,
    ILogger<NightlyImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(opts.TimeZone);

        if (opts.RunOnStartup) await SafeRunAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = NextRun(clock.GetUtcNow(), opts.DailyAt, zone);
            var wait = next - clock.GetUtcNow();
            logger.LogInformation("Next import at {Next:u} ({Local} {Zone})", next, TimeZoneInfo.ConvertTime(next, zone), zone.Id);

            try
            {
                await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await SafeRunAsync(stoppingToken);
        }
    }

    private async Task SafeRunAsync(CancellationToken ct)
    {
        try
        {
            await job.RunAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let one bad night kill the worker.
            logger.LogError(ex, "Import job crashed");
        }
    }

    /// <summary>Next occurrence of <paramref name="at"/> in <paramref name="zone"/> strictly after <paramref name="now"/>. DST-safe.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(localNow.DateTime);

        for (var i = 0; i < 3; i++)
        {
            var localTarget = date.AddDays(i).ToDateTime(at);
            // A time skipped by spring-forward: nudge an hour later.
            if (zone.IsInvalidTime(localTarget)) localTarget = localTarget.AddHours(1);
            var offset = zone.GetUtcOffset(localTarget);
            var candidate = new DateTimeOffset(localTarget, offset);
            if (candidate > now) return candidate;
        }

        throw new InvalidOperationException("Could not compute next run time.");
    }
}

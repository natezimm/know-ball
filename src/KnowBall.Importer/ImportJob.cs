using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KnowBall.Application;
using KnowBall.Application.Games;
using KnowBall.Application.Import;
using KnowBall.Domain;
using KnowBall.Infrastructure;

namespace KnowBall.Importer;

public sealed class ImportOptions
{
    /// <summary>Roster source: "espn" (live), "seed" (bundled sample) or "nba-stats" (the league's own feed, down since October 2026).</summary>
    public string Source { get; set; } = "espn";
    /// <summary>Source to try if the primary one fails, e.g. "seed" on a fresh dev machine. Empty = none.</summary>
    public string? FallbackSource { get; set; }
    /// <summary>Local time of day for the nightly run.</summary>
    public TimeOnly DailyAt { get; set; } = new(4, 0);
    public string TimeZone { get; set; } = "America/New_York";
    /// <summary>Also run once when the worker starts.</summary>
    public bool RunOnStartup { get; set; } = true;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 60;
    /// <summary>
    /// Reload the full player history when the last successful load is older than this many days,
    /// or has never happened. 0 = never on its own; only when asked for with --history.
    /// </summary>
    public int HistoryRefreshDays { get; set; } = 30;
    /// <summary>How many days of puzzles to keep queued for each generated game. 0 = none.</summary>
    public int PuzzleDaysAhead { get; set; } = 30;
    /// <summary>Practice rounds are deleted after this many days.</summary>
    public int PracticeRetentionDays { get; set; } = 7;
}

/// <summary>
/// One nightly run: reload the player history if it is due, refresh today's rosters (with
/// retries and an optional fallback source), then top up the puzzle queues. Used by --once and
/// by the nightly worker. The roster run is the result; the other steps log their own outcome
/// and never stop the night's work.
/// </summary>
public sealed class ImportJob(IServiceScopeFactory scopes, IOptions<ImportOptions> options, TimeProvider clock, ILogger<ImportJob> logger)
{
    public async Task<ImportRun> RunAsync(CancellationToken ct, bool forceHistory = false)
    {
        var opts = options.Value;

        await RunHistoryIfDueAsync(opts, forceHistory, ct);

        var run = await RunWithRetriesAsync(opts.Source, opts, ct);
        if (run.Status != ImportStatus.Succeeded && !string.IsNullOrWhiteSpace(opts.FallbackSource)
            && !opts.FallbackSource.Equals(opts.Source, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Primary source {Source} failed; trying fallback {Fallback}", opts.Source, opts.FallbackSource);
            run = await RunWithRetriesAsync(opts.FallbackSource, opts, ct);
        }

        // Yesterday's rosters still make fair puzzles, so the queues are topped up even after a failed import.
        await FillPuzzleQueuesAsync(opts, ct);
        return run;
    }

    private async Task<ImportRun> RunWithRetriesAsync(string sourceName, ImportOptions opts, CancellationToken ct)
    {
        ImportRun? run = null;
        for (var attempt = 1; attempt <= Math.Max(1, opts.MaxAttempts); attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<PlayerSourceResolver>().Get(sourceName);
            var importer = scope.ServiceProvider.GetRequiredService<PlayerImportService>();

            run = await importer.RunAsync(source, ct);
            if (run.Status == ImportStatus.Succeeded) return run;

            if (attempt < opts.MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(opts.RetryDelaySeconds * attempt);
                logger.LogWarning("Attempt {Attempt}/{Max} from {Source} failed; retrying in {Delay}", attempt, opts.MaxAttempts, sourceName, delay);
                await Task.Delay(delay, ct);
            }
        }
        return run!;
    }

    /// <summary>The full record, then the id links and birthplaces that hang off it.</summary>
    private async Task RunHistoryIfDueAsync(ImportOptions opts, bool force, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var history = services.GetRequiredService<IHistorySource>();

            if (!force)
            {
                if (opts.HistoryRefreshDays <= 0) return;

                // Read back in full: the date column cannot be compared in SQL on every database.
                var last = (await services.GetRequiredService<ITriviaDb>().ImportRuns.AsNoTracking()
                        .Where(r => r.Source == history.Name && r.Status == ImportStatus.Succeeded)
                        .OrderByDescending(r => r.Id)
                        .Take(1)
                        .ToListAsync(ct))
                    .FirstOrDefault();
                if (last?.FinishedAt is { } finished && clock.GetUtcNow() - finished < TimeSpan.FromDays(opts.HistoryRefreshDays)) return;
            }

            var run = await services.GetRequiredService<HistoryImportService>().RunAsync(history, ct);
            Console.WriteLine(Describe(run));
            if (run.Status != ImportStatus.Succeeded) return;

            var links = await services.GetRequiredService<CrosswalkImportService>()
                .RunAsync(services.GetRequiredService<ICrosswalkSource>(), run.LeagueCode, ct);
            Console.WriteLine(Describe(links));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "History import crashed");
        }
    }

    private async Task FillPuzzleQueuesAsync(ImportOptions opts, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<PuzzleScheduler>();

            foreach (var game in scope.ServiceProvider.GetRequiredService<GameRegistry>().All)
            {
                var made = await scheduler.FillQueueAsync(game, opts.PuzzleDaysAhead, ct);
                if (made > 0) logger.LogInformation("Queued {Count} {Game} puzzles", made, game.Name);
            }

            var purged = await scheduler.PurgePracticeAsync(TimeSpan.FromDays(opts.PracticeRetentionDays), ct);
            if (purged > 0) logger.LogInformation("Deleted {Count} old practice rounds", purged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Filling the puzzle queues failed");
        }
    }

    public static string Describe(ImportRun run) =>
        $"Import #{run.Id} [{run.Source}] {run.Status}: " +
        $"{run.TeamsUpserted} teams, {run.PlayersInserted} new, {run.PlayersUpdated} updated, " +
        $"{run.PlayersDeactivated} deactivated, {run.TeamChanges} team changes" +
        (run.Error is null ? "" : $" — {run.Error}");
}

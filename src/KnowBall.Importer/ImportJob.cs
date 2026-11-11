using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KnowBall.Application;
using KnowBall.Application.Games;
using KnowBall.Application.Games.LetterJacket;
using KnowBall.Application.Import;
using KnowBall.Application.Telemetry;
using KnowBall.Domain;
using KnowBall.Infrastructure;
using KnowBall.Infrastructure.Persistence;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.Importer;

public sealed class ImportOptions
{
    /// <summary>Leagues to import, comma-separated: "nba", "nfl" or "nba,nfl". The settings below without a league in their name are the NBA's.</summary>
    public string Leagues { get; set; } = "nba,nfl";
    /// <summary>NBA roster source: "espn" (live), "seed" (bundled sample) or "nba-stats" (the league's own feed, down since October 2026).</summary>
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
/// One nightly run: for each league, reload the player history if it is due and refresh today's
/// rosters (with retries and, for the NBA, an optional fallback source); then top up the puzzle
/// queues. Used by --once and by the nightly worker. The roster runs are the result: the first one
/// that failed, or else the first league's. The other steps log their own outcome and never stop
/// the night's work, and one league failing never stops the other.
/// </summary>
public sealed class ImportJob(IServiceScopeFactory scopes, IOptions<ImportOptions> options, TimeProvider clock, ILogger<ImportJob> logger)
{
    public const string Nba = "nba";
    public const string Nfl = "nfl";

    public async Task<ImportRun> RunAsync(CancellationToken ct, bool forceHistory = false)
    {
        var opts = options.Value;
        var leagues = LeaguesOf(opts.Leagues);
        // One trace per night: the downloads, database work and queue top-up all hang off this span.
        using var activity = KnowBallTelemetry.Source.StartActivity("knowball.import");
        activity?.SetTag("knowball.leagues", string.Join(",", leagues.Order()));
        var runs = new List<ImportRun>();

        if (leagues.Contains(Nba))
        {
            await RunHistoryIfDueAsync(BrefCsvSource.SourceName, opts, forceHistory, ct);

            var run = await RunWithRetriesAsync(opts.Source, opts, ct);
            if (run.Status != ImportStatus.Succeeded && !string.IsNullOrWhiteSpace(opts.FallbackSource)
                && !opts.FallbackSource.Equals(opts.Source, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Primary source {Source} failed; trying fallback {Fallback}", opts.Source, opts.FallbackSource);
                run = await RunWithRetriesAsync(opts.FallbackSource, opts, ct);
            }
            runs.Add(run);
        }

        if (leagues.Contains(Nfl))
        {
            await RunHistoryIfDueAsync(NflverseHistorySource.SourceName, opts, forceHistory, ct);
            var run = await RunWithRetriesAsync(NflverseRosterSource.SourceName, opts, ct);
            logger.LogInformation("{Run}", Describe(run));
            runs.Add(run);
        }

        if (runs.Count == 0) throw new InvalidOperationException($"Import:Leagues names no league this importer knows: '{opts.Leagues}'.");

        // Yesterday's rosters still make fair puzzles, so the queues are topped up even after a failed import.
        await FillPuzzleQueuesAsync(opts, ct);
        return runs.FirstOrDefault(r => r.Status != ImportStatus.Succeeded) ?? runs[0];
    }

    /// <summary>"nba, NFL" → {nba, nfl}.</summary>
    public static HashSet<string> LeaguesOf(string? setting) =>
        (setting ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.ToLowerInvariant())
            .ToHashSet();

    private async Task<ImportRun> RunWithRetriesAsync(string sourceName, ImportOptions opts, CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        using var activity = KnowBallTelemetry.Source.StartActivity("knowball.import.roster");
        activity?.SetTag("knowball.source", sourceName);
        var run = await RunAttemptsAsync(sourceName, opts, ct);

        var tags = new TagList { { "knowball.league", run.LeagueCode }, { "knowball.source", sourceName }, { "knowball.status", run.Status.ToString() } };
        KnowBallTelemetry.ImportRuns.Add(1, tags);
        KnowBallTelemetry.ImportDuration.Record(clock.GetElapsedTime(started).TotalSeconds, tags);
        if (run.Status != ImportStatus.Succeeded) activity?.SetStatus(ActivityStatusCode.Error, run.Error);
        return run;
    }

    private async Task<ImportRun> RunAttemptsAsync(string sourceName, ImportOptions opts, CancellationToken ct)
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

    /// <summary>The full record, then (for the NBA) the id links and birthplaces that hang off it.</summary>
    private async Task RunHistoryIfDueAsync(string sourceName, ImportOptions opts, bool force, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var history = services.GetRequiredService<HistorySourceResolver>().Get(sourceName);

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
            if (run.Status != ImportStatus.Succeeded || history.Name != BrefCsvSource.SourceName) return;

            var links = await services.GetRequiredService<CrosswalkImportService>()
                .RunAsync(services.GetRequiredService<ICrosswalkSource>(), run.LeagueCode, ct);
            Console.WriteLine(Describe(links));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "History import from {Source} crashed", sourceName);
        }
    }

    private async Task FillPuzzleQueuesAsync(ImportOptions opts, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<PuzzleScheduler>();

            var registry = scope.ServiceProvider.GetRequiredService<GameRegistry>();
            foreach (var game in registry.All)
            foreach (var league in registry.LeaguesOf(game))
            {
                var made = await scheduler.FillQueueAsync(game, opts.PuzzleDaysAhead, ct, league);
                if (made > 0) logger.LogInformation("Queued {Count} {Game} puzzles", made, league == LeagueCodes.Nba ? game.Name : $"{game.Name} ({league.ToUpperInvariant()})");
            }

            var purged = await scheduler.PurgePracticeAsync(TimeSpan.FromDays(opts.PracticeRetentionDays), ct);
            if (purged > 0) logger.LogInformation("Deleted {Count} old practice rounds", purged);
            var expired = await scope.ServiceProvider.GetRequiredService<LetterJacketGameService>().PurgeExpiredAsync(ct);
            if (expired > 0) logger.LogInformation("Deleted {Count} expired LetterJacket sessions", expired);
            var numbers = await scope.ServiceProvider.GetRequiredService<NumbersPlayService>().PurgeExpiredAsync(ct);
            if (numbers > 0) logger.LogInformation("Deleted {Count} expired Numbers Don't Lie practice rounds", numbers);
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SportsTrivia.Domain;

namespace SportsTrivia.Application.Games;

/// <summary>
/// Puts puzzles on the calendar. Generated games get a new puzzle for any day that lacks one;
/// hand-made games replay an old puzzle when their queue runs dry rather than show nothing.
/// </summary>
public sealed class PuzzleScheduler(ITriviaDb db, PuzzleCalendar calendar, TimeProvider clock, ILogger<PuzzleScheduler> logger)
{
    /// <summary>A player is not the answer to the same game twice within this many days.</summary>
    public const int NoRepeatDays = 120;
    public const string DefaultLeague = "nba";

    /// <summary>Overridable so tests can make generation repeatable.</summary>
    public Random Random { get; init; } = Random.Shared;

    /// <summary>The puzzle for <paramref name="date"/>, made now if the calendar has none. Null if none can be made.</summary>
    public async Task<Puzzle?> GetOrCreateDailyAsync(IGame game, DateOnly date, CancellationToken ct, string leagueCode = DefaultLeague)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == leagueCode, ct);
        if (league is null) return null;

        var existing = await FindAsync(game, league.Id, date, ct);
        if (existing is not null) return existing;

        var puzzle = game.IsGenerated
            ? await GenerateAsync(game, league, date, await RecentAnswersAsync(game, league.Id, date, ct), ct)
            : await ReplayAsync(game, league, date, ct);
        if (puzzle is null) return null;

        puzzle.Number = await NumberForAsync(game, league.Id, date, ct);
        db.Puzzles.Add(puzzle);
        try
        {
            await db.SaveChangesAsync(ct);
            return puzzle;
        }
        catch (DbUpdateException)
        {
            // Another request made today's puzzle at the same moment; use theirs.
            db.ClearTracked();
            return await FindAsync(game, league.Id, date, ct);
        }
    }

    /// <summary>Makes sure a generated game has a puzzle for each of the next <paramref name="days"/> days. Returns how many were made.</summary>
    public async Task<int> FillQueueAsync(IGame game, int days, CancellationToken ct, string leagueCode = DefaultLeague)
    {
        if (!game.IsGenerated || days <= 0) return 0;
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == leagueCode, ct);
        if (league is null) return 0;

        var today = calendar.Today;
        var last = today.AddDays(days - 1);
        var taken = (await db.Puzzles
                .Where(p => p.GameType == game.Type && p.LeagueId == league.Id && p.Date >= today && p.Date <= last)
                .Select(p => p.Date)
                .ToListAsync(ct))
            .ToHashSet();

        var recent = await RecentAnswersAsync(game, league.Id, today, ct, spanDays: NoRepeatDays + days);
        var first = await FirstDateAsync(game, league.Id, ct);

        var made = 0;
        for (var date = today; date <= last; date = date.AddDays(1))
        {
            if (taken.Contains(date)) continue;

            var puzzle = await GenerateAsync(game, league, date, recent, ct);
            if (puzzle is null)
            {
                logger.LogWarning("{Game} has no eligible players; its queue was not filled", game.Name);
                break;
            }

            first ??= date;
            puzzle.Number = date.DayNumber - first.Value.DayNumber + 1;
            db.Puzzles.Add(puzzle);
            if (puzzle.AnswerPlayerId is { } answer) recent.Add(answer);
            made++;
        }

        if (made > 0) await db.SaveChangesAsync(ct);
        return made;
    }

    /// <summary>A one-off round that is not on the calendar and not counted in stats.</summary>
    public async Task<Puzzle?> CreatePracticeAsync(IGame game, CancellationToken ct, string leagueCode = DefaultLeague)
    {
        if (!game.IsGenerated) return null;
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == leagueCode, ct);
        if (league is null) return null;

        // Steering clear of recent and queued answers also keeps practice from spoiling a coming daily.
        var today = calendar.Today;
        var puzzle = await GenerateAsync(game, league, today, await RecentAnswersAsync(game, league.Id, today, ct), ct);
        if (puzzle is null) return null;

        puzzle.Status = PuzzleStatus.Practice;
        puzzle.Date = null;
        db.Puzzles.Add(puzzle);
        await db.SaveChangesAsync(ct);
        return puzzle;
    }

    /// <summary>Removes practice rounds older than <paramref name="age"/>, with the progress saved against them.</summary>
    public async Task<int> PurgePracticeAsync(TimeSpan age, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - age;
        var old = await db.Puzzles.Where(p => p.Status == PuzzleStatus.Practice).ToListAsync(ct);
        old = old.Where(p => p.CreatedAt < cutoff).ToList();
        if (old.Count == 0) return 0;

        db.Puzzles.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        return old.Count;
    }

    /// <summary>"#142": days since the game's first puzzle, counting from one.</summary>
    public async Task<int> NumberForAsync(IGame game, int leagueId, DateOnly date, CancellationToken ct)
    {
        var first = await FirstDateAsync(game, leagueId, ct);
        return first is null ? 1 : Math.Max(1, date.DayNumber - first.Value.DayNumber + 1);
    }

    private Task<Puzzle?> FindAsync(IGame game, int leagueId, DateOnly date, CancellationToken ct) =>
        db.Puzzles.FirstOrDefaultAsync(
            p => p.GameType == game.Type && p.LeagueId == leagueId && p.Date == date && p.Status == PuzzleStatus.Scheduled, ct);

    private async Task<DateOnly?> FirstDateAsync(IGame game, int leagueId, CancellationToken ct) =>
        await db.Puzzles
            .Where(p => p.GameType == game.Type && p.LeagueId == leagueId && p.Status == PuzzleStatus.Scheduled && p.Date != null)
            .MinAsync(p => p.Date, ct);

    private async Task<HashSet<int>> RecentAnswersAsync(IGame game, int leagueId, DateOnly date, CancellationToken ct, int spanDays = NoRepeatDays)
    {
        var from = date.AddDays(-NoRepeatDays);
        var to = date.AddDays(spanDays);
        var ids = await db.Puzzles
            .Where(p => p.GameType == game.Type && p.LeagueId == leagueId && p.Date >= from && p.Date <= to && p.AnswerPlayerId != null)
            .Select(p => p.AnswerPlayerId!.Value)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task<Puzzle?> GenerateAsync(IGame game, League league, DateOnly date, HashSet<int> recent, CancellationToken ct)
    {
        var generated = await game.GenerateAsync(new GenerationContext(league, date, recent, Random), ct);
        if (generated is null) return null;

        return new Puzzle
        {
            GameType = game.Type,
            Status = PuzzleStatus.Scheduled,
            Date = date,
            LeagueId = league.Id,
            Difficulty = generated.Difficulty,
            PayloadJson = generated.PayloadJson,
            SecretJson = generated.SecretJson,
            AnswerPlayerId = generated.AnswerPlayerId,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    /// <summary>A copy of the hand-made puzzle that has gone longest without being shown.</summary>
    private async Task<Puzzle?> ReplayAsync(IGame game, League league, DateOnly date, CancellationToken ct)
    {
        var past = await db.Puzzles.AsNoTracking()
            .Where(p => p.GameType == game.Type && p.LeagueId == league.Id && p.Status == PuzzleStatus.Scheduled && p.Date < date)
            .ToListAsync(ct);
        if (past.Count == 0) return null;

        var stalest = past
            .GroupBy(p => p.SecretJson)
            .OrderBy(g => g.Max(p => p.Date))
            .First()
            .OrderBy(p => p.Date)
            .First();

        return new Puzzle
        {
            GameType = game.Type,
            Status = PuzzleStatus.Scheduled,
            Date = date,
            LeagueId = league.Id,
            Difficulty = stalest.Difficulty,
            PayloadJson = stalest.PayloadJson,
            SecretJson = stalest.SecretJson,
            AnswerPlayerId = stalest.AnswerPlayerId,
            CreatedAt = clock.GetUtcNow(),
        };
    }
}

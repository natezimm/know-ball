using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SportsTrivia.Domain;

namespace SportsTrivia.Application.Games;

/// <summary>
/// Runs every daily game: finds the puzzle, remembers each player's guesses, enforces the guess
/// limit and attaches the answer only once the game is over. Players are known by an anonymous
/// id their browser makes up; accounts come later.
/// </summary>
public sealed class GameService(ITriviaDb db, GameRegistry games, PuzzleScheduler scheduler, PuzzleCalendar calendar, TimeProvider clock)
{
    public const int ArchiveLimit = 120;

    public async Task<GameOutcome<GameState>> GetDailyAsync(string slug, string userKey, CancellationToken ct)
    {
        if (games.Find(slug) is not { } game) return UnknownGame<GameState>();

        var puzzle = await scheduler.GetOrCreateDailyAsync(game, calendar.Today, ct);
        if (puzzle is null) return GameOutcome<GameState>.NotFound($"There is no {game.Name} puzzle today.");

        return GameOutcome<GameState>.Success(BuildState(game, puzzle, await FindResultAsync(puzzle.Id, userKey, ct)));
    }

    public async Task<GameOutcome<GameState>> GetPuzzleAsync(string slug, int puzzleId, string userKey, CancellationToken ct)
    {
        if (games.Find(slug) is not { } game) return UnknownGame<GameState>();
        if (await FindPlayableAsync(game, puzzleId, ct) is not { } puzzle) return GameOutcome<GameState>.NotFound("Puzzle not found.");

        return GameOutcome<GameState>.Success(BuildState(game, puzzle, await FindResultAsync(puzzle.Id, userKey, ct)));
    }

    public async Task<GameOutcome<GameState>> StartPracticeAsync(string slug, CancellationToken ct)
    {
        if (games.Find(slug) is not { } game) return UnknownGame<GameState>();
        if (!game.IsGenerated) return GameOutcome<GameState>.Invalid($"{game.Name} has no practice mode.");

        var puzzle = await scheduler.CreatePracticeAsync(game, ct);
        return puzzle is null
            ? GameOutcome<GameState>.NotFound($"No {game.Name} puzzle could be made.")
            : GameOutcome<GameState>.Success(BuildState(game, puzzle, null));
    }

    public async Task<GameOutcome<GameState>> GuessAsync(string slug, int puzzleId, int playerId, string userKey, CancellationToken ct)
    {
        if (games.Find(slug) is not { } game) return UnknownGame<GameState>();
        if (await FindPlayableAsync(game, puzzleId, ct) is not { } puzzle) return GameOutcome<GameState>.NotFound("Puzzle not found.");

        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId && p.LeagueId == puzzle.LeagueId, ct);
        if (player is null) return GameOutcome<GameState>.Invalid("Pick a player from the list.");

        var result = await db.UserResults.FirstOrDefaultAsync(r => r.PuzzleId == puzzle.Id && r.UserKey == userKey, ct);
        var guesses = result is null ? [] : ReadGuesses(result);

        if (result?.CompletedAt is not null) return GameOutcome<GameState>.Conflict("This puzzle is finished.");
        if (guesses.Any(g => g.PlayerId == playerId)) return GameOutcome<GameState>.Invalid($"You already guessed {player.FullName}.");

        var evaluation = await game.EvaluateAsync(puzzle, player, ct);
        guesses.Add(new GuessRecord(player.Id, player.FullName, player.HeadshotUrl, evaluation.Correct, evaluation.Feedback));

        var now = clock.GetUtcNow();
        if (result is null)
        {
            result = new UserResult { UserKey = userKey, PuzzleId = puzzle.Id, StartedAt = now };
            db.UserResults.Add(result);
        }

        result.GuessesJson = GameJson.Write(guesses);
        result.HintsUsed = guesses.Count(g => !g.Correct);
        if (evaluation.Correct)
        {
            result.Solved = true;
            result.Score = guesses.Count;
            result.CompletedAt = now;
        }
        else if (guesses.Count >= game.MaxGuesses)
        {
            result.CompletedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return GameOutcome<GameState>.Success(BuildState(game, puzzle, result));
    }

    /// <summary>Past dailies, newest first, with how this player did on each.</summary>
    public async Task<GameOutcome<IReadOnlyList<ArchiveEntry>>> GetArchiveAsync(string slug, string userKey, CancellationToken ct)
    {
        if (games.Find(slug) is not { } game) return UnknownGame<IReadOnlyList<ArchiveEntry>>();

        var today = calendar.Today;
        var puzzles = await db.Puzzles.AsNoTracking()
            .Where(p => p.GameType == game.Type && p.Status == PuzzleStatus.Scheduled && p.Date <= today)
            .OrderByDescending(p => p.Date)
            .Take(ArchiveLimit)
            .Select(p => new { p.Id, p.Number, p.Date })
            .ToListAsync(ct);

        var ids = puzzles.Select(p => p.Id).ToList();
        var results = await db.UserResults.AsNoTracking()
            .Where(r => r.UserKey == userKey && ids.Contains(r.PuzzleId))
            .ToDictionaryAsync(r => r.PuzzleId, ct);

        IReadOnlyList<ArchiveEntry> entries = puzzles
            .Select(p =>
            {
                results.TryGetValue(p.Id, out var result);
                return new ArchiveEntry(p.Id, p.Number, p.Date!.Value, StatusOf(result), result is null ? 0 : ReadGuesses(result).Count);
            })
            .ToList();
        return GameOutcome<IReadOnlyList<ArchiveEntry>>.Success(entries);
    }

    /// <summary>Played, win rate, streaks and guess distribution for each game, from daily puzzles only.</summary>
    public async Task<IReadOnlyList<GameStats>> GetStatsAsync(string userKey, CancellationToken ct)
    {
        var finished = (await db.UserResults.AsNoTracking()
                .Where(r => r.UserKey == userKey && r.Puzzle!.Status == PuzzleStatus.Scheduled && r.Puzzle.Date != null)
                .Select(r => new { r.Puzzle!.GameType, Date = r.Puzzle.Date!.Value, r.Solved, r.Score, r.CompletedAt })
                .ToListAsync(ct))
            .Where(r => r.CompletedAt is not null)
            .ToList();

        var today = calendar.Today;
        var stats = new List<GameStats>();
        foreach (var game in games.All)
        {
            var mine = finished.Where(r => r.GameType == game.Type).ToList();
            var distribution = new int[game.MaxGuesses];
            foreach (var win in mine.Where(r => r.Solved && r.Score >= 1 && r.Score <= game.MaxGuesses)) distribution[win.Score - 1]++;

            var (current, best) = Streaks(mine.Where(r => r.Solved).Select(r => r.Date), today);
            stats.Add(new GameStats(game.Slug, game.Name, game.MaxGuesses, mine.Count, mine.Count(r => r.Solved), current, best, distribution));
        }
        return stats;
    }

    /// <summary>Where this player stands on each of today's puzzles, for the home page.</summary>
    public async Task<IReadOnlyList<TodayEntry>> GetTodayAsync(string userKey, CancellationToken ct)
    {
        var entries = new List<TodayEntry>();
        foreach (var game in games.All)
        {
            var puzzle = await scheduler.GetOrCreateDailyAsync(game, calendar.Today, ct);
            if (puzzle is null)
            {
                entries.Add(new TodayEntry(game.Slug, game.Name, false, null, GameStatus.New, 0, game.MaxGuesses));
                continue;
            }

            var result = await FindResultAsync(puzzle.Id, userKey, ct);
            entries.Add(new TodayEntry(
                game.Slug, game.Name, true, puzzle.Number, StatusOf(result), result is null ? 0 : ReadGuesses(result).Count, game.MaxGuesses));
        }
        return entries;
    }

    /// <summary>Longest run of consecutive winning days, and the run that reaches today or yesterday.</summary>
    internal static (int Current, int Best) Streaks(IEnumerable<DateOnly> winDates, DateOnly today)
    {
        var days = winDates.Distinct().OrderBy(d => d).ToList();
        var best = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days)
        {
            run = previous is { } p && day.DayNumber == p.DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            previous = day;
        }

        // Today's puzzle may not be played yet, so a run that ended yesterday is still alive.
        var current = previous is { } last && last.DayNumber >= today.DayNumber - 1 ? run : 0;
        return (current, best);
    }

    private GameState BuildState(IGame game, Puzzle puzzle, UserResult? result)
    {
        var guesses = result is null ? [] : ReadGuesses(result);
        var status = StatusOf(result);
        var complete = status is GameStatus.Won or GameStatus.Lost;
        var misses = guesses.Count(g => !g.Correct);
        var isToday = puzzle.Status == PuzzleStatus.Scheduled && puzzle.Date == calendar.Today;

        return new GameState(
            Game: game.Slug,
            PuzzleId: puzzle.Id,
            Number: puzzle.Number,
            Date: puzzle.Date,
            Practice: puzzle.Status == PuzzleStatus.Practice,
            MaxGuesses: game.MaxGuesses,
            Status: status == GameStatus.New ? GameStatus.Playing : status,
            Payload: JsonNode.Parse(puzzle.PayloadJson) ?? new JsonObject(),
            Clues: game.Clues(puzzle, misses, complete),
            Guesses: guesses,
            Answer: complete ? game.Reveal(puzzle) : null,
            NextPuzzleAt: isToday ? calendar.NextPuzzleAt : null);
    }

    private static string StatusOf(UserResult? result) => result switch
    {
        null => GameStatus.New,
        { Solved: true } => GameStatus.Won,
        { CompletedAt: not null } => GameStatus.Lost,
        _ => GameStatus.Playing,
    };

    private static List<GuessRecord> ReadGuesses(UserResult result) => GameJson.Read<List<GuessRecord>>(result.GuessesJson);

    private Task<UserResult?> FindResultAsync(int puzzleId, string userKey, CancellationToken ct) =>
        db.UserResults.AsNoTracking().FirstOrDefaultAsync(r => r.PuzzleId == puzzleId && r.UserKey == userKey, ct);

    /// <summary>A puzzle that may be played now: a practice round, or a daily whose day has come.</summary>
    private async Task<Puzzle?> FindPlayableAsync(IGame game, int puzzleId, CancellationToken ct)
    {
        var puzzle = await db.Puzzles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == puzzleId && p.GameType == game.Type, ct);
        return puzzle switch
        {
            { Status: PuzzleStatus.Practice } => puzzle,
            { Status: PuzzleStatus.Scheduled, Date: { } date } when date <= calendar.Today => puzzle,
            _ => null,
        };
    }

    private static GameOutcome<T> UnknownGame<T>() => GameOutcome<T>.NotFound("Unknown game.");
}

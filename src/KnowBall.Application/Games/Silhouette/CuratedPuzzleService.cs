using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Domain;

namespace KnowBall.Application.Games.Silhouette;

public sealed record NewWhosThatPuzzle(int PlayerId, int Year, string Team, string? Caption, PhotoAttribution? Attribution = null);

/// <summary>A hand-made puzzle as the admin area lists it, answer and all.</summary>
/// <param name="State">"draft", "scheduled" (date still to come) or "published".</param>
public sealed record CuratedPuzzle(
    int Id,
    string State,
    DateOnly? Date,
    int? Number,
    int PlayerId,
    string PlayerName,
    int Year,
    string Team,
    string? Caption,
    string SilhouetteUrl,
    string BlurredUrl,
    string PhotoUrl,
    PhotoAttribution? Attribution = null);

public sealed record CuratedPreview(JsonNode? Clues, JsonNode? Answer);

/// <summary>
/// The admin side of Who's That Sportsman?: make a puzzle from an uploaded photo, put it on the
/// calendar, reorder what is coming up, or take it back off. A puzzle whose day has arrived can
/// no longer be changed, because people may already have played it.
/// </summary>
public sealed class CuratedPuzzleService(
    ITriviaDb db,
    PuzzleCalendar calendar,
    PuzzleScheduler scheduler,
    WhosThatGame game,
    IImageStore images,
    ISilhouetteRenderer renderer,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<CuratedPuzzle>> ListAsync(CancellationToken ct)
    {
        var puzzles = await db.Puzzles.AsNoTracking()
            .Where(p => p.GameType == game.Type && p.Status != PuzzleStatus.Practice)
            .ToListAsync(ct);

        // Drafts first, then what is coming up in order, then history with the newest on top.
        var today = calendar.Today;
        return puzzles
            .OrderBy(p => p.Date is null ? 0 : p.Date > today ? 1 : 2)
            .ThenBy(p => p.Date > today ? p.Date!.Value.DayNumber : -(p.Date?.DayNumber ?? 0))
            .ThenByDescending(p => p.Id)
            .Select(p => View(p, today))
            .ToList();
    }

    public async Task<GameOutcome<CuratedPuzzle>> CreateAsync(NewWhosThatPuzzle input, Stream photo, Stream cutout, CancellationToken ct)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == PuzzleScheduler.DefaultLeague, ct);
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PlayerId, ct);
        if (league is null || player is null || player.LeagueId != league.Id) return GameOutcome<CuratedPuzzle>.Invalid("Pick the player in the photo from the list.");
        if (input.Year is < 1940 or > 2100) return GameOutcome<CuratedPuzzle>.Invalid("Enter the year the photo was taken.");
        if (string.IsNullOrWhiteSpace(input.Team)) return GameOutcome<CuratedPuzzle>.Invalid("Enter the team the player was on.");

        if (Validate(input) is { } error) return GameOutcome<CuratedPuzzle>.Invalid(error);

        SilhouetteImages rendered;
        try
        {
            rendered = await renderer.RenderAsync(photo, cutout, ct);
        }
        catch (InvalidImageException ex)
        {
            return GameOutcome<CuratedPuzzle>.Invalid(ex.Message);
        }

        var secret = new WhosThatSecret(
            player.Id,
            player.FullName,
            SilhouetteUrl: await images.SaveAsync(rendered.Silhouette, ".png", ct),
            BlurredUrl: await images.SaveAsync(rendered.Blurred, ".jpg", ct),
            PhotoUrl: await images.SaveAsync(rendered.Photo, ".jpg", ct),
            input.Year,
            input.Team.Trim(),
            string.IsNullOrWhiteSpace(input.Caption) ? null : input.Caption.Trim(),
            player.HeadshotUrl,
            Normalize(input.Attribution));

        var puzzle = new Puzzle
        {
            GameType = game.Type,
            Status = PuzzleStatus.Draft,
            LeagueId = league.Id,
            AnswerPlayerId = player.Id,
            SecretJson = GameJson.Write(secret),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Puzzles.Add(puzzle);
        await db.SaveChangesAsync(ct);
        return GameOutcome<CuratedPuzzle>.Success(View(puzzle, calendar.Today));
    }

    /// <summary>Only drafts can be changed. Unschedule a future puzzle first; live puzzles stay locked.</summary>
    public async Task<GameOutcome<CuratedPuzzle>> UpdateAsync(int puzzleId, NewWhosThatPuzzle input, Stream? photo, Stream? cutout, CancellationToken ct)
    {
        var puzzle = await db.Puzzles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == puzzleId && p.GameType == game.Type && p.Status != PuzzleStatus.Practice, ct);
        if (puzzle is null) return GameOutcome<CuratedPuzzle>.NotFound("Puzzle not found.");
        if (puzzle.Status != PuzzleStatus.Draft) return GameOutcome<CuratedPuzzle>.Conflict("Only drafts can be edited. Unschedule a future puzzle first.");
        if (Validate(input) is { } error) return GameOutcome<CuratedPuzzle>.Invalid(error);
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PlayerId && p.LeagueId == puzzle.LeagueId, ct);
        if (player is null) return GameOutcome<CuratedPuzzle>.Invalid("Pick the player in the photo from the list.");
        if ((photo is null) != (cutout is null)) return GameOutcome<CuratedPuzzle>.Invalid("To replace images, choose both the photo and the cut-out.");

        var old = GameJson.Read<WhosThatSecret>(puzzle.SecretJson);
        var secret = old with
        {
            PlayerId = player.Id, Name = player.FullName, HeadshotUrl = player.HeadshotUrl,
            Year = input.Year, Team = input.Team.Trim(), Caption = Clean(input.Caption), Attribution = Normalize(input.Attribution),
        };
        var saved = new List<string>();
        try
        {
            if (photo is not null && cutout is not null)
            {
                SilhouetteImages rendered;
                try { rendered = await renderer.RenderAsync(photo, cutout, ct); }
                catch (InvalidImageException ex) { return GameOutcome<CuratedPuzzle>.Invalid(ex.Message); }
                async Task<string> Save(byte[] bytes, string extension)
                {
                    var url = await images.SaveAsync(bytes, extension, ct);
                    saved.Add(url);
                    return url;
                }
                secret = secret with
                {
                    SilhouetteUrl = await Save(rendered.Silhouette, ".png"),
                    BlurredUrl = await Save(rendered.Blurred, ".jpg"),
                    PhotoUrl = await Save(rendered.Photo, ".jpg"),
                };
            }
            var originalJson = puzzle.SecretJson;
            var updatedJson = GameJson.Write(secret);
            // Recheck at the write: another tab may schedule, delete or edit during image rendering.
            var changed = await db.Puzzles
                .Where(p => p.Id == puzzleId && p.Status == PuzzleStatus.Draft && p.SecretJson == originalJson)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.SecretJson, updatedJson)
                    .SetProperty(p => p.AnswerPlayerId, player.Id), ct);
            if (changed == 0)
            {
                foreach (var url in saved) await images.DeleteAsync(url, CancellationToken.None);
                return GameOutcome<CuratedPuzzle>.Conflict("The draft changed while saving. Reload before editing again.");
            }
            puzzle.SecretJson = updatedJson;
            puzzle.AnswerPlayerId = player.Id;
        }
        catch
        {
            // A rejected replacement must leave the old, referenced images available.
            foreach (var url in saved) await images.DeleteAsync(url, CancellationToken.None);
            throw;
        }
        if (saved.Count > 0)
            foreach (var url in new[] { old.SilhouetteUrl, old.BlurredUrl, old.PhotoUrl })
                await images.DeleteAsync(url, CancellationToken.None);
        return GameOutcome<CuratedPuzzle>.Success(View(puzzle, calendar.Today));
    }

    /// <summary>A read-only look at the same hint/reveal rules as gameplay. No user result is loaded or written.</summary>
    public async Task<GameOutcome<CuratedPreview>> PreviewAsync(int puzzleId, int misses, bool reveal, CancellationToken ct)
    {
        if (misses is < 0 or > 4) return GameOutcome<CuratedPreview>.Invalid("Preview misses must be between 0 and 4.");
        var puzzle = await db.Puzzles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == puzzleId && p.GameType == game.Type && p.Status != PuzzleStatus.Practice, ct);
        return puzzle is null
            ? GameOutcome<CuratedPreview>.NotFound("Puzzle not found.")
            : GameOutcome<CuratedPreview>.Success(new(game.Clues(puzzle, misses, reveal), reveal ? game.Reveal(puzzle) : null));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PhotoAttribution? Normalize(PhotoAttribution? value)
    {
        var result = new PhotoAttribution(Clean(value?.SourceUrl), Clean(value?.Credit), Clean(value?.License));
        return result.SourceUrl is null && result.Credit is null && result.License is null ? null : result;
    }

    private static string? Validate(NewWhosThatPuzzle input)
    {
        if (input.Year is < 1940 or > 2100) return "Enter the year the photo was taken.";
        if (string.IsNullOrWhiteSpace(input.Team) || input.Team.Trim().Length > 60) return "Enter a team of up to 60 characters.";
        if (input.Caption?.Length > 160) return "Keep the caption to 160 characters.";
        var attribution = Normalize(input.Attribution);
        if (attribution?.Credit?.Length > 200 || attribution?.License?.Length > 200) return "Keep photo credit and license to 200 characters each.";
        if (attribution?.SourceUrl is { } source && (source.Length > 2048 || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            return "Photo source must be an http or https URL without credentials, up to 2048 characters.";
        return null;
    }

    /// <summary>
    /// Puts a draft on the calendar: on <paramref name="date"/> if given and free, otherwise on
    /// the first free day from today on.
    /// </summary>
    public async Task<GameOutcome<CuratedPuzzle>> ScheduleAsync(int puzzleId, DateOnly? date, CancellationToken ct)
    {
        var puzzle = await FindAsync(puzzleId, ct);
        if (puzzle is null) return GameOutcome<CuratedPuzzle>.NotFound("Puzzle not found.");
        if (puzzle.Status != PuzzleStatus.Draft) return GameOutcome<CuratedPuzzle>.Conflict("This puzzle is already on the calendar.");

        var today = calendar.Today;
        if (date < today) return GameOutcome<CuratedPuzzle>.Invalid("Pick today or a later date.");

        var taken = (await db.Puzzles
                .Where(p => p.GameType == game.Type && p.LeagueId == puzzle.LeagueId && p.Date >= today)
                .Select(p => p.Date!.Value)
                .ToListAsync(ct))
            .ToHashSet();
        if (date is { } wanted && taken.Contains(wanted)) return GameOutcome<CuratedPuzzle>.Conflict($"{wanted:MMM d} already has a puzzle.");

        var day = date ?? today;
        while (taken.Contains(day)) day = day.AddDays(1);

        puzzle.Status = PuzzleStatus.Scheduled;
        puzzle.Date = day;
        puzzle.Number = await scheduler.NumberForAsync(game, puzzle.LeagueId, day, ct);
        await db.SaveChangesAsync(ct);
        return GameOutcome<CuratedPuzzle>.Success(View(puzzle, today));
    }

    /// <summary>Takes a puzzle that has not gone live back off the calendar.</summary>
    public async Task<GameOutcome<CuratedPuzzle>> UnscheduleAsync(int puzzleId, CancellationToken ct)
    {
        var puzzle = await FindAsync(puzzleId, ct);
        if (puzzle is null) return GameOutcome<CuratedPuzzle>.NotFound("Puzzle not found.");
        if (!IsUpcoming(puzzle)) return GameOutcome<CuratedPuzzle>.Conflict("Only a puzzle that has not gone live can be taken off the calendar.");

        puzzle.Status = PuzzleStatus.Draft;
        puzzle.Date = null;
        puzzle.Number = null;
        await db.SaveChangesAsync(ct);
        return GameOutcome<CuratedPuzzle>.Success(View(puzzle, calendar.Today));
    }

    /// <summary>Moves an upcoming puzzle one day earlier or later by trading places with its neighbour.</summary>
    public async Task<GameOutcome<CuratedPuzzle>> MoveAsync(int puzzleId, bool earlier, CancellationToken ct)
    {
        var puzzle = await FindAsync(puzzleId, ct);
        if (puzzle is null) return GameOutcome<CuratedPuzzle>.NotFound("Puzzle not found.");
        if (!IsUpcoming(puzzle)) return GameOutcome<CuratedPuzzle>.Conflict("Only upcoming puzzles can be reordered.");

        var today = calendar.Today;
        var upcoming = await db.Puzzles
            .Where(p => p.GameType == game.Type && p.LeagueId == puzzle.LeagueId && p.Status == PuzzleStatus.Scheduled && p.Date > today)
            .OrderBy(p => p.Date)
            .ToListAsync(ct);

        var index = upcoming.FindIndex(p => p.Id == puzzle.Id);
        var other = earlier ? index - 1 : index + 1;
        if (index < 0 || other < 0 || other >= upcoming.Count) return GameOutcome<CuratedPuzzle>.Conflict("There is nothing to swap with.");

        // The two keep their days and numbers and trade what they show. Nobody has played either
        // yet, and trading the contents avoids ever having two puzzles on one day.
        var neighbour = upcoming[other];
        (puzzle.SecretJson, neighbour.SecretJson) = (neighbour.SecretJson, puzzle.SecretJson);
        (puzzle.PayloadJson, neighbour.PayloadJson) = (neighbour.PayloadJson, puzzle.PayloadJson);
        (puzzle.AnswerPlayerId, neighbour.AnswerPlayerId) = (neighbour.AnswerPlayerId, puzzle.AnswerPlayerId);
        (puzzle.Difficulty, neighbour.Difficulty) = (neighbour.Difficulty, puzzle.Difficulty);
        await db.SaveChangesAsync(ct);

        // The moved puzzle now sits in the neighbour's row.
        return GameOutcome<CuratedPuzzle>.Success(View(neighbour, today));
    }

    /// <summary>Deletes a draft or upcoming puzzle and its images.</summary>
    public async Task<GameOutcome<bool>> DeleteAsync(int puzzleId, CancellationToken ct)
    {
        var puzzle = await FindAsync(puzzleId, ct);
        if (puzzle is null) return GameOutcome<bool>.NotFound("Puzzle not found.");
        if (puzzle.Status != PuzzleStatus.Draft && !IsUpcoming(puzzle))
            return GameOutcome<bool>.Conflict("A puzzle that has gone live cannot be deleted.");

        var secret = GameJson.Read<WhosThatSecret>(puzzle.SecretJson);
        db.Puzzles.Remove(puzzle);
        await db.SaveChangesAsync(ct);

        foreach (var url in new[] { secret.SilhouetteUrl, secret.BlurredUrl, secret.PhotoUrl })
            await images.DeleteAsync(url, ct);
        return GameOutcome<bool>.Success(true);
    }

    private Task<Puzzle?> FindAsync(int puzzleId, CancellationToken ct) =>
        db.Puzzles.FirstOrDefaultAsync(p => p.Id == puzzleId && p.GameType == game.Type && p.Status != PuzzleStatus.Practice, ct);

    private bool IsUpcoming(Puzzle puzzle) => puzzle.Status == PuzzleStatus.Scheduled && puzzle.Date > calendar.Today;

    private static CuratedPuzzle View(Puzzle puzzle, DateOnly today)
    {
        var secret = GameJson.Read<WhosThatSecret>(puzzle.SecretJson);
        var state = puzzle.Status == PuzzleStatus.Draft ? "draft" : puzzle.Date > today ? "scheduled" : "published";
        return new CuratedPuzzle(
            puzzle.Id, state, puzzle.Date, puzzle.Number, secret.PlayerId, secret.Name, secret.Year, secret.Team, secret.Caption,
            secret.SilhouetteUrl, secret.BlurredUrl, secret.PhotoUrl, secret.Attribution);
    }
}

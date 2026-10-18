using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using KnowBall.Application;
using KnowBall.Application.Games;
using KnowBall.Application.Games.Silhouette;
using KnowBall.Domain;

namespace KnowBall.Api.Endpoints;

/// <summary>
/// The admin area's API, for making and scheduling Who's That Sportsman? puzzles. Every request
/// must carry the key from <c>Admin:Key</c> in an <c>X-Admin-Key</c> header. With no key
/// configured the whole area answers 404, as if it were not there.
/// </summary>
public static class AdminEndpoints
{
    public const string KeyHeader = "X-Admin-Key";
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin").AddEndpointFilter(RequireKey);

        admin.MapGet("/overview", Overview)
            .WithName("AdminOverview")
            .WithSummary("How many days each game has queued, and the state of the player data.");

        var puzzles = admin.MapGroup($"/{WhosThatGame.GameSlug}/puzzles");

        puzzles.MapGet("/", async (CuratedPuzzleService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .WithName("AdminListPuzzles");

        puzzles.MapPut("/{id:int}", Update).DisableAntiforgery().WithName("AdminUpdatePuzzle");

        puzzles.MapGet("/{id:int}/preview", async (int id, int? misses, bool? reveal, CuratedPuzzleService service, CancellationToken ct) =>
            Respond(await service.PreviewAsync(id, misses ?? 0, reveal ?? false, ct)))
            .WithName("AdminPreviewPuzzle");

        puzzles.MapPost("/", Create)
            .DisableAntiforgery() // the key header, which a cross-site form cannot send, is the protection
            .WithName("AdminCreatePuzzle")
            .WithSummary("Make a draft from a photo, a cut-out of its subject, the answer and the hint facts.");

        puzzles.MapPost("/{id:int}/schedule", async (int id, ScheduleRequest? request, CuratedPuzzleService service, CancellationToken ct) =>
                Respond(await service.ScheduleAsync(id, request?.Date, ct)))
            .WithName("AdminSchedulePuzzle")
            .WithSummary("Put a draft on the calendar: on the given date, or on the first free day.");

        puzzles.MapPost("/{id:int}/unschedule", async (int id, CuratedPuzzleService service, CancellationToken ct) =>
                Respond(await service.UnscheduleAsync(id, ct)))
            .WithName("AdminUnschedulePuzzle");

        puzzles.MapPost("/{id:int}/move", async (int id, MoveRequest? request, CuratedPuzzleService service, CancellationToken ct) =>
                request?.Direction is "earlier" or "later"
                    ? Respond(await service.MoveAsync(id, request.Direction == "earlier", ct))
                    : GameEndpoints.Error(GameFailure.Invalid, "Direction must be 'earlier' or 'later'."))
            .WithName("AdminMovePuzzle");

        puzzles.MapDelete("/{id:int}", async (int id, CuratedPuzzleService service, CancellationToken ct) =>
            {
                var outcome = await service.DeleteAsync(id, ct);
                return outcome.Ok ? Results.NoContent() : GameEndpoints.Error(outcome.Failure, outcome.Error);
            })
            .WithName("AdminDeletePuzzle");

        return api;
    }

    private static async ValueTask<object?> RequireKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expected = http.RequestServices.GetRequiredService<IConfiguration>()["Admin:Key"];
        if (string.IsNullOrEmpty(expected)) return Results.NotFound();

        var given = http.Request.Headers[KeyHeader].ToString();
        var match = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        return match ? await next(context) : Results.Json(new ApiError("Wrong admin key."), statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> Create(HttpRequest request, CuratedPuzzleService service, CancellationToken ct)
    {
        if (!request.HasFormContentType) return GameEndpoints.Error(GameFailure.Invalid, "Send the puzzle as a form with two image files.");
        var form = await request.ReadFormAsync(ct);

        var photo = form.Files.GetFile("photo");
        var cutout = form.Files.GetFile("cutout");
        if (photo is null || cutout is null) return GameEndpoints.Error(GameFailure.Invalid, "Choose both the photo and the cut-out.");
        if (photo.Length > MaxUploadBytes || cutout.Length > MaxUploadBytes)
            return GameEndpoints.Error(GameFailure.Invalid, "Each image must be under 20 MB.");
        if (!int.TryParse(form["playerId"], out var playerId)) return GameEndpoints.Error(GameFailure.Invalid, "Pick the player in the photo from the list.");
        if (!int.TryParse(form["year"], out var year)) return GameEndpoints.Error(GameFailure.Invalid, "Enter the year the photo was taken.");

        await using var photoStream = photo.OpenReadStream();
        await using var cutoutStream = cutout.OpenReadStream();
        var outcome = await service.CreateAsync(
            new NewWhosThatPuzzle(playerId, year, form["team"].ToString(), form["caption"].ToString(), Attribution(form)), photoStream, cutoutStream, ct);
        return outcome.Ok
            ? Results.Json(outcome.Result, statusCode: StatusCodes.Status201Created)
            : GameEndpoints.Error(outcome.Failure, outcome.Error);
    }

    private static PhotoAttribution Attribution(IFormCollection form) =>
        new(form["sourceUrl"].ToString(), form["credit"].ToString(), form["license"].ToString());

    private static async Task<IResult> Update(int id, HttpRequest request, CuratedPuzzleService service, CancellationToken ct)
    {
        if (!request.HasFormContentType) return GameEndpoints.Error(GameFailure.Invalid, "Send the puzzle as a form.");
        var form = await request.ReadFormAsync(ct);
        if (!int.TryParse(form["playerId"], out var playerId)) return GameEndpoints.Error(GameFailure.Invalid, "Pick the player in the photo from the list.");
        if (!int.TryParse(form["year"], out var year)) return GameEndpoints.Error(GameFailure.Invalid, "Enter the year the photo was taken.");
        var photo = form.Files.GetFile("photo");
        var cutout = form.Files.GetFile("cutout");
        // Browsers may include empty file inputs when keeping the existing images.
        if (photo?.Length == 0) photo = null;
        if (cutout?.Length == 0) cutout = null;
        if (photo?.Length > MaxUploadBytes || cutout?.Length > MaxUploadBytes)
            return GameEndpoints.Error(GameFailure.Invalid, "Each image must be under 20 MB.");
        await using var photoStream = photo?.OpenReadStream();
        await using var cutoutStream = cutout?.OpenReadStream();
        return Respond(await service.UpdateAsync(id,
            new NewWhosThatPuzzle(playerId, year, form["team"].ToString(), form["caption"].ToString(), Attribution(form)), photoStream, cutoutStream, ct));
    }

    private static async Task<IResult> Overview(ITriviaDb db, GameRegistry games, PuzzleCalendar calendar, CancellationToken ct)
    {
        var today = calendar.Today;
        var queued = await db.Puzzles.AsNoTracking()
            .Where(p => p.Status == PuzzleStatus.Scheduled && p.Date >= today)
            .GroupBy(p => p.GameType)
            .Select(g => new { GameType = g.Key, Days = g.Count(), Last = g.Max(p => p.Date) })
            .ToListAsync(ct);

        // Counts only: the admin is usually also a player, so answers of generated games stay out of sight.
        var queue = games.All.Select(game =>
        {
            var q = queued.FirstOrDefault(x => x.GameType == game.Type);
            return new { game = game.Slug, name = game.Name, generated = game.IsGenerated, daysQueued = q?.Days ?? 0, queuedThrough = q?.Last };
        });

        var runs = (await db.ImportRuns.AsNoTracking().OrderByDescending(r => r.Id).Take(40).ToListAsync(ct))
            .GroupBy(r => r.Source)
            .Select(g => g.First())
            .Select(r => new { source = r.Source, status = r.Status.ToString(), finishedAt = r.FinishedAt, error = r.Error });

        return Results.Ok(new
        {
            today,
            games = queue,
            data = new
            {
                players = await db.Players.CountAsync(ct),
                activePlayers = await db.Players.CountAsync(p => p.IsActive, ct),
                playersWithHeadshots = await db.Players.CountAsync(p => p.HeadshotUrl != null, ct),
                teams = await db.Teams.CountAsync(ct),
            },
            imports = runs,
        });
    }

    private static IResult Respond<T>(GameOutcome<T> outcome) =>
        outcome.Ok ? Results.Ok(outcome.Result) : GameEndpoints.Error(outcome.Failure, outcome.Error);

    private sealed record ScheduleRequest(DateOnly? Date);

    private sealed record MoveRequest(string? Direction);
}

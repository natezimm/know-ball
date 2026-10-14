using System.Text.RegularExpressions;
using KnowBall.Application.Games;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Games.Silhouette;

namespace KnowBall.Api.Endpoints;

/// <summary>
/// The daily games. Every request names its player with an <c>X-Anon-Id</c> header: a random id
/// the browser makes up once and keeps. Responses never carry a puzzle's answer until that
/// player has finished it.
/// </summary>
public static partial class GameEndpoints
{
    public const string AnonHeader = "X-Anon-Id";

    // Named in the route so "/api/{game}/..." cannot swallow other endpoints.
    private const string Game = "{game:regex(^(" + ScoutingReportGame.GameSlug + "|" + CareerPathGame.GameSlug + "|" + WhosThatGame.GameSlug + ")$)}";

    public static IEndpointRouteBuilder MapGames(this IEndpointRouteBuilder api)
    {
        api.MapGet($"/{Game}/daily", (string game, HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => Respond(await service.GetDailyAsync(game, user, ct))))
            .WithName("DailyPuzzle")
            .WithSummary("Today's puzzle with this player's progress. The answer is absent until the game is over.");

        api.MapGet($"/{Game}/puzzles/{{puzzleId:int}}", (string game, int puzzleId, HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => Respond(await service.GetPuzzleAsync(game, puzzleId, user, ct))))
            .WithName("PuzzleById")
            .WithSummary("A past daily or a practice round, with this player's progress.");

        api.MapPost($"/{Game}/{{puzzleId:int}}/guess", (string game, int puzzleId, GuessRequest? request, HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => request?.PlayerId is { } playerId
                    ? Respond(await service.GuessAsync(game, puzzleId, playerId, user, ct))
                    : Error(GameFailure.Invalid, "Pick a player from the list.")))
            .RequireRateLimiting(RateLimits.GamePlay)
            .WithName("SubmitGuess")
            .WithSummary("Guess a player. Returns the puzzle with the new guess judged.");

        api.MapPost($"/{Game}/practice", (string game, HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async _ => Respond(await service.StartPracticeAsync(game, ct))))
            .RequireRateLimiting(RateLimits.GamePlay)
            .WithName("StartPractice")
            .WithSummary("Start a practice round: a fresh puzzle that is not counted in stats.");

        api.MapGet($"/{Game}/archive", (string game, HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => Respond(await service.GetArchiveAsync(game, user, ct))))
            .WithName("PuzzleArchive")
            .WithSummary("Past daily puzzles, newest first, with how this player did on each.");

        api.MapGet("/me/stats", (HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => Results.Ok(await service.GetStatsAsync(user, ct))))
            .WithName("MyStats")
            .WithSummary("Played, wins, streaks and guess distribution for each daily game.");

        api.MapGet("/me/today", (HttpContext http, GameService service, CancellationToken ct) =>
                WithPlayer(http, async user => Results.Ok(await service.GetTodayAsync(user, ct))))
            .WithName("MyToday")
            .WithSummary("Where this player stands on each of today's puzzles.");

        return api;
    }

    private static Task<IResult> WithPlayer(HttpContext http, Func<string, Task<IResult>> handle)
    {
        var id = http.Request.Headers[AnonHeader].ToString();
        return AnonId().IsMatch(id)
            ? handle(id)
            : Task.FromResult(Error(GameFailure.Invalid, $"Send your player id in the {AnonHeader} header."));
    }

    private static IResult Respond<T>(GameOutcome<T> outcome) =>
        outcome.Ok ? Results.Ok(outcome.Result) : Error(outcome.Failure, outcome.Error);

    internal static IResult Error(GameFailure failure, string? error) =>
        Results.Json(new ApiError(error ?? "Request failed"), statusCode: failure switch
        {
            GameFailure.NotFound => StatusCodes.Status404NotFound,
            GameFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex AnonId();

    private sealed record GuessRequest(int? PlayerId);
}

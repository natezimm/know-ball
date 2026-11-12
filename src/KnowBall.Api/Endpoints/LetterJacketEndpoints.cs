using System.Text.Json.Serialization;
using KnowBall.Application.Games.LetterJacket;

namespace KnowBall.Api.Endpoints;

/// <summary>
/// LetterJacket, the sports word game. Same contract as Nerdle's API: start a game, post guesses,
/// ask for hints. Errors are returned as <c>{ "error": "..." }</c>.
/// </summary>
public static class LetterJacketEndpoints
{
    public static IEndpointRouteBuilder MapLetterJacket(this IEndpointRouteBuilder api)
    {
        var games = api.MapGroup("/letter-jacket/games").PausedWhenOff("letter-jacket");

        games.MapPost("/", async (StartGameRequest? request, LetterJacketGameService service, CancellationToken ct) =>
                Results.Json(await service.CreateGameAsync(request?.WordLength, ct), statusCode: StatusCodes.Status201Created))
            .RequireRateLimiting(RateLimits.LetterJacketStart)
            .WithName("StartLetterJacketGame")
            .WithSummary("Start a game with a random 4, 5 or 6 letter sports word. The answer is not returned.");

        games.MapGet("/{gameId}", async (string gameId, LetterJacketGameService service, CancellationToken ct) =>
            {
                var outcome = await service.GetGameAsync(gameId, ct);
                if (!outcome.Ok) return Failure(outcome.Failure, outcome.Error);
                var state = outcome.Result!;
                return Results.Ok(new
                {
                    state.GameId, state.WordLength, state.MaxAttempts,
                    Guesses = state.Guesses.Select(g => new { g.RequestId, g.Word,
                        Score = g.Score.Select(s => s.ToString().ToLowerInvariant()).ToArray() }),
                    state.Complete, state.Won, state.Answer,
                    LetterHint = state.LetterHint is null ? null : HintResponse.From(state.LetterHint),
                });
            })
            .WithName("ResumeLetterJacketGame");

        games.MapPost("/{gameId}/guesses", async (string gameId, GuessRequest? request, LetterJacketGameService service, CancellationToken ct) =>
            {
                var outcome = await service.SubmitGuessAsync(gameId, request?.Word, request?.RequestId, ct);
                return outcome.Ok ? Results.Ok(GuessResponse.From(outcome.Result!)) : Failure(outcome.Failure, outcome.Error);
            })
            .RequireRateLimiting(RateLimits.LetterJacketPlay)
            .WithName("SubmitLetterJacketGuess")
            .WithSummary("Score a guess. Words that are not in the dictionary do not use an attempt.");

        games.MapPost("/{gameId}/hint", async (string gameId, HintRequest? request, LetterJacketGameService service, CancellationToken ct) =>
            {
                if (!TryParseHintType(request?.Type, out var type))
                    return Failure(LetterJacketFailure.Invalid, "Unknown hint type");

                var outcome = await service.GetHintAsync(gameId, type, ct);
                return outcome.Ok ? Results.Ok(HintResponse.From(outcome.Result!)) : Failure(outcome.Failure, outcome.Error);
            })
            .RequireRateLimiting(RateLimits.LetterJacketPlay)
            .WithName("GetLetterJacketHint")
            .WithSummary("Reveal the answer's category, or one letter the player has not placed yet.");

        return api;
    }

    private static bool TryParseHintType(string? value, out LetterJacketHintType type)
    {
        switch (value)
        {
            case null or "letter":
                type = LetterJacketHintType.Letter;
                return true;
            case "category":
                type = LetterJacketHintType.Category;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static IResult Failure(LetterJacketFailure failure, string? error) =>
        Results.Json(new ApiError(error ?? "Request failed"), statusCode: failure switch
        {
            LetterJacketFailure.NotFound => StatusCodes.Status404NotFound,
            LetterJacketFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });

    private sealed record StartGameRequest(int? WordLength);

    private sealed record GuessRequest(string? Word, Guid? RequestId);

    private sealed record HintRequest(string? Type);

    // An invalid word is reported as just { "valid": false }, and the answer only appears once the game is over.
    private sealed record GuessResponse(
        bool Valid,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Score,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Won,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Complete,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AttemptsRemaining,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Answer)
    {
        public static GuessResponse From(LetterJacketGuessResult result) => result.Valid
            ? new(
                true,
                result.Score!.Select(s => s.ToString().ToLowerInvariant()).ToArray(),
                result.Won,
                result.Complete,
                result.AttemptsRemaining,
                result.Answer)
            : new(false, null, null, null, null, null);
    }

    private sealed record HintResponse(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Position,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Letter)
    {
        public static HintResponse From(LetterJacketHint hint) => new(hint.Category, hint.Position, hint.Letter?.ToString());
    }
}

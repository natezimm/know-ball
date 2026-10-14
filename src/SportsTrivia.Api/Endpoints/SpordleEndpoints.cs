using System.Text.Json.Serialization;
using SportsTrivia.Application.Games.Spordle;

namespace SportsTrivia.Api.Endpoints;

/// <summary>
/// Spordle, the sports word game. Same contract as Nerdle's API: start a game, post guesses,
/// ask for hints. Errors are returned as <c>{ "error": "..." }</c>.
/// </summary>
public static class SpordleEndpoints
{
    public static IEndpointRouteBuilder MapSpordle(this IEndpointRouteBuilder api)
    {
        var games = api.MapGroup("/spordle/games");

        games.MapPost("/", (StartGameRequest? request, SpordleGameService service) =>
                Results.Json(service.CreateGame(request?.WordLength), statusCode: StatusCodes.Status201Created))
            .RequireRateLimiting(RateLimits.SpordleStart)
            .WithName("StartSpordleGame")
            .WithSummary("Start a game with a random 4, 5 or 6 letter sports word. The answer is not returned.");

        games.MapPost("/{gameId}/guesses", (string gameId, GuessRequest? request, SpordleGameService service) =>
            {
                var outcome = service.SubmitGuess(gameId, request?.Word);
                return outcome.Ok ? Results.Ok(GuessResponse.From(outcome.Result!)) : Failure(outcome.Failure, outcome.Error);
            })
            .RequireRateLimiting(RateLimits.SpordlePlay)
            .WithName("SubmitSpordleGuess")
            .WithSummary("Score a guess. Words that are not in the dictionary do not use an attempt.");

        games.MapPost("/{gameId}/hint", (string gameId, HintRequest? request, SpordleGameService service) =>
            {
                if (!TryParseHintType(request?.Type, out var type))
                    return Failure(SpordleFailure.Invalid, "Unknown hint type");

                var outcome = service.GetHint(gameId, type);
                return outcome.Ok ? Results.Ok(HintResponse.From(outcome.Result!)) : Failure(outcome.Failure, outcome.Error);
            })
            .RequireRateLimiting(RateLimits.SpordlePlay)
            .WithName("GetSpordleHint")
            .WithSummary("Reveal the answer's category, or one letter the player has not placed yet.");

        return api;
    }

    private static bool TryParseHintType(string? value, out SpordleHintType type)
    {
        switch (value)
        {
            case null or "letter":
                type = SpordleHintType.Letter;
                return true;
            case "category":
                type = SpordleHintType.Category;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static IResult Failure(SpordleFailure failure, string? error) =>
        Results.Json(new ApiError(error ?? "Request failed"), statusCode: failure switch
        {
            SpordleFailure.NotFound => StatusCodes.Status404NotFound,
            SpordleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });

    private sealed record StartGameRequest(int? WordLength);

    private sealed record GuessRequest(string? Word);

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
        public static GuessResponse From(SpordleGuessResult result) => result.Valid
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
        public static HintResponse From(SpordleHint hint) => new(hint.Category, hint.Position, hint.Letter?.ToString());
    }
}

using KnowBall.Application.Games.BackOfTheCard;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Games.TeamPhoto;
using Microsoft.FeatureManagement;

namespace KnowBall.Api.Endpoints;

/// <summary>
/// One feature flag per game, so a game whose data has gone wrong can be taken off the site with a
/// setting and a restart rather than a release. A paused game's endpoints answer 503 and the home page
/// shows its card as taking a break; every other game carries on. The flags live in the
/// <c>FeatureManagement</c> section and are all on in <c>appsettings.json</c>, because an undeclared flag
/// reads as off.
/// </summary>
public static class GamePauses
{
    /// <summary>Each game's URL slug and the name of its flag.</summary>
    public static readonly IReadOnlyDictionary<string, string> Flags = new Dictionary<string, string>
    {
        ["letter-jacket"] = "LetterJacket",
        [ScoutingReportGame.GameSlug] = "ScoutingReport",
        [CareerPathGame.GameSlug] = "CareerPath",
        [BackOfTheCardGame.GameSlug] = "BackOfTheCard",
        [TeamPhotoGame.GameSlug] = "TeamPhoto",
        ["numbers-dont-lie"] = "NumbersDontLie",
    };

    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["letter-jacket"] = "Letter Jacket",
        [ScoutingReportGame.GameSlug] = "Scouting Report",
        [CareerPathGame.GameSlug] = "Career Path",
        [BackOfTheCardGame.GameSlug] = "Back of the Card",
        [TeamPhotoGame.GameSlug] = "Team Photo",
        ["numbers-dont-lie"] = "Numbers Don't Lie",
    };

    public static IEndpointRouteBuilder MapGamePauses(this IEndpointRouteBuilder api)
    {
        api.MapGet("/games/paused", async (IFeatureManager features) =>
            {
                var paused = new List<string>();
                foreach (var (slug, flag) in Flags)
                    if (!await features.IsEnabledAsync(flag)) paused.Add(slug);
                return Results.Ok(paused);
            })
            .WithName("PausedGames")
            .WithSummary("The slugs of the games that are paused for now. Usually empty.");
        return api;
    }

    /// <summary>
    /// Answers 503 for these endpoints while the game's flag is off. Without <paramref name="slug"/> the
    /// game comes from the route's <c>{game}</c> value.
    /// </summary>
    public static TBuilder PausedWhenOff<TBuilder>(this TBuilder builder, string? slug = null)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var game = slug ?? context.HttpContext.GetRouteValue("game") as string;
            if (game is null || !Flags.TryGetValue(game, out var flag)) return await next(context);
            var features = context.HttpContext.RequestServices.GetRequiredService<IFeatureManager>();
            return await features.IsEnabledAsync(flag)
                ? await next(context)
                : Results.Json(new ApiError($"{Names[game]} is taking a short break. The other games are still on."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });
}

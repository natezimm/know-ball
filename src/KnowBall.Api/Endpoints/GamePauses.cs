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
/// <c>FeatureManagement</c> section. A game whose flag is not set at all is on: the feature manager
/// reads an undeclared flag as off, and a missing setting must never take a game down.
/// </summary>
public static class GamePauses
{
    /// <summary>Each game's URL slug and the name of its flag.</summary>
    // Routes match slugs ignoring case, so these lookups do too.
    public static readonly IReadOnlyDictionary<string, string> Flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["letter-jacket"] = "LetterJacket",
        [ScoutingReportGame.GameSlug] = "ScoutingReport",
        [CareerPathGame.GameSlug] = "CareerPath",
        [BackOfTheCardGame.GameSlug] = "BackOfTheCard",
        [TeamPhotoGame.GameSlug] = "TeamPhoto",
        ["numbers-dont-lie"] = "NumbersDontLie",
    };

    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
        api.MapGet("/games/paused", async (IFeatureManager features, IConfiguration configuration) =>
            {
                var paused = new List<string>();
                foreach (var (slug, flag) in Flags)
                    if (await IsPausedAsync(flag, features, configuration)) paused.Add(slug);
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
            var services = context.HttpContext.RequestServices;
            return !await IsPausedAsync(flag, services.GetRequiredService<IFeatureManager>(), services.GetRequiredService<IConfiguration>())
                ? await next(context)
                : Results.Json(new ApiError($"{Names[game]} is taking a short break. The other games are still on."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

    private static async Task<bool> IsPausedAsync(string flag, IFeatureManager features, IConfiguration configuration) =>
        configuration.GetSection($"FeatureManagement:{flag}").Exists() && !await features.IsEnabledAsync(flag);
}

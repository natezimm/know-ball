using System.Threading.RateLimiting;

namespace KnowBall.Api.Endpoints;

/// <summary>Every error the API reports itself has this shape: <c>{ "error": "..." }</c>.</summary>
public sealed record ApiError(string Error);

/// <summary>
/// Per-address request limits. Each is a number of requests a minute, read from configuration
/// when a request arrives. Behind a reverse proxy, forwarded headers must be enabled
/// (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true) or every player shares one address.
/// </summary>
public static class RateLimits
{
    /// <summary><c>Spordle:GamesPerMinute</c>, default 30.</summary>
    public const string SpordleStart = "spordle-start";
    /// <summary><c>Spordle:GuessesPerMinute</c>, default 20; shared by guesses and hints.</summary>
    public const string SpordlePlay = "spordle-play";
    /// <summary><c>Games:GuessesPerMinute</c>, default 60; guesses and practice rounds in the daily games.</summary>
    public const string GamePlay = "game-play";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError("Too many requests, please slow down."), cancellationToken);
            };
            options.AddPolicy(SpordleStart, http => PerMinute(http, "Spordle:GamesPerMinute", 30));
            options.AddPolicy(SpordlePlay, http => PerMinute(http, "Spordle:GuessesPerMinute", 20));
            options.AddPolicy(GamePlay, http => PerMinute(http, "Games:GuessesPerMinute", 60));
        });

    private static RateLimitPartition<string> PerMinute(HttpContext http, string settingKey, int fallback)
    {
        var limit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue(settingKey, fallback);
        return RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
    }
}

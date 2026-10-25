using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

namespace KnowBall.Api.Endpoints;

/// <summary>Every error the API reports itself has this shape: <c>{ "error": "..." }</c>.</summary>
public sealed record ApiError(string Error);

/// <summary>
/// Per-address request limits. Each is a number of requests a minute, read from configuration
/// when a request arrives. Behind a reverse proxy, the proxy must be trusted through
/// ForwardedHeaders:KnownNetworks (see Program.cs) or every player shares one address.
/// </summary>
public static class RateLimits
{
    /// <summary><c>Spordle:GamesPerMinute</c>, default 30.</summary>
    public const string SpordleStart = "spordle-start";
    /// <summary><c>Spordle:GuessesPerMinute</c>, default 20; shared by guesses and hints.</summary>
    public const string SpordlePlay = "spordle-play";
    /// <summary><c>Games:GuessesPerMinute</c>, default 60; guesses and practice rounds in the daily games.</summary>
    public const string GamePlay = "game-play";

    /// <summary>
    /// <c>RateLimits:RequestsPerMinute</c>, default 300: every request to the site, pages and files included,
    /// before the stricter limits above. A first visit loads a few dozen files, so only scripts reach it.
    /// 0 turns it off. <c>/health</c> is never limited, so a busy address cannot make the site look down.
    /// </summary>
    public const string RequestsPerMinuteKey = "RateLimits:RequestsPerMinute";
    private const int DefaultRequestsPerMinute = 300;

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                // Every window is a minute, so a minute is the most anyone waits.
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
                response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await response.WriteAsJsonAsync(new ApiError("Too many requests, please slow down."), cancellationToken);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var limit = http.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue(RequestsPerMinuteKey, DefaultRequestsPerMinute);
                if (limit <= 0 || http.Request.Path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("unlimited");
                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(http),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
            });
            options.AddPolicy(SpordleStart, http => PerMinute(http, "Spordle:GamesPerMinute", 30));
            options.AddPolicy(SpordlePlay, http => PerMinute(http, "Spordle:GuessesPerMinute", 20));
            options.AddPolicy(GamePlay, http => PerMinute(http, "Games:GuessesPerMinute", 60));
        });

    private static RateLimitPartition<string> PerMinute(HttpContext http, string settingKey, int fallback)
    {
        var limit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue(settingKey, fallback);
        return RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(http),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1) });
    }

    /// <summary>
    /// The visitor's address, after forwarded headers. One IPv6 connection is usually given a whole /64,
    /// so IPv6 addresses count by that prefix; otherwise a visitor could take a fresh address per request.
    /// </summary>
    internal static string ClientKey(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || IPAddress.IsLoopback(address)) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}

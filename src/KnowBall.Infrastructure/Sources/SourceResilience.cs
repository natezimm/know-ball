using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace KnowBall.Infrastructure.Sources;

/// <summary>How the importer's data sources retry one failed request (Sources:Retry).</summary>
public sealed class SourceRetryOptions
{
    /// <summary>Retries after the first try, for a 5xx, 408, 429, a dropped connection or a timed-out attempt.</summary>
    public int Retries { get; set; } = 2;

    /// <summary>The first wait, doubled for each retry with jitter. A Retry-After header wins when it says longer.</summary>
    public double BaseDelaySeconds { get; set; } = 2;

    /// <summary>The longest a single wait may be, Retry-After included.</summary>
    public double MaxDelaySeconds { get; set; } = 30;
}

public static class SourceResilience
{
    /// <summary>
    /// Retries a source's transient failures one request at a time, so a blip on one page of a roster
    /// does not cost the whole import attempt that <c>Import:MaxAttempts</c> would otherwise repeat.
    /// Each attempt gets the source's own <c>TimeoutSeconds</c>. <see cref="HttpClient.Timeout"/> bounds
    /// the whole call, every attempt and wait and the reading of the body, which HttpClient does after
    /// this handler returns, so a server that stalls part way through a body cannot hold the import.
    /// There is no circuit breaker: a run makes a few dozen calls a night, and the job-level retries
    /// already space out a longer outage.
    /// </summary>
    public static IHttpClientBuilder AddSourceResilience(this IHttpClientBuilder builder, Func<IServiceProvider, int> timeoutSeconds)
    {
        builder.ConfigureHttpClient((services, http) =>
        {
            var (attempt, retries, maxDelay) = Budget(services, timeoutSeconds);
            http.Timeout = attempt * (retries + 1) + maxDelay * retries;
        });
        builder.AddResilienceHandler("source", (pipeline, context) =>
        {
            var (attempt, retries, maxDelay) = Budget(context.ServiceProvider, timeoutSeconds);
            var retry = context.ServiceProvider.GetRequiredService<IOptions<SourceRetryOptions>>().Value;
            if (retries > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(Math.Max(0, retry.BaseDelaySeconds)),
                    MaxDelay = maxDelay,
                    // Polly's own Retry-After handling is not held to MaxDelay, so read the header here.
                    ShouldRetryAfterHeader = false,
                    DelayGenerator = args => ValueTask.FromResult(RetryAfter(args.Outcome.Result, maxDelay, DateTimeOffset.UtcNow)),
                });
            }
            pipeline.AddTimeout(attempt);
        });
        return builder;
    }

    /// <summary>
    /// The wait a response's Retry-After asks for, held to <paramref name="maxDelay"/>; null when it
    /// asks for none, so the exponential backoff decides.
    /// </summary>
    internal static TimeSpan? RetryAfter(HttpResponseMessage? response, TimeSpan maxDelay, DateTimeOffset now)
    {
        var header = response?.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - now : null);
        if (wait is not { } asked)
        {
            return null;
        }
        return asked < TimeSpan.Zero ? TimeSpan.Zero : asked > maxDelay ? maxDelay : asked;
    }

    private static (TimeSpan Attempt, int Retries, TimeSpan MaxDelay) Budget(IServiceProvider services, Func<IServiceProvider, int> timeoutSeconds)
    {
        var retry = services.GetRequiredService<IOptions<SourceRetryOptions>>().Value;
        return (TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds(services))),
            Math.Max(0, retry.Retries),
            TimeSpan.FromSeconds(Math.Max(0, retry.MaxDelaySeconds)));
    }
}

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
    /// Each attempt gets the source's own <c>TimeoutSeconds</c>; <see cref="HttpClient.Timeout"/> is
    /// turned off because it would otherwise cover every attempt together and cut the retries short.
    /// There is no circuit breaker: a run makes a few dozen calls a night, and the job-level retries
    /// already space out a longer outage.
    /// </summary>
    public static IHttpClientBuilder AddSourceResilience(this IHttpClientBuilder builder, Func<IServiceProvider, int> timeoutSeconds)
    {
        builder.ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddResilienceHandler("source", (pipeline, context) =>
        {
            var retry = context.ServiceProvider.GetRequiredService<IOptions<SourceRetryOptions>>().Value;
            var attempt = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds(context.ServiceProvider)));
            var retries = Math.Max(0, retry.Retries);
            var maxDelay = TimeSpan.FromSeconds(Math.Max(0, retry.MaxDelaySeconds));

            // Outermost: a bound on the whole call, every attempt and wait together.
            pipeline.AddTimeout(attempt * (retries + 1) + maxDelay * retries);
            if (retries > 0)
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = retries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(Math.Max(0, retry.BaseDelaySeconds)),
                    MaxDelay = maxDelay,
                    ShouldRetryAfterHeader = true,
                });
            }
            pipeline.AddTimeout(attempt);
        });
        return builder;
    }
}

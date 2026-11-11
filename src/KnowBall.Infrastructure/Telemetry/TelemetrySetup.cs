using KnowBall.Application.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KnowBall.Infrastructure.Telemetry;

/// <summary>
/// Sends traces, metrics and logs over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, and does
/// nothing at all when it is not, so tests, local runs and a fresh clone need no collector. The
/// standard OpenTelemetry variables configure the rest (<c>OTEL_EXPORTER_OTLP_HEADERS</c>,
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c>), so
/// the same image can point at Grafana Cloud in production or the Aspire dashboard on a laptop.
/// See docs/observability.md and ADR 0007.
/// </summary>
public static class TelemetrySetup
{
    /// <summary>The share of traces kept when no parent decided: <c>Telemetry:TraceSampleRatio</c>.</summary>
    public const double DefaultTraceSampleRatio = 0.25;

    /// <param name="serviceName">The default service.name; <c>OTEL_SERVICE_NAME</c> overrides it.</param>
    /// <param name="tracing">Host-specific instrumentation, such as ASP.NET Core for the API.</param>
    /// <param name="metrics">Host-specific meters.</param>
    public static bool AddKnowBallTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        // The exporter's own settings are environment variables that exist before the host is built,
        // so reading this one here sees the same value the exporter will.
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) return false;

        var ratio = builder.Configuration.GetValue("Telemetry:TraceSampleRatio", DefaultTraceSampleRatio);
        var version = typeof(TelemetrySetup).Assembly.GetName().Version?.ToString();

        builder.Logging.AddOpenTelemetry(logs =>
        {
            logs.IncludeScopes = true;
            logs.IncludeFormattedMessage = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(t =>
            {
                // The ratio decides for requests that arrive without a sampled parent. A browser's
                // traceparent (the web app sends one so its errors can name a trace) is not trusted
                // to force sampling, or any visitor could make every request expensive.
                var sampler = new TraceIdRatioBasedSampler(ratio);
                t.SetSampler(new ParentBasedSampler(sampler,
                    remoteParentSampled: sampler, remoteParentNotSampled: sampler));
                t.AddSource(KnowBallTelemetry.Name);
                t.AddSource("Npgsql");
                t.AddHttpClientInstrumentation();
                tracing?.Invoke(t);
            })
            .WithMetrics(m =>
            {
                m.AddMeter(KnowBallTelemetry.Name);
                m.AddMeter("Npgsql");
                m.AddRuntimeInstrumentation();
                m.AddHttpClientInstrumentation();
                metrics?.Invoke(m);
            })
            .WithLogging()
            .UseOtlpExporter();
        return true;
    }

    /// <summary>
    /// Builds the tracer and meter providers now. A host that runs builds them when it starts; a
    /// one-shot command that never starts its host has to ask, or nothing it does is recorded.
    /// </summary>
    public static void StartProviders(IServiceProvider services)
    {
        services.GetService<TracerProvider>();
        services.GetService<MeterProvider>();
    }
}

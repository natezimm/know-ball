# 0007. OpenTelemetry, sent straight to a free hosted backend

**Status:** accepted

## Context

Know Ball had a health check and console logs, but no way to see how long requests take, which queries are
slow, or how a nightly import spent its time. It shares a small VM with other sites, so anything that runs
beside it has to be small, and the cost should stay at zero.

## Decision

- Instrument with OpenTelemetry: ASP.NET Core, HttpClient, Npgsql and runtime instrumentation, plus Know Ball's
  own `ActivitySource` and `Meter` in Application (`KnowBallTelemetry`), which need only the base library.
- Export over OTLP straight from each process to Grafana Cloud's free tier. No collector, Prometheus, Loki or
  Jaeger runs on the VM: together they would need several hundred megabytes.
- Turn it on with the standard `OTEL_EXPORTER_OTLP_ENDPOINT` variable and do nothing without it, so tests and
  a fresh clone need nothing extra. The same variables point a laptop at the Aspire dashboard.
- Sample a quarter of traces by default and decide on the server, ignoring a browser's sampling flag.
- Write console logs as JSON in production, so they stay readable by a machine when telemetry is off.

## Consequences

- Grafana Cloud keeps 14 days, which is enough to investigate an incident but not for long trends.
- The SDK adds about 26 MB to the site (81 MB to 107 MB in a local measurement), so its container limit rose
  from 160 to 192 MB. Exports are batched, so a crash can lose the last few seconds of telemetry.
- Switching backends (Honeycomb, New Relic, a self-hosted stack) is a change of two environment variables.

# Observability

Know Ball sends traces, metrics and logs with [OpenTelemetry](https://opentelemetry.io/) when it is told
where to send them, and nothing at all when it is not. Production sends them to Grafana Cloud's free tier;
a laptop can use the .NET Aspire dashboard. The reasons are in [ADR 0007](adr/0007-opentelemetry-without-a-collector.md).

## What is collected

| Signal | Source | Examples |
| --- | --- | --- |
| Traces | ASP.NET Core, HttpClient, Npgsql, `KnowBallTelemetry.Source` | a guess request and its SQL; the nightly import (`knowball.import`, one `knowball.import.roster` per source) with its downloads |
| Metrics | ASP.NET Core, HttpClient, .NET runtime, Npgsql, `KnowBallTelemetry` | request duration and status, GC and heap, connection pool, the counters below |
| Logs | Every `ILogger` call, with its scopes | the same lines the console shows, each carrying its trace id |

Know Ball's own metrics ([`KnowBallTelemetry`](../src/KnowBall.Application/Telemetry/KnowBallTelemetry.cs)):

| Metric | Type | Tags |
| --- | --- | --- |
| `knowball.guesses` | counter | `knowball.game`, `knowball.correct` |
| `knowball.games.finished` | counter | `knowball.game`, `knowball.outcome` (won, lost), `knowball.practice` |
| `knowball.import.runs` | counter | `knowball.league`, `knowball.source`, `knowball.status` |
| `knowball.import.duration` | histogram, seconds | the same as `knowball.import.runs` |

Production runs each import as its own short process (`run-nightly.sh`), and each process is its own series
(its `service.instance.id`), whose import counters start and end at 1. `increase()` sees no change in such a
series, so the dashboard counts imports with `max_over_time` summed across series instead.

Nothing identifies a player: the anonymous id, guesses' player names and addresses are never tags or span
attributes. `/health` is left out of traces, since the uptime checks would otherwise be most of them.

## Sampling

`Telemetry:TraceSampleRatio` (default 0.25 in production, 1 in Development) is the share of requests traced.
The web app sends a W3C `traceparent` on each API call and logs its trace id to the console when a call fails,
so a failure seen in a browser can be found on the server. The server keeps its own ratio whatever the header
says, so a visitor cannot make every request traced. Metrics and logs are never sampled.

## Production: Grafana Cloud

The free tier holds 10,000 active metric series, 50 GB of logs and 50 GB of traces a month for 14 days, far
more than Know Ball uses. No collector runs on the VM; the SDK sends straight to Grafana's OTLP gateway.

1. In Grafana Cloud, open **Connections → Add new connection → OpenTelemetry (OTLP)** and create a token.
   It shows the endpoint and an `Authorization=Basic …` header.
2. Add both to `~/know-ball/.env` on the VM:

   ```sh
   OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-<region>.grafana.net/otlp
   OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic%20<base64 of instance id:token>
   ```

   The header's space is written `%20`, as the OpenTelemetry specification asks.
3. Run `docker compose up -d` in `~/know-ball`. The site and the importer start sending within a minute.
4. Import [dashboards/know-ball.json](dashboards/know-ball.json) under **Dashboards → New → Import**.

Removing the two lines and running `docker compose up -d` again turns it off.

## A laptop: the Aspire dashboard

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://dashboard:18890 docker compose --profile site --profile telemetry up -d --build
```

Open <http://localhost:18888>. The dashboard keeps everything in memory and needs no account. To trace
`dotnet run` instead, start only the dashboard (`docker compose --profile telemetry up -d dashboard`, after
publishing its port 18890 if you need it outside Docker) and set `OTEL_EXPORTER_OTLP_ENDPOINT` for the API.

## Cost on the VM

The OpenTelemetry SDK runs inside each process. Measured locally (two copies of the site under a 160 MB limit,
the same 600 requests each, one exporting every trace to a local collector) the site used 81 MB without it and
107 MB with it, so production's limit went from 160 to 192 MB. The importer runs only at 4 AM and traces every
run, since it makes one trace a night. With no endpoint set, none of it is loaded.

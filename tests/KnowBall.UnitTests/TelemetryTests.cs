using System.Diagnostics.Metrics;
using KnowBall.Application.Games;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Telemetry;
using KnowBall.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace KnowBall.UnitTests;

/// <summary>The meters are static, so these tests run alone: another class's guesses would land in the counts.</summary>
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public class TelemetryCollection;

[Collection(nameof(TelemetryTests))]
public class TelemetryTests : IDisposable
{
    private const string Me = "telemetry-player-1";
    private readonly GameWorld _world = new();

    [Fact]
    public void Telemetry_stays_off_without_an_OTLP_endpoint()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";

        Assert.False(builder.AddKnowBallTelemetry("know-ball"));
    }

    [Fact]
    public void Telemetry_turns_on_when_an_OTLP_endpoint_is_set()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4318";

        Assert.True(builder.AddKnowBallTelemetry("know-ball"));
        using var host = builder.Build();
    }

    [Fact]
    public async Task A_guess_is_counted_and_a_win_counts_a_finished_game()
    {
        var measured = new List<(string Name, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KnowBallTelemetry.Name) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags) copy[tag.Key] = tag.Value;
            lock (measured) measured.Add((instrument.Name, copy));
        });
        listener.Start();

        using var engine = _world.NewEngine();
        var state = (await engine.Service.GetDailyAsync(ScoutingReportGame.GameSlug, Me, default)).Result!;
        await using var ctx = _world.Db.NewContext();
        var answerId = (await ctx.Puzzles.SingleAsync(p => p.Id == state.PuzzleId)).AnswerPlayerId!.Value;
        var outcome = await engine.Service.GuessAsync(ScoutingReportGame.GameSlug, state.PuzzleId, answerId, Me, default);

        Assert.Equal(GameStatus.Won, outcome.Result!.Status);
        var guess = Assert.Single(measured, m => m.Name == "knowball.guesses");
        Assert.Equal((ScoutingReportGame.GameSlug, (object?)true), ((string)guess.Tags["knowball.game"]!, guess.Tags["knowball.correct"]));
        var finished = Assert.Single(measured, m => m.Name == "knowball.games.finished");
        Assert.Equal("won", finished.Tags["knowball.outcome"]);
        Assert.Equal(false, finished.Tags["knowball.practice"]);
    }

    public void Dispose() => _world.Dispose();
}

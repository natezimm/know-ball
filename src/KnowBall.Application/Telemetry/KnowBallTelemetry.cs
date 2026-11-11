using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KnowBall.Application.Telemetry;

/// <summary>
/// The traces and metrics Know Ball itself emits, beside what ASP.NET Core, HttpClient and Npgsql
/// already report. They are plain <see cref="ActivitySource"/> and <see cref="Meter"/> objects from
/// the base library, so this layer needs no OpenTelemetry package; the hosts decide whether anything
/// listens (see <c>TelemetrySetup</c> in Infrastructure). With nothing listening they cost almost nothing.
/// </summary>
public static class KnowBallTelemetry
{
    public const string Name = "KnowBall";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>Saved guesses, tagged with the game and whether the guess was right.</summary>
    public static readonly Counter<long> Guesses =
        Meter.CreateCounter<long>("knowball.guesses", "{guess}", "Guesses saved, by game and whether they were right.");

    /// <summary>Finished games, tagged with the game, won or lost, and whether it was practice.</summary>
    public static readonly Counter<long> GamesFinished =
        Meter.CreateCounter<long>("knowball.games.finished", "{game}", "Games that ended in a win or a loss.");

    /// <summary>Roster imports, tagged with league, source and the run's status.</summary>
    public static readonly Counter<long> ImportRuns =
        Meter.CreateCounter<long>("knowball.import.runs", "{run}", "Roster imports, by league, source and status.");

    /// <summary>How long each roster import took, including its retries.</summary>
    public static readonly Histogram<double> ImportDuration =
        Meter.CreateHistogram<double>("knowball.import.duration", "s", "How long a roster import took, retries included.");
}

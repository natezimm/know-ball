using SportsTrivia.Importer;

namespace SportsTrivia.UnitTests;

public class NightlyScheduleTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeOnly FourAm = new(4, 0);

    [Fact]
    public void Before_run_time_schedules_today()
    {
        var now = new DateTimeOffset(2026, 10, 10, 1, 30, 0, TimeSpan.FromHours(-4)); // 1:30 EDT
        var next = NightlyImportWorker.NextRun(now, FourAm, Eastern);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 4, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void After_run_time_schedules_tomorrow()
    {
        var now = new DateTimeOffset(2026, 10, 10, 4, 0, 0, TimeSpan.FromHours(-4)); // exactly 4:00 → next day
        var next = NightlyImportWorker.NextRun(now, FourAm, Eastern);
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.FromHours(-4)), next);
    }

    [Fact]
    public void Handles_fall_back_offset_change()
    {
        // DST ends Sunday Nov 1 2026 at 2:00 EDT → 4:00 that morning is EST (-5).
        var now = new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.FromHours(-4));
        var next = NightlyImportWorker.NextRun(now, FourAm, Eastern);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.FromHours(-5)), next);
    }

    [Fact]
    public void Skipped_spring_forward_time_moves_an_hour_later()
    {
        // DST starts Sunday Mar 8 2026: 2:00-3:00 does not exist.
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        var next = NightlyImportWorker.NextRun(now, new TimeOnly(2, 30), Eastern);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4)), next);
    }
}

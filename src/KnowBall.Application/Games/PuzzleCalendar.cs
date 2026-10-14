namespace KnowBall.Application.Games;

/// <summary>The daily puzzle changes at midnight Eastern, for everyone.</summary>
public sealed class PuzzleCalendar(TimeProvider clock)
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    /// <summary>The moment tomorrow's puzzle goes live.</summary>
    public DateTimeOffset NextPuzzleAt
    {
        get
        {
            var midnight = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
            // Midnight exists on both daylight-saving change days in this zone.
            return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
        }
    }
}

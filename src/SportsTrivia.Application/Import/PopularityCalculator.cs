namespace SportsTrivia.Application.Import;

/// <summary>What a career adds up to, for <see cref="PopularityCalculator.FromCareer"/>.</summary>
/// <param name="RecentPointsPerGame">Best scoring average of the last two seasons; null for players who are no longer active.</param>
public sealed record CareerSummary(
    double CareerPoints,
    int AllStarSelections,
    int MvpAwards,
    int AllLeagueSelections,
    int OtherAwards,
    bool HallOfFame,
    double? RecentPointsPerGame);

/// <summary>Rough 0-100 "how likely is a casual fan to know this player" score.</summary>
public static class PopularityCalculator
{
    /// <summary>
    /// The score for a player with a full record. A career can earn up to 95 on its own (scoring,
    /// All-Star picks, MVPs, All-League teams, the Hall of Fame); current players add up to 30 for
    /// how much they score now, so today's starters outrank forgotten veterans.
    /// </summary>
    public static double FromCareer(CareerSummary c)
    {
        var score = Math.Min(c.CareerPoints / 25_000 * 35, 35)
                    + Math.Min(c.AllStarSelections * 2.0, 20)
                    + Math.Min(c.MvpAwards * 7.5, 15)
                    + Math.Min(c.AllLeagueSelections * 1.5, 10)
                    + Math.Min(c.OtherAwards * 2.0, 5)
                    + (c.HallOfFame ? 10 : 0)
                    + Math.Min((c.RecentPointsPerGame ?? 0) * 1.1, 30);

        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }

    /// <summary>
    /// The score when all we have is one roster row: seed data, or a rookie the history has not caught up with.
    /// </summary>
    public static double Compute(SourcePlayer p, int currentSeason)
    {
        if (p.Popularity is { } fixedScore) return Math.Clamp(fixedScore, 0, 100);

        var score = 0.0;

        // Scoring is the strongest single signal of fame. 30 ppg ≈ 60 points.
        score += Math.Min(p.PointsPerGame ?? 0, 35) * 2.0;

        // Longevity: up to 15 points for a 15-year career.
        if (p.FromYear is { } from)
        {
            var years = Math.Max(0, currentSeason - from + 1);
            score += Math.Min(years, 15);
        }

        // Top draft picks get early attention.
        score += p.DraftNumber switch
        {
            1 => 10,
            <= 3 => 7,
            <= 10 => 4,
            _ => 0,
        };

        // Well-rounded stars (rebounds + assists) get a small bump.
        score += Math.Min((p.ReboundsPerGame ?? 0) + (p.AssistsPerGame ?? 0), 20) * 0.75;

        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }
}

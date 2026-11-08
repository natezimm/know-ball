namespace KnowBall.Application.Import;

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

/// <summary>What an NFL career adds up to, for <see cref="PopularityCalculator.FromFootballCareer"/>.</summary>
/// <param name="Position">The player's position code, such as QB, WR, OT or CB.</param>
/// <param name="CareerValue">Weighted career Approximate Value from the draft record; null for undrafted players.</param>
/// <param name="CareerFantasyPoints">PPR fantasy points over every season with stats, which also covers undrafted players.</param>
/// <param name="Seasons">Seasons spent on a team's roster.</param>
/// <param name="DraftPick">Overall pick; null if undrafted.</param>
/// <param name="RecentPerGame">
/// Best per-game production of the last two seasons, for current players only: PPR fantasy points, or for a
/// defender sacks, interceptions and tackles weighted to the same scale.
/// </param>
public sealed record FootballCareer(
    string? Position,
    double? CareerValue,
    double CareerFantasyPoints,
    int ProBowls,
    int AllPros,
    bool HallOfFame,
    int Seasons,
    int? DraftPick,
    double? RecentPerGame);

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

    /// <summary>
    /// The score for an NFL player. The career earns up to 85 (career value or fantasy production, Pro
    /// Bowls, All-Pro teams, the Hall of Fame, years in the league, draft slot) and current players add up
    /// to 25 for how they are playing now. Casual fans know quarterbacks and the players who score best, so
    /// the total is weighted by position: linemen and specialists only rank high as genuine stars.
    /// </summary>
    public static double FromFootballCareer(FootballCareer c)
    {
        var career = Math.Max(
            Math.Min((c.CareerValue ?? 0) / 120 * 35, 35),
            Math.Min(c.CareerFantasyPoints / 3_000 * 35, 35));

        var score = career
                    + Math.Min(c.ProBowls * 2.5, 20)
                    + Math.Min(c.AllPros * 3.0, 12)
                    + (c.HallOfFame ? 10 : 0)
                    + Math.Min(c.Seasons, 15) * 0.5
                    + c.DraftPick switch
                    {
                        null => 0,
                        1 => 5,
                        <= 10 => 3,
                        <= 32 => 1.5,
                        _ => 0,
                    }
                    + Math.Min((c.RecentPerGame ?? 0) * 1.1, 25);

        return Math.Round(Math.Clamp(score * PositionWeight(c.Position), 0, 100), 1);
    }

    /// <summary>How much a casual fan notices each position, as a multiplier on the score.</summary>
    public static double PositionWeight(string? position) => position?.ToUpperInvariant() switch
    {
        "QB" => 1.25,
        "RB" or "WR" or "TE" or "FB" => 1.1,
        "T" or "OT" or "G" or "OG" or "C" or "OL" => 0.6,
        "K" or "P" or "LS" => 0.5,
        null => 0.8,
        _ => 0.9,
    };
}

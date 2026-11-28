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

/// <summary>What an NHL career adds up to, for <see cref="PopularityCalculator.FromHockeyCareer"/>.</summary>
/// <param name="Position">C, LW, RW, D or G.</param>
/// <param name="CareerPoints">Regular-season points over the whole career.</param>
/// <param name="CareerWins">A goalie's regular-season wins; 0 for a skater.</param>
/// <param name="MajorAwards">Art Ross, Rocket Richard, Norris, Vezina, Conn Smythe, Ted Lindsay and Selke trophies.</param>
/// <param name="DraftPick">Overall pick; null if undrafted.</param>
/// <param name="RecentPerGame">Best points a game of the last two seasons (a goalie's win rate on the same scale); null for players who are no longer active.</param>
public sealed record HockeyCareer(
    string? Position,
    int CareerPoints,
    int CareerWins,
    int Seasons,
    int MvpAwards,
    int MajorAwards,
    int StanleyCups,
    bool HallOfFame,
    int? DraftPick,
    double? RecentPerGame);
/// <summary>What an MLB career adds up to, for <see cref="PopularityCalculator.FromBaseballCareer"/>.</summary>
/// <param name="OtherAwards">Rookie of the Year, Gold Gloves, Silver Sluggers and World Series MVPs.</param>
/// <param name="Seasons">Seasons with a game in the majors.</param>
/// <param name="RecentValue">The better of the last two seasons by <see cref="PopularityCalculator.BaseballSeasonValue"/>, for current players only.</param>
public sealed record BaseballCareer(
    double Hits,
    double HomeRuns,
    double Wins,
    double Strikeouts,
    double Saves,
    int AllStars,
    int Mvps,
    int CyYoungs,
    int OtherAwards,
    bool HallOfFame,
    int Seasons,
    double? RecentValue);

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

    /// <summary>
    /// The score for an NHL player. The career earns up to 95 (points, or a goalie's wins, Hart trophies, the other
    /// big trophies, Stanley Cups, the Hall of Fame, years in the league, draft slot) and current players add up to
    /// 25 for how they are scoring now. Defensemen score fewer points than forwards for the same fame, so their
    /// points count for more.
    /// </summary>
    public static double FromHockeyCareer(HockeyCareer c)
    {
        var career = c.Position == "G"
            ? Math.Min(c.CareerWins / 400.0 * 35, 35)
            : Math.Min(c.CareerPoints * (c.Position == "D" ? 1.6 : 1.0) / 1_200.0 * 35, 35);

        var score = career
                    + Math.Min(c.MvpAwards * 7.5, 15)
                    + Math.Min(c.MajorAwards * 3.0, 15)
                    + Math.Min(c.StanleyCups * 1.5, 6)
                    + (c.HallOfFame ? 10 : 0)
                    + Math.Min(c.Seasons, 15) * 0.5
                    + c.DraftPick switch
                    {
                        null => 0,
                        1 => 6.5,
                        <= 5 => 4,
                        <= 15 => 2,
                        _ => 0,
                    }
                    + Math.Min((c.RecentPerGame ?? 0) * 20, 25);

        return Math.Round(Math.Clamp(score, 0, 100), 1);
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

    /// <summary>
    /// The score for an MLB player. The career earns up to about 90: the better of what he did at the plate (3,000 hits
    /// or 500 home runs is the top) and on the mound (300 wins, 3,000 strikeouts or 400 saves), All-Star picks, MVPs and
    /// Cy Youngs, the other awards, the Hall of Fame and years in the majors. Current players add up to 25 for how they
    /// are playing now, so today's stars outrank forgotten veterans.
    /// </summary>
    public static double FromBaseballCareer(BaseballCareer c)
    {
        var batting = Math.Max(Math.Min(c.Hits / 3_000 * 30, 30), Math.Min(c.HomeRuns / 500 * 30, 30));
        var pitching = Math.Max(Math.Max(Math.Min(c.Wins / 300 * 30, 30), Math.Min(c.Strikeouts / 3_000 * 30, 30)), Math.Min(c.Saves / 400 * 30, 30));

        var score = Math.Max(batting, pitching)
                    + Math.Min(c.AllStars * 2.0, 20)
                    + Math.Min((c.Mvps + c.CyYoungs) * 7.5, 15)
                    + Math.Min(c.OtherAwards * 1.0, 8)
                    + (c.HallOfFame ? 10 : 0)
                    + Math.Min(c.Seasons, 15) * 0.5
                    + Math.Min(c.RecentValue ?? 0, 25);

        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }

    /// <summary>
    /// One season on a 0-25 scale: a 40-homer, 170-hit season, a 250-strikeout one or a 40-save one is near the top.
    /// The better of hitting and pitching counts, so a two-way player is not added twice.
    /// </summary>
    public static double BaseballSeasonValue(double hits, double homeRuns, double steals, double wins, double strikeouts, double saves)
    {
        var batting = homeRuns * 0.35 + hits * 0.06 + steals * 0.08;
        var pitching = Math.Max(strikeouts * 0.09 + wins * 0.5, saves * 0.6);
        return Math.Round(Math.Min(Math.Max(batting, pitching), 25), 2);
    }
}

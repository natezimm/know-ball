using SportsTrivia.Application.Import;

namespace SportsTrivia.UnitTests;

public class PopularityCalculatorTests
{
    private static SourcePlayer P(double? ppg = null, int? from = null, int? pick = null, double? reb = null, double? ast = null, double? fixedScore = null) =>
        new("1", "A", "B", null, PointsPerGame: ppg, FromYear: from, DraftNumber: pick, ReboundsPerGame: reb, AssistsPerGame: ast, Popularity: fixedScore);

    [Fact]
    public void Star_outranks_role_player()
    {
        var star = PopularityCalculator.Compute(P(ppg: 30, from: 2015, pick: 3, reb: 8, ast: 6), 2025);
        var role = PopularityCalculator.Compute(P(ppg: 6, from: 2022, pick: 40, reb: 3, ast: 1), 2025);
        Assert.True(star > role + 30, $"star {star} vs role {role}");
    }

    [Fact]
    public void Fixed_score_wins_and_is_clamped()
    {
        Assert.Equal(88, PopularityCalculator.Compute(P(ppg: 2, fixedScore: 88), 2025));
        Assert.Equal(100, PopularityCalculator.Compute(P(fixedScore: 140), 2025));
    }

    [Fact]
    public void Missing_data_scores_zero_not_crash() =>
        Assert.Equal(0, PopularityCalculator.Compute(P(), 2025));

    [Fact]
    public void Never_exceeds_100() =>
        Assert.InRange(PopularityCalculator.Compute(P(ppg: 50, from: 1990, pick: 1, reb: 20, ast: 20), 2025), 0, 100);

    private static CareerSummary Career(double points = 0, int allStars = 0, int mvps = 0, int allLeague = 0, int other = 0, bool hof = false, double? recent = null) =>
        new(points, allStars, mvps, allLeague, other, hof, recent);

    [Fact]
    public void An_all_time_great_scores_near_the_top_long_after_retiring() =>
        Assert.InRange(PopularityCalculator.FromCareer(Career(points: 32_000, allStars: 14, mvps: 5, allLeague: 11, other: 2, hof: true)), 90, 100);

    [Fact]
    public void A_current_starter_outranks_a_retired_role_player()
    {
        var starter = PopularityCalculator.FromCareer(Career(points: 6_000, recent: 18));
        var veteran = PopularityCalculator.FromCareer(Career(points: 9_000));
        Assert.True(starter > veteran, $"starter {starter} vs veteran {veteran}");
    }

    [Fact]
    public void Each_part_of_a_career_is_capped()
    {
        Assert.Equal(35, PopularityCalculator.FromCareer(Career(points: 90_000)));
        Assert.Equal(20, PopularityCalculator.FromCareer(Career(allStars: 30)));
        Assert.Equal(15, PopularityCalculator.FromCareer(Career(mvps: 9)));
        Assert.Equal(30, PopularityCalculator.FromCareer(Career(recent: 40)));
        Assert.Equal(100, PopularityCalculator.FromCareer(Career(90_000, 30, 9, 30, 9, true, 40)));
        Assert.Equal(0, PopularityCalculator.FromCareer(Career()));
    }
}

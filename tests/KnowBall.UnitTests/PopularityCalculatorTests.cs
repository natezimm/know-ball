using KnowBall.Application.Import;

namespace KnowBall.UnitTests;

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

    private static FootballCareer Football(string position = "QB", double? value = null, double fantasy = 0, int proBowls = 0, int allPros = 0,
        bool hof = false, int seasons = 0, int? pick = null, double? recent = null) =>
        new(position, value, fantasy, proBowls, allPros, hof, seasons, pick, recent);

    [Fact]
    public void A_star_quarterback_scores_near_the_top() =>
        Assert.InRange(PopularityCalculator.FromFootballCareer(Football(value: 150, proBowls: 10, allPros: 3, seasons: 15, pick: 1)), 90, 100);

    [Fact]
    public void The_same_career_ranks_a_quarterback_over_a_receiver_over_a_defender_over_a_lineman_over_a_kicker()
    {
        double Score(string position) => PopularityCalculator.FromFootballCareer(Football(position, value: 80, proBowls: 4, seasons: 10));
        Assert.True(Score("QB") > Score("WR"));
        Assert.True(Score("WR") > Score("CB"));
        Assert.True(Score("CB") > Score("OT"));
        Assert.True(Score("OT") > Score("K"));
    }

    [Fact]
    public void Fantasy_production_stands_in_for_an_undrafted_players_missing_draft_record()
    {
        var undrafted = PopularityCalculator.FromFootballCareer(Football("WR", fantasy: 2_000, seasons: 10));
        var drafted = PopularityCalculator.FromFootballCareer(Football("WR", value: 80, seasons: 10));
        Assert.Equal(drafted, undrafted);
    }

    [Fact]
    public void A_current_player_in_form_outranks_a_forgotten_veteran()
    {
        var current = PopularityCalculator.FromFootballCareer(Football("RB", value: 30, seasons: 4, pick: 20, recent: 18));
        var veteran = PopularityCalculator.FromFootballCareer(Football("RB", value: 50, seasons: 9));
        Assert.True(current > veteran, $"current {current} vs veteran {veteran}");
    }

    [Fact]
    public void A_football_score_stays_between_0_and_100()
    {
        Assert.Equal(100, PopularityCalculator.FromFootballCareer(Football(value: 400, fantasy: 9_000, proBowls: 20, allPros: 10, hof: true, seasons: 25, pick: 1, recent: 40)));
        Assert.Equal(0, PopularityCalculator.FromFootballCareer(Football()));
    }
}

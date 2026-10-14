using KnowBall.Application.Import;

namespace KnowBall.UnitTests;

public class StintBuilderTests
{
    private static IReadOnlyList<StintSpan> Build(params (int Season, string Team)[] rows) => StintBuilder.Build(rows);

    [Fact]
    public void Seasons_in_a_row_with_one_team_are_one_spell() =>
        Assert.Equal([new StintSpan("CHI", 2011, 2013, 3)], Build((2011, "CHI"), (2012, "CHI"), (2013, "CHI")));

    [Fact]
    public void A_trade_in_mid_season_ends_one_spell_and_starts_the_next_in_the_same_year() =>
        Assert.Equal(
            [new StintSpan("MIN", 2017, 2018, 2), new StintSpan("PHI", 2018, 2018, 1), new StintSpan("MIA", 2019, 2020, 2)],
            Build((2017, "MIN"), (2018, "MIN"), (2018, "PHI"), (2019, "MIA"), (2020, "MIA")));

    [Fact]
    public void Going_back_to_an_old_team_is_a_new_spell() =>
        Assert.Equal(
            [new StintSpan("CLE", 2003, 2009, 2), new StintSpan("MIA", 2010, 2010, 1), new StintSpan("CLE", 2014, 2014, 1)],
            Build((2003, "CLE"), (2009, "CLE"), (2010, "MIA"), (2014, "CLE")));

    [Fact]
    public void A_missed_year_does_not_split_a_spell_and_is_not_counted_as_a_season() =>
        Assert.Equal([new StintSpan("GSW", 2018, 2021, 2)], Build((2018, "GSW"), (2021, "GSW")));

    [Fact]
    public void No_rows_means_no_spells() => Assert.Empty(Build());
}

using KnowBall.Application.Import;
using KnowBall.Infrastructure.Sources;
using Spell = KnowBall.Infrastructure.Sources.NflverseHistorySource.RosterSpell;

namespace KnowBall.UnitTests;

/// <summary>Placing the All-Pro and Pro Bowl names from Wikipedia on players of the nflverse record.</summary>
public class NflHonorsTests
{
    private static readonly NflTeams Teams = NflTeams.Instance.Value;

    private static (Dictionary<string, List<HistoryAward>> Awards, List<NflHonors.Honor> Unplaced) Place(
        IEnumerable<NflHonors.Honor> honors, params (string Id, string Name, Spell[] Spells)[] players) =>
        new NflHonors(honors.ToList()).Place(
            players.ToDictionary(p => p.Id, p => p.Name),
            players.ToDictionary(p => p.Id, p => p.Spells.ToList()),
            Teams);

    private static Spell On(int season, string team) => new(season, 1, team);

    [Fact]
    public void A_name_is_matched_without_its_punctuation_spaces_or_suffix()
    {
        var (awards, unplaced) = Place(
            [new(2014, "AllPro1", "J. J. Watt", "HOU"), new(2015, "ProBowl", "Chris Harris, Jr.", "DEN")],
            ("watt", "J.J. Watt", [On(2014, "HOU")]),
            ("harris", "Chris Harris", [On(2015, "DEN")]));

        Assert.Empty(unplaced);
        Assert.Equal([new HistoryAward(2014, "AllPro1")], awards["watt"]);
        Assert.Equal([new HistoryAward(2015, "ProBowl")], awards["harris"]);
    }

    [Fact]
    public void Players_who_share_a_name_are_told_apart_by_the_franchise_including_its_old_name()
    {
        var (awards, _) = Place(
            [new(2005, "ProBowl", "Steve Smith", "CAR"), new(2005, "ProBowl", "Charles Woodson", "LV")],
            ("panther", "Steve Smith", [On(2005, "CAR")]),
            ("giant", "Steve Smith", [On(2005, "NYG")]),
            ("woodson", "Charles Woodson", [On(2005, "OAK")]));

        Assert.True(awards.ContainsKey("panther"));
        Assert.False(awards.ContainsKey("giant"));
        Assert.True(awards.ContainsKey("woodson"));
    }

    [Fact]
    public void A_player_known_by_another_first_name_is_found_by_last_name_on_that_team()
    {
        var (awards, unplaced) = Place(
            [new(2018, "AllPro1", "Darius Leonard", "IND"), new(2019, "ProBowl", "Josh Allen", "JAX")],
            ("leonard", "Shaquille Leonard", [On(2018, "IND")]),
            ("qb", "Josh Allen", [On(2019, "BUF")]),
            ("edge", "Josh Hines-Allen", [On(2019, "JAX")]),
            ("other", "Brandon Allen", [On(2019, "JAX")]));

        Assert.Empty(unplaced);
        Assert.True(awards.ContainsKey("leonard"));
        Assert.True(awards.ContainsKey("edge"));
        Assert.False(awards.ContainsKey("qb"));
    }

    [Fact]
    public void A_name_no_one_player_answers_to_is_left_out()
    {
        var (awards, unplaced) = Place(
            [new(2002, "ProBowl", "Michael Lewis", "NO"), new(2010, "ProBowl", "Steve Smith", "MIA")],
            ("safety", "Michael Lewis", [On(2002, "PHI")]),
            ("panther", "Steve Smith", [On(2010, "CAR")]),
            ("giant", "Steve Smith", [On(2010, "NYG")]));

        Assert.Empty(awards);
        Assert.Equal(2, unplaced.Count);
    }

    [Fact]
    public void A_first_team_All_Pro_at_one_position_is_not_also_second_team()
    {
        var (awards, _) = Place(
            [new(2019, "AllPro1", "T. J. Watt", "PIT"), new(2019, "AllPro2", "T. J. Watt", "PIT")],
            ("watt", "T.J. Watt", [On(2019, "PIT")]),
            ("brother", "Derek Watt", [On(2019, "PIT")]));

        Assert.Equal([new HistoryAward(2019, "AllPro1")], awards["watt"]);
        Assert.False(awards.ContainsKey("brother"));
    }

    [Fact]
    public void The_bundled_file_covers_every_season_since_1999()
    {
        var honors = NflHonors.Instance.Value.Honors;
        var seasons = honors.Select(h => h.Season).Distinct().Order().ToList();

        Assert.Equal(1999, seasons[0]);
        Assert.Equal(Enumerable.Range(1999, seasons.Count), seasons);
        Assert.All(seasons, s => Assert.InRange(honors.Count(h => h.Season == s && h.Type == "AllPro1"), 24, 40));
        Assert.All(seasons, s => Assert.InRange(honors.Count(h => h.Season == s && h.Type == "ProBowl"), 80, 140));
        Assert.All(honors, h => Assert.NotNull(Teams.Franchise(h.Franchise)));
    }

    [Fact]
    public async Task An_ESPN_award_lists_every_winner_of_a_tied_vote()
    {
        using var stream = new MemoryStream("""
            {"winners":[{"athlete":{"$ref":"http://x/seasons/2003/athletes/1428?lang=en"}},{"athlete":{"$ref":"http://x/seasons/2003/athletes/2306?lang=en"}},{"team":{}}]}
            """u8.ToArray());
        Assert.Equal(["1428", "2306"], await NflverseHistorySource.EspnWinnersAsync(stream, CancellationToken.None));
    }
}

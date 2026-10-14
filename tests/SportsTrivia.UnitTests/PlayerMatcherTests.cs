using SportsTrivia.Application.Import;
using SportsTrivia.Application.Text;
using SportsTrivia.Domain;

namespace SportsTrivia.UnitTests;

public class PlayerMatcherTests
{
    private static int _nextId = 1;

    private static Player P(string name, string? born = null, string primaryId = "", params (string Source, string Value)[] ids) => new()
    {
        Id = _nextId++,
        ExternalId = primaryId.Length > 0 ? primaryId : $"p{_nextId}",
        FullName = name,
        NormalizedName = NameNormalizer.Normalize(name),
        BirthDate = born is null ? null : DateOnly.Parse(born),
        ExternalIds = ids.Select(i => new PlayerExternalId { Source = i.Source, Value = i.Value }).ToList(),
    };

    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void A_known_id_wins_even_if_the_name_has_changed()
    {
        var player = P("Ron Artest", "1979-11-13", ids: ("espn", "25"));
        var matcher = new PlayerMatcher("espn", [player]);

        Assert.Same(player, matcher.Find("25", "Metta World Peace", D("1979-11-13")));
    }

    [Fact]
    public void A_new_source_matches_on_name_and_birth_date_ignoring_accents()
    {
        var luka = P("Luka Dončić", "1999-02-28", ids: ("bref", "doncilu01"));
        var matcher = new PlayerMatcher("espn", [luka, P("Luka Garza", "1998-12-27")]);

        Assert.Same(luka, matcher.Find("3945274", "Luka Doncic", D("1999-02-28")));
    }

    [Theory]
    [InlineData("Jaren Jackson Jr.", "Jaren Jackson")]   // suffix on one side only
    [InlineData("Nicolas Claxton", "Nic Claxton")]       // short first name
    [InlineData("P.J. Washington", "PJ Washington")]
    public void Same_birthday_tolerates_small_differences_in_the_name(string stored, string incoming)
    {
        var player = P(stored, "1999-09-15");
        var matcher = new PlayerMatcher("espn", [player]);

        Assert.Same(player, matcher.Find("1", incoming, D("1999-09-15")));
    }

    [Fact]
    public void The_same_name_with_a_different_birth_date_is_someone_else()
    {
        var father = P("Gary Payton", "1968-07-23");
        var matcher = new PlayerMatcher("espn", [father]);

        Assert.Null(matcher.Find("9", "Gary Payton", D("1992-12-01")));
    }

    [Fact]
    public void A_player_already_tied_to_another_id_in_this_source_is_not_matched_again()
    {
        var taken = P("Jalen Williams", "2001-04-14", ids: ("espn", "100"));
        var matcher = new PlayerMatcher("espn", [taken]);

        Assert.Null(matcher.Find("200", "Jalen Williams", D("2001-04-14")));
    }

    [Fact]
    public void Name_alone_matches_only_when_a_birth_date_is_missing_and_the_name_is_unique()
    {
        var seedRow = P("Cooper Flagg");
        var one = P("Bill Bradley", "1943-07-28");
        var two = P("Bill Bradley");
        var matcher = new PlayerMatcher("bref", [seedRow, one, two]);

        Assert.Same(seedRow, matcher.Find("flaggco01", "Cooper Flagg", D("2006-12-21")));
        Assert.Null(matcher.Find("bradlbi02", "Bill Bradley", null));
    }

    [Fact]
    public void Rows_from_before_ids_were_namespaced_match_on_their_only_id()
    {
        var legacy = P("LeBron James", primaryId: "seed-lebron-james");
        var matcher = new PlayerMatcher("seed", [legacy]);

        Assert.Same(legacy, matcher.Find("seed-lebron-james", "Someone Else", null));
    }

    [Fact]
    public void Binding_records_the_id_once_and_makes_it_findable()
    {
        var player = P("Victor Wembanyama", "2004-01-04");
        var matcher = new PlayerMatcher("espn", [player]);

        matcher.Bind(player, "5104157");
        matcher.Bind(player, "5104157");

        var id = Assert.Single(player.ExternalIds);
        Assert.Equal(("espn", "5104157"), (id.Source, id.Value));
        Assert.Same(player, matcher.Find("5104157", "", null));
    }

    [Fact]
    public void A_primary_id_already_used_by_another_source_gets_a_prefix()
    {
        var matcher = new PlayerMatcher("espn", [P("Someone", primaryId: "2544")]);

        Assert.Equal("espn:2544", matcher.PrimaryIdFor("2544"));
        Assert.Equal("1966", matcher.PrimaryIdFor("1966"));
    }
}

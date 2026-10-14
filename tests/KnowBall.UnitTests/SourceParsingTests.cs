using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KnowBall.Application.Import;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

public class CsvReaderTests
{
    [Fact]
    public void Reads_quoted_fields_with_commas_quotes_and_blank_cells()
    {
        var rows = CsvReader.Parse("player,colleges,wt\r\n\"O'Neal, Shaq\",\"LSU, \"\"Tigers\"\"\",325\nNoCollege,NA,\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("O'Neal, Shaq", rows[0].Str("player"));
        Assert.Equal("LSU, \"Tigers\"", rows[0].Str("colleges"));
        Assert.Equal(325, rows[0].Int("wt"));
        Assert.Null(rows[1].Str("colleges")); // "NA" is the file's blank
        Assert.Null(rows[1].Int("wt"));
        Assert.Null(rows[1].Str("no_such_column"));
    }
}

/// <summary>Runs the Basketball-Reference source over the trimmed copies of its real files in tests/fixtures/bref.</summary>
public class BrefCsvSourceTests
{
    public static readonly string FixtureDirectory = FindFixtures();

    private static readonly Lazy<HistorySnapshot> Snapshot = new(() =>
        new BrefCsvSource(new HttpClient(), Options.Create(new BrefOptions { Directory = FixtureDirectory }), NullLogger<BrefCsvSource>.Instance)
            .FetchAsync(CancellationToken.None).GetAwaiter().GetResult());

    private static HistoryPlayer Player(string id) => Snapshot.Value.Players.Single(p => p.ExternalId == id);

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "bref");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/fixtures/bref");
    }

    [Fact]
    public void Seasons_are_named_by_the_year_they_start()
    {
        Assert.Equal(2025, Snapshot.Value.LatestSeason); // the files call it 2026
        var lebron = Player("jamesle01");
        Assert.Equal((2003, 2025), (lebron.FromYear, lebron.ToYear));
        Assert.Equal(2003, lebron.Seasons[0].Season);
    }

    [Fact]
    public void Career_facts_are_read()
    {
        var lebron = Player("jamesle01");
        Assert.Equal(("LeBron", "James"), (lebron.FirstName, lebron.LastName));
        Assert.Equal(("F-G", 81, new DateOnly(1984, 12, 30)), (lebron.Position, lebron.HeightInches, lebron.BirthDate));
        Assert.Null(lebron.College);
        Assert.Equal((2003, 1), (lebron.DraftYear, lebron.DraftNumber));
        Assert.False(lebron.HallOfFame);
        Assert.True(Player("jordami01").HallOfFame);
        Assert.Equal("UNC", Player("jordami01").College);
    }

    [Fact]
    public void A_split_season_has_a_combined_line_and_one_line_per_team_in_order()
    {
        var season = Player("butleji01").Seasons.Where(s => s.Season == 2024).ToList();

        Assert.Equal([null, "MIA", "GSW"], season.Select(s => s.TeamKey));
        Assert.Equal([true, false, false], season.Select(s => s.IsSeasonTotal));
        Assert.Equal(55, season[0].Games);
    }

    [Fact]
    public void A_one_team_season_is_its_own_total()
    {
        var season = Assert.Single(Player("curryst01").Seasons, s => s.Season == 2015);
        Assert.True(season.IsSeasonTotal);
        Assert.Equal("GSW", season.TeamKey);
        Assert.Equal(30.1, season.PointsPerGame);
        Assert.Contains("\"tp\":0.454", season.StatsJson);
    }

    [Fact]
    public void Awards_are_mapped_to_shared_type_names()
    {
        var jokic = Player("jokicni01").Awards;
        Assert.Contains(new HistoryAward(2020, AwardTypes.Mvp), jokic);
        Assert.Contains(new HistoryAward(2020, AwardTypes.AllLeague + "1"), jokic);
        Assert.Contains(new HistoryAward(2020, AwardTypes.AllStar), jokic);
        Assert.DoesNotContain(jokic, a => a.Type == AwardTypes.Mvp && a.Season == 2022); // voted on, did not win
        Assert.Contains(new HistoryAward(2023, AwardTypes.Rookie), Player("wembavi01").Awards);
    }

    [Fact]
    public void Every_team_code_is_filed_under_a_known_team()
    {
        var teams = Snapshot.Value.Teams;
        Assert.Equal(99, teams.Count);
        Assert.DoesNotContain(teams, t => t.Team.City.Length == 0 && t.Team.Name.Contains(' ') && t.Key is not ("FLO" or "SSL"));
        Assert.Equal(30, teams.Count(t => t.ActiveTo is null));
    }

    [Fact]
    public void Current_teams_use_the_leagues_ids_and_old_identities_point_at_what_they_became()
    {
        var teams = Snapshot.Value.Teams.ToDictionary(t => t.Key);

        // Basketball-Reference says BRK, CHO and PHO; the NBA says BKN, CHA and PHX.
        Assert.Equal(("1610612751", "BKN"), (teams["BRK"].Team.ExternalId, teams["BRK"].Team.Abbreviation));
        Assert.Equal("CHA", teams["CHO"].Team.Abbreviation);
        Assert.Null(teams["BRK"].FranchiseKey);

        Assert.Equal(("bref-SEA", "Seattle", "SuperSonics", "OKC"),
            (teams["SEA"].Team.ExternalId, teams["SEA"].Team.City, teams["SEA"].Team.Name, teams["SEA"].FranchiseKey));
        Assert.Equal((1967, 2007), (teams["SEA"].ActiveFrom, teams["SEA"].ActiveTo));

        // The Bobcats share the code CHA with today's Hornets but are their own, former, identity.
        Assert.Equal(("bref-CHA", "Bobcats", "CHO"), (teams["CHA"].Team.ExternalId, teams["CHA"].Team.Name, teams["CHA"].FranchiseKey));
    }

    [Fact]
    public void The_same_team_under_a_second_code_is_one_team()
    {
        var teams = Snapshot.Value.Teams.ToDictionary(t => t.Key);

        Assert.False(teams.ContainsKey("INA")); // the ABA Pacers
        Assert.Equal(1967, teams["IND"].ActiveFrom);
        Assert.False(teams.ContainsKey("CHH"));
        Assert.Contains(Player("malonmo01").Seasons, s => s.TeamKey == "UTS"); // Utah Stars, ABA
    }

    [Fact]
    public void Names_split_on_the_first_space()
    {
        Assert.Equal(("Shai", "Gilgeous-Alexander"), BrefCsvSource.SplitName("Shai Gilgeous-Alexander"));
        Assert.Equal(("Michael", "Porter Jr."), BrefCsvSource.SplitName(" Michael Porter Jr. "));
        Assert.Equal(("Nenê", ""), BrefCsvSource.SplitName("Nenê"));
    }
}

public class EspnRosterSourceTests
{
    private const string TeamsJson = """
        { "sports": [ { "leagues": [ { "teams": [
          { "team": { "id": "9", "abbreviation": "GS" } },
          { "team": { "id": "13", "abbreviation": "LAL" } },
          { "team": { "abbreviation": "NO-ID" } } ] } ] } ] }
        """;

    private const string RosterJson = """
        { "season": { "year": 2027, "type": 1 },
          "athletes": [
            { "id": "3945274", "firstName": "Luka", "lastName": "Doncic", "fullName": "Luka Doncic", "jersey": "77",
              "height": 80.0, "weight": 230.0, "dateOfBirth": "1999-02-28T08:00Z",
              "birthPlace": { "city": "Ljubljana", "country": "Slovenia" }, "college": null,
              "position": { "abbreviation": "G" }, "headshot": { "href": "https://a.espncdn.com/luka.png" },
              "experience": { "years": 8 } },
            { "id": 5211, "fullName": "Rafael Castro", "position": {}, "height": 0 },
            { "fullName": "No Id" } ] }
        """;

    [Fact]
    public void Team_list_gives_ids_and_abbreviations()
    {
        using var doc = JsonDocument.Parse(TeamsJson);
        Assert.Equal([("9", "GS"), ("13", "LAL")], EspnRosterSource.ParseTeams(doc.RootElement));
    }

    [Fact]
    public void Roster_rows_become_players_on_the_given_team()
    {
        using var doc = JsonDocument.Parse(RosterJson);
        var roster = EspnRosterSource.ParseRoster(doc.RootElement, "1610612747");

        Assert.Equal((2026, false), (roster.SeasonStart, roster.InSeason)); // 2027 is the 2026-27 season; type 1 is preseason
        Assert.Equal(2, roster.Players.Count);

        var luka = roster.Players[0];
        Assert.Equal(("3945274", "Luka", "Doncic", "1610612747"), (luka.ExternalId, luka.FirstName, luka.LastName, luka.TeamExternalId));
        Assert.Equal(("G", 80, 230, "77"), (luka.Position, luka.HeightInches, luka.WeightLbs, luka.JerseyNumber));
        Assert.Equal((new DateOnly(1999, 2, 28), "Ljubljana", "Slovenia"), (luka.BirthDate, luka.BirthPlace, luka.Country));
        Assert.Equal(("https://a.espncdn.com/luka.png", 2018), (luka.HeadshotUrl, luka.FromYear));
        Assert.Null(luka.College);
    }

    [Fact]
    public void Missing_fields_are_left_empty_not_guessed()
    {
        using var doc = JsonDocument.Parse(RosterJson);
        var castro = EspnRosterSource.ParseRoster(doc.RootElement, "T").Players[1];

        Assert.Equal(("5211", "Rafael", "Castro"), (castro.ExternalId, castro.FirstName, castro.LastName));
        Assert.Null(castro.Position);
        Assert.Null(castro.HeightInches);
        Assert.Null(castro.JerseyNumber);
        Assert.Null(castro.BirthDate);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Regular_season_and_playoffs_count_as_in_season(int type, bool expected)
    {
        using var doc = JsonDocument.Parse($$"""{ "season": { "year": 2027, "type": {{type}} }, "athletes": [] }""");
        Assert.Equal(expected, EspnRosterSource.ParseRoster(doc.RootElement, "T").InSeason);
    }
}

public class WikidataCrosswalkSourceTests
{
    private const string IdsJson = """
        { "results": { "bindings": [
          { "bref": { "value": "j/jamesle01" }, "nba": { "value": "2544" }, "espn": { "value": "1966" },
            "place": { "value": "http://www.wikidata.org/entity/Q163132" }, "coord": { "value": "Point(-81.517777777 41.073055555)" } },
          { "bref": { "value": "j/jamesle01" }, "nba": { "value": "9999" } },
          { "bref": { "value": "oldtimer01" } } ] } }
        """;

    private const string PlacesJson = """
        { "results": { "bindings": [
          { "place": { "value": "http://www.wikidata.org/entity/Q163132" }, "placeLabel": { "value": "Akron" }, "countryLabel": { "value": "United States" } },
          { "place": { "value": "http://www.wikidata.org/entity/Q163132" }, "placeLabel": { "value": "Akron" }, "countryLabel": { "value": "Ohio" } } ] } }
        """;

    [Fact]
    public void Entries_join_ids_coordinates_and_place_names()
    {
        using var places = JsonDocument.Parse(PlacesJson);
        using var ids = JsonDocument.Parse(IdsJson);
        var entries = WikidataCrosswalkSource.ParseEntries(ids.RootElement, WikidataCrosswalkSource.ParsePlaces(places.RootElement));

        Assert.Equal(2, entries.Count); // the second row for the same player is ignored
        var lebron = entries[0];
        Assert.Equal(("bref", "jamesle01"), (lebron.KeySource, lebron.KeyValue));
        Assert.Equal("2544", lebron.Ids["nba"]);
        Assert.Equal("1966", lebron.Ids["espn"]);
        Assert.Equal(("Akron", "USA", 41.07306, -81.51778), (lebron.BirthPlace, lebron.Country, lebron.Latitude, lebron.Longitude));

        Assert.Equal("oldtimer01", entries[1].KeyValue);
        Assert.Empty(entries[1].Ids);
        Assert.Null(entries[1].Latitude);
    }

    [Fact]
    public void A_response_without_results_is_an_error_not_an_empty_list()
    {
        using var broken = JsonDocument.Parse("""{ "error": "timeout" }""");
        Assert.Throws<InvalidOperationException>(() => WikidataCrosswalkSource.ParseEntries(broken.RootElement, []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a point")]
    [InlineData("Point(1)")]
    public void Unreadable_coordinates_are_dropped(string? wkt) =>
        Assert.Equal((null, null), WikidataCrosswalkSource.ParsePoint(wkt));
}

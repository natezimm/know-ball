using System.Text.Json;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

public class NbaStatsParsingTests
{
    [Theory]
    [InlineData("6-9", 81)]
    [InlineData("7-4", 88)]
    [InlineData("6-0", 72)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("tall", null)]
    public void Parses_height(string? raw, int? inches) =>
        Assert.Equal(inches, NbaStatsPlayerSource.ParseHeight(raw));

    [Fact]
    public void Reads_rows_by_column_name_and_tolerates_types()
    {
        const string json = """
        {
          "resultSets": [{
            "name": "PlayerIndex",
            "headers": ["PERSON_ID","PLAYER_LAST_NAME","PLAYER_FIRST_NAME","TEAM_ID","HEIGHT","WEIGHT","DRAFT_YEAR","DRAFT_NUMBER","PTS","COLLEGE"],
            "rowSet": [
              [203999, "Jokić", "Nikola", 1610612743, "6-11", "284", 2014, 41, 29.6, " "],
              [1641705, "Wembanyama", "Victor", 1610612759, "7-4", 235, 2023, 1, "24.3", "None"],
              [1, "Agent", "Free", 0, null, null, "Undrafted", null, null, null]
            ]
          }]
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var rows = NbaStatsPlayerSource.ParseResultSet(doc.RootElement, "PlayerIndex");

        Assert.Equal(3, rows.Count);
        Assert.Equal("203999", rows[0].Str("PERSON_ID"));
        Assert.Equal("Jokić", rows[0].Str("PLAYER_LAST_NAME"));
        Assert.Equal(284, rows[0].Int("WEIGHT"));
        Assert.Equal(29.6, rows[0].Dbl("PTS"));
        Assert.Null(rows[0].Str("COLLEGE"));            // blank → null
        Assert.Equal(24.3, rows[1].Dbl("PTS"));         // numeric string
        Assert.Null(rows[2].Int("DRAFT_YEAR"));         // "Undrafted"
        Assert.Null(rows[2].Str("MISSING_COLUMN"));     // unknown column is fine
    }

    [Fact]
    public void Missing_resultSets_is_a_clear_error()
    {
        using var doc = JsonDocument.Parse("""{ "message": "blocked" }""");
        var ex = Assert.Throws<InvalidOperationException>(() => NbaStatsPlayerSource.ParseResultSet(doc.RootElement, "PlayerIndex"));
        Assert.Contains("resultSets", ex.Message);
    }

    [Theory]
    [InlineData(2026, 10, 10, 2026)]
    [InlineData(2026, 9, 30, 2025)]
    [InlineData(2026, 1, 15, 2025)]
    public void Season_rolls_over_in_october(int y, int m, int d, int expected) =>
        Assert.Equal(expected, SeasonCalendar.CurrentSeasonStartYear(new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Season_label() => Assert.Equal("2025-26", SeasonCalendar.Label(2025));

    [Fact]
    public void Team_reference_has_all_30_teams_with_coordinates()
    {
        Assert.Equal(30, TeamReference.Nba.Count);
        Assert.All(TeamReference.Nba.Values, t =>
        {
            Assert.InRange(t.Latitude, 20, 50);
            Assert.InRange(t.Longitude, -125, -70);
            Assert.Contains(t.Conference, new[] { "East", "West" });
        });
        Assert.Equal(15, TeamReference.Nba.Values.Count(t => t.Conference == "East"));
    }
}

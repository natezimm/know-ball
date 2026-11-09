using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Import;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

/// <summary>Runs the nflverse sources over the small made-up files in tests/fixtures/nflverse, laid out like the real releases.</summary>
public class NflverseSourceTests
{
    public static readonly string FixtureDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BrefCsvSourceTests.FixtureDirectory)!, "nflverse");

    private static NflverseOptions Options() => new()
    {
        Directory = FixtureDirectory,
        FirstSeason = 2023,
        WeeklyFromSeason = 2024,
        MinSeasons = 2,
    };

    private static async Task<HistorySnapshot> History(DateTimeOffset now) =>
        await new NflverseHistorySource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<NflverseHistorySource>.Instance).FetchAsync(CancellationToken.None);

    private static async Task<SourceSnapshot> Roster(DateTimeOffset now) =>
        await new NflverseRosterSource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<NflverseRosterSource>.Instance).FetchAsync(CancellationToken.None);

    private static readonly DateTimeOffset MidSeason = new(2025, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Path(HistoryPlayer p) =>
        string.Join(", ", p.Seasons.Select(s => $"{s.Season} {s.TeamKey}{(s.IsSeasonTotal ? "" : "*")}"));

    [Fact]
    public async Task History_reads_every_season_and_keeps_a_traded_players_teams_in_order()
    {
        var snapshot = await History(MidSeason);

        Assert.Equal("nfl", snapshot.LeagueCode);
        Assert.Equal("gsis", snapshot.IdSource);
        Assert.Equal(2025, snapshot.LatestSeason);
        Assert.True(snapshot.KeepSeasonLines);

        var alpha = snapshot.Players.Single(p => p.ExternalId == "00-0000001");
        Assert.Equal(("Alpha", "Passer"), (alpha.FirstName, alpha.LastName));
        Assert.Equal("2023 KC, 2024 KC, 2025 KC", Path(alpha));
        Assert.Equal((2017, 2025), (alpha.FromYear, alpha.ToYear));
        Assert.Equal((1, 2017), (alpha.DraftNumber, alpha.DraftYear));
        Assert.Equal(("15", "https://static.www.nfl.com/image/upload/alpha"), (alpha.JerseyNumber, alpha.HeadshotUrl));
        // ESPN's AP awards, linked by the ESPN id in the players file; a tied vote's other winner is no one here.
        Assert.Equal([new HistoryAward(2024, AwardTypes.OffensivePlayer), new HistoryAward(2025, AwardTypes.Mvp)], alpha.Awards);
        // His career began before the stat lines this build keeps, so his lines only place him on a team.
        Assert.False(alpha.KeepSeasonLines);
        Assert.All(alpha.Seasons, s => Assert.Equal("{}", s.StatsJson));

        // Season file: the team he was traded away from first. Weekly files: in week order.
        var bravo = snapshot.Players.Single(p => p.ExternalId == "00-0000002");
        Assert.Equal("2023 ARI*, 2023 NYJ*, 2024 NYJ*, 2024 BUF*, 2025 BUF", Path(bravo));
        Assert.Equal(("Bravo", "Catcher Jr."), (bravo.FirstName, bravo.LastName));
        Assert.Equal(73, bravo.HeightInches); // "6-1"
        Assert.Equal("0", bravo.JerseyNumber); // 0 has been allowed since 2023
        Assert.Null(bravo.DraftNumber);
    }

    [Fact]
    public async Task History_leaves_out_practice_squad_players_and_one_year_backups_but_keeps_rookies()
    {
        var ids = (await History(MidSeason)).Players.Select(p => p.ExternalId).Order().ToList();

        // 3 was only ever on a practice squad, 4 played one season and left, 9 is on a team this build does not know.
        Assert.Equal(["00-0000001", "00-0000002", "00-0000005", "00-0000006"], ids);
    }

    [Fact]
    public async Task History_matches_a_row_without_an_id_by_name_and_birth_date_and_files_old_codes_under_their_franchise()
    {
        var snapshot = await History(MidSeason);

        var foxtrot = snapshot.Players.Single(p => p.ExternalId == "00-0000006");
        Assert.Equal("2023 STL, 2024 ARI", Path(foxtrot)); // a weekly injured-reserve row is not a game
        Assert.Equal("S", foxtrot.Position); // filed as SAF

        var stLouis = snapshot.Teams.Single(t => t.Key == "STL");
        Assert.Equal(("St. Louis", "Rams", "LA"), (stLouis.Team.City, stLouis.Team.Name, stLouis.FranchiseKey));
        Assert.Equal((2023, (int?)2023), (stLouis.ActiveFrom, stLouis.ActiveTo));

        var rams = snapshot.Teams.Single(t => t.Key == "LA");
        Assert.Equal(("LAR", "NFC", "NFC West"), (rams.Team.Abbreviation, rams.Team.Conference, rams.Team.Division));
        Assert.Null(rams.ActiveTo);
        Assert.Equal(33, snapshot.Teams.Count);
    }

    [Fact]
    public async Task History_scores_fame_from_the_draft_record_and_production()
    {
        var snapshot = await History(MidSeason);
        double Fame(string id) => snapshot.Players.Single(p => p.ExternalId == id).Popularity!.Value;

        // Alpha: a first overall pick with Pro Bowls and 22 points a game this season.
        Assert.True(Fame("00-0000001") > 80);
        Assert.True(Fame("00-0000001") > Fame("00-0000002"));
        Assert.True(Fame("00-0000002") > Fame("00-0000005"));
    }

    [Fact]
    public async Task History_keeps_a_stat_line_for_each_season_of_a_career_the_stats_cover()
    {
        var echo = (await History(MidSeason)).Players.Single(p => p.ExternalId == "00-0000005");

        Assert.True(echo.KeepSeasonLines);
        var line = Assert.Single(echo.Seasons);
        Assert.Equal((2025, "BUF", true, (int?)5), (line.Season, line.TeamKey, line.IsSeasonTotal, line.Games));
        Assert.Equal("""{"car":61,"ryd":288,"rtd":2,"rec":4,"wyd":31}""", line.StatsJson);
    }

    [Fact]
    public void A_season_split_by_a_trade_has_a_line_per_team_and_the_stats_on_a_combined_line()
    {
        List<NflverseHistorySource.RosterSpell> spells = [new(2024, 1, "NYJ"), new(2024, 6, "BUF"), new(2025, 1, "BUF")];
        var lines = NflverseHistorySource.CardLines(spells, new() { [2024] = 2, [2025] = 1 },
            [new(2024, 15, "BUF", "{\"rec\":50}"), new(2025, 4, "BUF", "{\"rec\":6}"), new(2023, 2, "ARI", "{\"rec\":1}")]);

        Assert.Equal(
            ["2023 ARI total 2", "2024 NYJ split -", "2024 BUF split -", "2024 - total 15", "2025 BUF total 4"],
            lines.Select(l => $"{l.Season} {l.TeamKey ?? "-"} {(l.IsSeasonTotal ? "total" : "split")} {l.Games?.ToString() ?? "-"}").ToList());
        Assert.Equal("{\"rec\":50}", lines[3].StatsJson);
    }

    [Fact]
    public async Task History_ends_with_last_season_before_kickoff()
    {
        var snapshot = await History(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2025, snapshot.LatestSeason);
    }

    [Fact]
    public async Task Roster_lists_players_on_a_roster_and_leaves_off_the_practice_squad_and_the_released()
    {
        var snapshot = await Roster(MidSeason);

        Assert.Equal(("nfl", 2025, "gsis"), (snapshot.LeagueCode, snapshot.Season, snapshot.IdSource));
        Assert.True(snapshot.Supplement);
        Assert.True(snapshot.InSeason);
        Assert.Equal(32, snapshot.Teams.Count);
        Assert.Equal(["00-0000001", "00-0000002", "00-0000005", "00-0000007"], snapshot.Players.Select(p => p.ExternalId).Order().ToList());

        var alpha = snapshot.Players.Single(p => p.ExternalId == "00-0000001");
        Assert.Equal(("KC", "15", "QB", 74), (alpha.TeamExternalId, alpha.JerseyNumber, alpha.Position, alpha.HeightInches));
        Assert.Equal(new DateOnly(1995, 9, 17), alpha.BirthDate);
        Assert.Equal("https://static.www.nfl.com/image/upload/alpha2", alpha.HeadshotUrl);
    }

    [Fact]
    public async Task Roster_falls_back_to_last_season_when_the_new_league_years_file_is_not_out()
    {
        var snapshot = await Roster(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2025, snapshot.Season);
        Assert.False(snapshot.InSeason);
    }

    [Theory]
    [InlineData("HOU", 1996, "OIL")]
    [InlineData("HOU", 2002, "HOU")]
    [InlineData("ARZ", 2010, "ARI")]
    [InlineData("BLT", 2010, "BAL")]
    [InlineData("SL", 2010, "STL")]
    [InlineData("LAR", 2020, "LA")]
    [InlineData("OAK", 2019, "OAK")]
    [InlineData("XYZ", 2019, null)]
    [InlineData(null, 2019, null)]
    public void Team_codes_are_filed_under_the_identity_of_their_season(string? code, int season, string? expected) =>
        Assert.Equal(expected, NflTeams.Instance.Value.Identity(code, season));

    [Fact]
    public void Every_former_team_points_at_a_current_franchise_and_every_division_names_its_conference()
    {
        var teams = NflTeams.Instance.Value;
        Assert.Equal(32, teams.Current.Count);
        Assert.All(teams.Former.Values, f => Assert.True(teams.Current.ContainsKey(f.Franchise)));
        Assert.All(teams.Current.Values, t => Assert.StartsWith(t.Conference + " ", t.Division));
        Assert.Equal(8, teams.Current.Values.Select(t => t.Division).Distinct().Count());
    }

    [Theory]
    [InlineData("75", 75)]
    [InlineData("6-3", 75)]
    [InlineData("0", null)]
    [InlineData(null, null)]
    [InlineData("tall", null)]
    public void Heights_read_as_inches_or_feet_and_inches(string? value, int? expected) =>
        Assert.Equal(expected, NflverseFiles.Height(value));
}

public class CsvStreamTests
{
    [Fact]
    public async Task Streaming_reads_the_same_rows_as_parsing_even_when_a_field_crosses_a_buffer()
    {
        var longField = new string('x', 20_000);
        var text = $"a,b,c\r\n\"one, \"\"quoted\"\"\",2,\n\n{longField},\"{longField}\"\"\",3\nlast,row,4";
        var parsed = CsvReader.Parse(text);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        var streamed = new List<CsvRow>();
        await foreach (var row in CsvReader.StreamAsync(stream)) streamed.Add(row);

        Assert.Equal(3, streamed.Count);
        Assert.Equal(parsed.Select(r => (r.Str("a"), r.Str("b"), r.Str("c"))), streamed.Select(r => (r.Str("a"), r.Str("b"), r.Str("c"))));
        Assert.Equal("one, \"quoted\"", streamed[0].Str("a"));
        Assert.Equal(longField + "\"", streamed[1].Str("b"));
    }

    [Fact]
    public async Task A_quote_at_the_end_of_a_buffer_is_still_read_as_a_doubled_quote()
    {
        // 16,384 characters fill the reader's first block, ending on the first quote of a doubled pair.
        var head = "a\n\"";
        var filler = new string('y', 16 * 1024 - head.Length - 1);
        var text = head + filler + "\"\"z\"\n";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        var rows = new List<CsvRow>();
        await foreach (var row in CsvReader.StreamAsync(stream)) rows.Add(row);

        Assert.Equal(filler + "\"z", Assert.Single(rows).Str("a"));
    }
}

public class ImportLeaguesTests
{
    [Theory]
    [InlineData("nba,nfl", new[] { "nba", "nfl" })]
    [InlineData(" NFL ", new[] { "nfl" })]
    [InlineData("nba,,nba", new[] { "nba" })]
    [InlineData("", new string[0])]
    public void The_leagues_setting_is_a_comma_separated_list(string setting, string[] expected) =>
        Assert.Equal(expected.Order(), KnowBall.Importer.ImportJob.LeaguesOf(setting).Order());
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using KnowBall.Application.Import;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

/// <summary>Runs the NHL sources over the small made-up answers in tests/fixtures/nhl, saved as the files the sources name.</summary>
public class NhlApiSourceTests
{
    public static readonly string FixtureDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(BrefCsvSourceTests.FixtureDirectory)!, "nhl");

    private static NhlApiOptions Options() => new() { Directory = FixtureDirectory, FirstSeason = 1990, MinGames = 50 };

    private static readonly DateTimeOffset MidSeason = new(2025, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static async Task<HistorySnapshot> History(DateTimeOffset now) =>
        await new NhlHistorySource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<NhlHistorySource>.Instance).FetchAsync(CancellationToken.None);

    private static async Task<SourceSnapshot> Roster(DateTimeOffset now) =>
        await new NhlRosterSource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(now),
            NullLogger<NhlRosterSource>.Instance).FetchAsync(CancellationToken.None);

    private static string Path(HistoryPlayer p) =>
        string.Join(", ", p.Seasons.Select(s => $"{s.Season} {s.TeamKey}{(s.IsSeasonTotal ? "" : "*")}"));

    [Fact]
    public async Task History_reads_the_players_with_enough_games_and_everyone_playing_this_season()
    {
        var snapshot = await History(MidSeason);

        Assert.Equal(("nhl", "nhl", 2025), (snapshot.LeagueCode, snapshot.IdSource, snapshot.LatestSeason));
        // 8000004 has too few games; 8000006 is listed this season but has no record.
        Assert.Equal(["8000001", "8000002", "8000003", "8000005", "8000008"], snapshot.Players.Select(p => p.ExternalId));
    }

    [Fact]
    public async Task History_keeps_a_traded_players_teams_in_the_order_played_with_a_line_for_the_whole_season()
    {
        var alpha = (await History(MidSeason)).Players.Single(p => p.ExternalId == "8000001");

        Assert.Equal(("Alpha", "Sniper", "C"), (alpha.FirstName, alpha.LastName, alpha.Position));
        // Minor-league and playoff lines are not part of the record.
        Assert.Equal("2023 TOR, 2024 TOR*, 2024 BOS*, 2024 , 2025 BOS", Path(alpha));
        Assert.Equal((2023, 2025), (alpha.FromYear, alpha.ToYear));

        var y2024 = alpha.Seasons.Where(s => s.Season == 2024).ToList();
        Assert.Equal(
            ["TOR 50 {\"g\":25,\"a\":30,\"pts\":55,\"pm\":10,\"pim\":10,\"ppg\":8,\"gwg\":4,\"sog\":170}",
             "BOS 30 {\"g\":15,\"a\":15,\"pts\":30,\"pm\":-4,\"pim\":6,\"ppg\":5,\"gwg\":2,\"sog\":100}",
             "- 80 {\"g\":40,\"a\":45,\"pts\":85,\"pm\":6,\"pim\":16,\"ppg\":13,\"gwg\":6,\"sog\":270}"],
            y2024.Select(s => $"{s.TeamKey ?? "-"} {s.Games} {s.StatsJson}"));
        Assert.Equal((1.06, 20.0), (y2024[^1].PointsPerGame!.Value, y2024[0].MinutesPerGame!.Value));
        Assert.Null(y2024[^1].MinutesPerGame); // a traded season's average time is not added up

        Assert.Equal(
            [new HistoryAward(2023, AwardTypes.RocketRichard), new HistoryAward(2023, AwardTypes.StanleyCup),
             new HistoryAward(2024, AwardTypes.ArtRoss), new HistoryAward(2024, AwardTypes.Mvp)],
            alpha.Awards);
        Assert.Equal(("Boston University", "Canada"), (alpha.College, alpha.Country));
        Assert.Equal((2015, 1, "91"), (alpha.DraftYear!.Value, alpha.DraftNumber!.Value, alpha.JerseyNumber));
        Assert.Equal((73, 200), (alpha.HeightInches!.Value, alpha.WeightLbs!.Value));
        Assert.False(alpha.HallOfFame);
        Assert.NotNull(alpha.Popularity);
    }

    [Fact]
    public async Task History_works_out_a_goalies_average_and_save_percentage_from_his_totals()
    {
        var bravo = (await History(MidSeason)).Players.Single(p => p.ExternalId == "8000002");

        Assert.Equal("G", bravo.Position);
        Assert.Equal(
            "{\"gs\":58,\"w\":35,\"l\":18,\"otl\":5,\"gaa\":2.5,\"svp\":0.912,\"so\":4}",
            bravo.Seasons.Single(s => s.Season == 2023).StatsJson);
        Assert.All(bravo.Seasons, s => Assert.Null(s.PointsPerGame));
        Assert.Equal([new HistoryAward(2024, AwardTypes.Vezina)], bravo.Awards);
        Assert.Equal("Sweden", bravo.Country);
    }

    [Fact]
    public async Task History_reads_only_from_the_first_season_but_dates_the_career_from_its_start()
    {
        var snapshot = await History(MidSeason);
        var charlie = snapshot.Players.Single(p => p.ExternalId == "8000003");

        Assert.Equal("1990 QUE, 1991 HFD, 1992 HFD", Path(charlie));
        Assert.Equal((1989, 1992), (charlie.FromYear, charlie.ToYear));
        Assert.True(charlie.HallOfFame);
        Assert.Equal("D", charlie.Position);
        Assert.Equal("4", charlie.JerseyNumber);

        // Former teams point at the team they became, with its conference; a new nickname gets no logo.
        var quebec = snapshot.Teams.Single(t => t.Key == "QUE");
        Assert.Equal(("COL", 1990, 1990), (quebec.FranchiseKey, quebec.ActiveFrom, quebec.ActiveTo));
        Assert.Equal(("Quebec", "Nordiques", "Western"), (quebec.Team.City, quebec.Team.Name, quebec.Team.Conference));
        Assert.Null(quebec.Team.LogoUrl);
        Assert.Equal("CAR", snapshot.Teams.Single(t => t.Key == "HFD").FranchiseKey);
        Assert.Equal(32, snapshot.Teams.Count(t => t.FranchiseKey is null && t.Team.LogoUrl is not null));
    }

    [Fact]
    public async Task History_files_a_team_under_each_name_it_has_played_under_and_skips_names_it_does_not_know()
    {
        var echo = (await History(MidSeason)).Players.Single(p => p.ExternalId == "8000005");

        // Utah Hockey Club and the Mammoth are one team; a team this build does not know has no line of its own, but
        // its games still count in the season's total.
        Assert.Equal("2024 UTA, 2025 UTA*, 2025 ", Path(echo));
        Assert.Equal(6, echo.Seasons[^1].Games);
        Assert.Equal(("RW", "Czechia", "0"), (echo.Position, echo.Country, echo.JerseyNumber));
    }

    [Fact]
    public async Task History_reads_each_teams_record_with_overtime_losses_ties_and_playoff_berths()
    {
        var snapshot = await History(MidSeason);

        // A team with no games yet has no record; the lockout season has none at all.
        Assert.Equal(
            ["1991 HFD 26-41-13 OTL 0 playoffs", "1991 QUE 20-48-12 OTL 0",
             "2024 UTA 38-31-0 OTL 13", "2024 TOR 52-26-0 OTL 4 playoffs",
             "2025 BOS 12-8-0 OTL 2", "2025 UTA 10-9-0 OTL 3"],
            snapshot.TeamSeasons!.Select(r => $"{r.Season} {r.TeamKey} {r.Wins}-{r.Losses}-{r.Ties} OTL {r.OvertimeLosses}{(r.MadePlayoffs ? " playoffs" : "")}"));
    }

    [Fact]
    public async Task Roster_lists_every_player_on_a_team_today()
    {
        var snapshot = await Roster(MidSeason);

        Assert.Equal(("nhl", 2025, "nhl"), (snapshot.LeagueCode, snapshot.Season, snapshot.IdSource));
        Assert.True(snapshot.Supplement);
        Assert.True(snapshot.InSeason);
        Assert.Equal(32, snapshot.Teams.Count);
        Assert.Equal(
            ["8000001 BOS C 91 Canada", "8000002 PHI G 35 Sweden", "8000005 UTA RW 0 Czechia", "8000007 UTA D 44 XYZ"],
            snapshot.Players.OrderBy(p => p.ExternalId).Select(p => $"{p.ExternalId} {p.TeamExternalId} {p.Position} {p.JerseyNumber} {p.Country}"));
        var echo = snapshot.Players.Single(p => p.ExternalId == "8000005");
        Assert.Equal((new DateOnly(2006, 9, 9), 72), (echo.BirthDate!.Value, echo.HeightInches!.Value));
    }

    [Fact]
    public async Task Roster_fails_when_a_team_is_missing_rather_than_retire_its_players()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nhl-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(dir, "rosters"));
        File.WriteAllText(System.IO.Path.Combine(dir, "rosters", "BOS.json"), "{\"forwards\":[]}");
        try
        {
            var source = new NhlRosterSource(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new NhlApiOptions { Directory = dir }),
                new FakeTimeProvider(MidSeason), NullLogger<NhlRosterSource>.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync(CancellationToken.None));
            Assert.Contains("ANA", error.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(2025, 10, 1, 2025, 2025, true)]
    [InlineData(2025, 9, 30, 2024, 2025, false)]
    [InlineData(2026, 6, 20, 2025, 2025, true)]
    [InlineData(2026, 7, 1, 2025, 2026, false)]
    public void Calendar_starts_a_season_in_October_and_a_league_year_in_July(int y, int m, int d, int played, int roster, bool inSeason)
    {
        var now = new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal((played, roster, inSeason), (NhlCalendar.LatestPlayedSeason(now), NhlCalendar.RosterSeason(now), NhlCalendar.InSeason(now)));
        Assert.Equal(20232024, NhlCalendar.SeasonId(2023));
        Assert.Equal(2023, NhlCalendar.Season(20232024));
    }

    [Fact]
    public void Every_current_team_is_in_a_conference_and_division_and_every_former_franchise_is_known()
    {
        var teams = NhlTeams.Instance.Value;
        Assert.Equal(32, teams.Current.Count);
        Assert.Equal(16, teams.Current.Values.Count(t => t.Conference == "Eastern"));
        Assert.Equal(4, teams.Current.Values.Select(t => t.Division).Distinct().Count());
        Assert.All(teams.Former.Values, f => Assert.True(f.Franchise is null || teams.KeyOfCode(f.Franchise) is not null, f.Code));

        // The Coyotes moved twice and then stopped: none of their names leads to a current team.
        Assert.Equal("WIN", teams.Key("Winnipeg Jets (1979)"));
        Assert.Equal("WPG", teams.Key("Winnipeg Jets"));
        Assert.Null(teams.CurrentFranchise("WIN"));
        Assert.Equal("WPG", teams.CurrentFranchise("ATL")!.Key);
        Assert.Equal("MTL", teams.Key("Montreal Canadiens"));
    }
}

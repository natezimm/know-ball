using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowBall.IntegrationTests;

/// <summary>The NHL editions over HTTP, on players loaded from tests/fixtures/nhl by the real importer services.</summary>
[Collection(ApiCollection.Name)]
public sealed class NhlGameApiTests(ApiFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.EnsureHistoryAsync();
        await factory.EnsureNhlAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient NewPlayer()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", Guid.NewGuid().ToString());
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<int> PlayerId(HttpClient client, string name, bool includeInactive = false)
    {
        var found = await Json(await client.GetAsync($"/api/players/search?league=nhl&q={Uri.EscapeDataString(name)}&includeInactive={includeInactive}"));
        return found.EnumerateArray().First(p => p.GetProperty("fullName").GetString() == name).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Nhl_career_path_is_its_own_daily_and_is_won_through_the_nhl_search()
    {
        var client = NewPlayer();
        var nhl = await Json(await client.GetAsync("/api/career-path/daily?league=nhl"));
        Assert.Equal("nhl", nhl.GetProperty("league").GetString());
        Assert.Equal(JsonValueKind.Null, nhl.GetProperty("answer").ValueKind);

        // The one fixture player through three franchises or more; Utah Hockey Club is the Mammoth, and an NHL season
        // ends the year after it starts.
        var stops = nhl.GetProperty("payload").GetProperty("stops").EnumerateArray()
            .Select(s => $"{s.GetProperty("abbreviation").GetString()} {s.GetProperty("from").GetInt32()}-{s.GetProperty("to").GetInt32()}")
            .ToList();
        Assert.Equal(["WPG 2015-2017", "VGK 2017-2018", "SEA 2021-2022", "UTA 2024-2025"], stops);

        var puzzleId = nhl.GetProperty("puzzleId").GetInt32();
        var miss = await Json(await client.PostAsJsonAsync($"/api/career-path/{puzzleId}/guess", new { playerId = await PlayerId(client, "Alpha Sniper") }));
        Assert.Equal("From", miss.GetProperty("clues").GetProperty("hints")[2].GetProperty("label").GetString());

        var won = await Json(await client.PostAsJsonAsync($"/api/career-path/{puzzleId}/guess", new { playerId = await PlayerId(client, "Golf Journeyman", true) }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal("USA", won.GetProperty("clues").GetProperty("hints")[2].GetProperty("value").GetString());

        var stats = (await Json(await client.GetAsync("/api/me/stats"))).EnumerateArray()
            .Single(s => s.GetProperty("game").GetString() == "career-path" && s.GetProperty("league").GetString() == "nhl");
        Assert.Equal(1, stats.GetProperty("wins").GetInt32());
    }

    [Fact]
    public async Task Nhl_scouting_report_keeps_its_answer_back_and_scores_a_forward_as_close_to_a_forward()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/scouting-report/daily?league=nhl"));
        Assert.Equal("nhl", daily.GetProperty("league").GetString());
        var puzzleId = daily.GetProperty("puzzleId").GetInt32();

        // A guess from another league is refused.
        var nbaPlayer = (await Json(await client.GetAsync("/api/players/search?q=curry")))[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = nbaPlayer })).StatusCode);

        var guess = await Json(await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = await PlayerId(client, "Alpha Sniper") }));
        var feedback = guess.GetProperty("guesses")[0].GetProperty("feedback");
        Assert.Equal("Eastern", feedback.GetProperty("conference").GetProperty("value").GetString());
        Assert.Equal("Atlantic", feedback.GetProperty("division").GetProperty("value").GetString());
        Assert.Equal("C", feedback.GetProperty("position").GetProperty("value").GetString());
        if (guess.GetProperty("status").GetString() == "playing")
            Assert.Equal(JsonValueKind.Null, guess.GetProperty("answer").ValueKind);
    }

    [Fact]
    public async Task Nhl_back_of_the_card_prints_a_skaters_seasons_by_two_year_season_and_is_won()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/back-of-the-card/daily?league=nhl"));
        Assert.Equal("nhl", daily.GetProperty("league").GetString());
        var payload = daily.GetProperty("payload");
        Assert.True(payload.GetProperty("splitYears").GetBoolean());
        Assert.Equal(["G", "A", "Pts", "+/-"], payload.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("label").GetString()));
        // Golf Journeyman is the one fixture player with five seasons.
        Assert.Equal([2015, 2016, 2017, 2021, 2024], payload.GetProperty("seasons").EnumerateArray().Select(s => s.GetProperty("season").GetInt32()));
        Assert.Equal(JsonValueKind.Null, daily.GetProperty("answer").ValueKind);

        var puzzleId = daily.GetProperty("puzzleId").GetInt32();
        var won = await Json(await client.PostAsJsonAsync($"/api/back-of-the-card/{puzzleId}/guess", new { playerId = await PlayerId(client, "Golf Journeyman", true) }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal(["WPG", "WPG", "VGK", "SEA", "UTA"], won.GetProperty("clues").GetProperty("teams").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task Nhl_team_photo_is_a_stanley_cup_final_teams_starting_six()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/team-photo/daily?league=nhl"));
        Assert.Equal("nhl", daily.GetProperty("league").GetString());
        var payload = daily.GetProperty("payload");
        Assert.Equal(("FLA", 2024, "won"), (payload.GetProperty("team").GetProperty("abbreviation").GetString(),
            payload.GetProperty("season").GetInt32(), payload.GetProperty("finals").GetString()));
        Assert.Equal((52, 24, 6), (payload.GetProperty("wins").GetInt32(), payload.GetProperty("losses").GetInt32(), payload.GetProperty("overtimeLosses").GetInt32()));
        Assert.Equal(["LW", "C", "RW", "D", "G", "D"], payload.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("position").GetString()));
        Assert.DoesNotContain("Pivot", payload.GetRawText());

        var puzzleId = daily.GetProperty("puzzleId").GetInt32();
        var bench = await Json(await client.PostAsJsonAsync($"/api/team-photo/{puzzleId}/guess", new { playerId = await PlayerId(client, "Mike Spare", true) }));
        Assert.True(bench.GetProperty("guesses")[0].GetProperty("feedback").GetProperty("bench").GetBoolean());

        JsonElement last = bench;
        foreach (var name in new[] { "Hotel Winger", "India Pivot", "Juliet Flank", "Kilo Rear", "November Crease", "Lima Pair" })
            last = await Json(await client.PostAsJsonAsync($"/api/team-photo/{puzzleId}/guess", new { playerId = await PlayerId(client, name, true) }));
        Assert.Equal("won", last.GetProperty("status").GetString());
    }
}

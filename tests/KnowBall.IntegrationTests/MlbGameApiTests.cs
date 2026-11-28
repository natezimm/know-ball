using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowBall.IntegrationTests;

/// <summary>The MLB editions over HTTP, on players loaded from tests/fixtures/mlbstats by the real importer services.</summary>
[Collection(ApiCollection.Name)]
public sealed class MlbGameApiTests(ApiFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.EnsureHistoryAsync();
        await factory.EnsureMlbAsync();
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

    private async Task<int> PlayerId(HttpClient client, string name)
    {
        var found = await Json(await client.GetAsync($"/api/players/search?league=mlb&q={Uri.EscapeDataString(name)}&includeInactive=true"));
        return found.EnumerateArray().First(p => p.GetProperty("fullName").GetString() == name).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Mlb_career_path_follows_a_traded_pitcher_through_the_mlb_search()
    {
        var client = NewPlayer();
        var mlb = await Json(await client.GetAsync("/api/career-path/daily?league=mlb"));
        var nfl = await Json(await client.GetAsync("/api/career-path/daily?league=nfl"));

        Assert.Equal(("mlb", "nfl"), (mlb.GetProperty("league").GetString(), nfl.GetProperty("league").GetString()));
        Assert.NotEqual(nfl.GetProperty("puzzleId").GetInt32(), mlb.GetProperty("puzzleId").GetInt32());
        Assert.Equal(JsonValueKind.Null, mlb.GetProperty("answer").ValueKind);

        // The one fixture player with three clubs: Oakland, then Washington, traded to the Yankees in 2025, where he still is.
        var stops = mlb.GetProperty("payload").GetProperty("stops").EnumerateArray()
            .Select(s => $"{s.GetProperty("abbreviation").GetString()} {s.GetProperty("from").GetInt32()}-{(s.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Number ? to.GetInt32().ToString() : "now")}")
            .ToList();
        Assert.Equal(["OAK 2024-2024", "WSH 2025-2025", "NYY 2025-now"], stops);

        var puzzleId = mlb.GetProperty("puzzleId").GetInt32();
        var won = await Json(await client.PostAsJsonAsync($"/api/career-path/{puzzleId}/guess", new { playerId = await PlayerId(client, "Bravo Arm") }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal("Bravo Arm", won.GetProperty("answer").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Mlb_scouting_report_scores_the_american_and_national_leagues_and_refuses_an_nba_guess()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/scouting-report/daily?league=mlb"));
        Assert.Equal("mlb", daily.GetProperty("league").GetString());
        foreach (var name in new[] { "Slugger", "Bravo Arm", "Rookie", "Catcher" })
            Assert.DoesNotContain(name, daily.GetRawText());
        var puzzleId = daily.GetProperty("puzzleId").GetInt32();

        var nbaId = (await Json(await client.GetAsync("/api/players/search?q=curry")))[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = nbaId })).StatusCode);

        var guess = await Json(await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = await PlayerId(client, "Delta Rookie Jr.") }));
        var feedback = guess.GetProperty("guesses")[0].GetProperty("feedback");
        Assert.Equal(("NL", "NL East", "SS"),
            (feedback.GetProperty("conference").GetProperty("value").GetString(), feedback.GetProperty("division").GetProperty("value").GetString(),
                feedback.GetProperty("position").GetProperty("value").GetString()));
    }
}

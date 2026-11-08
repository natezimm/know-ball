using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Application;

namespace KnowBall.IntegrationTests;

/// <summary>The NFL editions over HTTP, on players loaded from tests/fixtures/nflverse by the real importer services.</summary>
[Collection(ApiCollection.Name)]
public sealed class NflGameApiTests(ApiFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.EnsureHistoryAsync();
        await factory.EnsureNflAsync();
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
        var found = await Json(await client.GetAsync($"/api/players/search?league=nfl&q={Uri.EscapeDataString(name)}&includeInactive={includeInactive}"));
        return found.EnumerateArray().First(p => p.GetProperty("fullName").GetString() == name).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Nfl_career_path_is_its_own_daily_and_is_won_through_the_nfl_search()
    {
        var client = NewPlayer();
        var nfl = await Json(await client.GetAsync("/api/career-path/daily?league=nfl"));
        var nba = await Json(await client.GetAsync("/api/career-path/daily"));

        Assert.Equal(("nfl", "nba"), (nfl.GetProperty("league").GetString(), nba.GetProperty("league").GetString()));
        Assert.NotEqual(nba.GetProperty("puzzleId").GetInt32(), nfl.GetProperty("puzzleId").GetInt32());
        Assert.Equal(JsonValueKind.Null, nfl.GetProperty("answer").ValueKind);

        // The one fixture player through three franchises: Arizona, the Jets, then Buffalo, where he still is.
        var stops = nfl.GetProperty("payload").GetProperty("stops").EnumerateArray()
            .Select(s => $"{s.GetProperty("abbreviation").GetString()} {s.GetProperty("from").GetInt32()}-{(s.TryGetProperty("to", out var to) ? to.GetInt32().ToString() : "now")}")
            .ToList();
        Assert.Equal(["ARI 2023-2023", "NYJ 2023-2024", "BUF 2024-now"], stops);

        var puzzleId = nfl.GetProperty("puzzleId").GetInt32();
        var won = await Json(await client.PostAsJsonAsync($"/api/career-path/{puzzleId}/guess", new { playerId = await PlayerId(client, "Bravo Catcher Jr.", true) }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal("Bravo Catcher Jr.", won.GetProperty("answer").GetProperty("name").GetString());

        var archive = await Json(await client.GetAsync("/api/career-path/archive?league=nfl"));
        Assert.Equal((puzzleId, "won"), (archive[0].GetProperty("puzzleId").GetInt32(), archive[0].GetProperty("status").GetString()));
        var stats = (await Json(await client.GetAsync("/api/me/stats"))).EnumerateArray()
            .Single(s => s.GetProperty("game").GetString() == "career-path" && s.GetProperty("league").GetString() == "nfl");
        Assert.Equal(1, stats.GetProperty("wins").GetInt32());
    }

    [Fact]
    public async Task Nfl_scouting_report_keeps_its_answer_back_and_refuses_an_nba_guess()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/scouting-report/daily?league=nfl"));
        Assert.Equal("nfl", daily.GetProperty("league").GetString());
        Assert.DoesNotContain("Passer", daily.GetRawText());
        var puzzleId = daily.GetProperty("puzzleId").GetInt32();

        var nbaPlayer = await Json(await client.GetAsync("/api/players/search?q=curry"));
        var nbaId = nbaPlayer[0].GetProperty("id").GetInt32();
        var refused = await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = nbaId });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var guess = await Json(await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = await PlayerId(client, "Alpha Passer") }));
        var feedback = guess.GetProperty("guesses")[0].GetProperty("feedback");
        Assert.Contains(feedback.GetProperty("conference").GetProperty("value").GetString(), new[] { "AFC", "NFC" });
        Assert.Equal("AFC West", feedback.GetProperty("division").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Leagues_without_an_edition_are_not_found()
    {
        var client = NewPlayer();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/back-of-the-card/daily?league=nfl")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/career-path/daily?league=mlb")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/scouting-report/practice?league=nhl", null)).StatusCode);
    }

    [Fact]
    public async Task An_nfl_practice_round_is_an_nfl_puzzle_off_the_calendar()
    {
        var practice = await Json(await NewPlayer().PostAsync("/api/scouting-report/practice?league=nfl", null));
        Assert.True(practice.GetProperty("practice").GetBoolean());
        Assert.Equal("nfl", practice.GetProperty("league").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ITriviaDb>();
        var id = practice.GetProperty("puzzleId").GetInt32();
        Assert.Equal("nfl", await db.Puzzles.Where(p => p.Id == id).Select(p => p.League!.Code).SingleAsync());
    }
}

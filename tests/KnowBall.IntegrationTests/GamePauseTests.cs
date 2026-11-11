using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace KnowBall.IntegrationTests;

/// <summary>The per-game flags that take one game off the site while the others carry on.</summary>
[Collection(ApiCollection.Name)]
public sealed class GamePauseTests(ApiFactory factory)
{
    private HttpClient Client(params string[] pausedFlags)
    {
        var client = pausedFlags.Length == 0
            ? factory.CreateClient()
            : factory.WithWebHostBuilder(builder =>
            {
                foreach (var flag in pausedFlags) builder.UseSetting($"FeatureManagement:{flag}", "false");
            }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task Every_game_is_on_out_of_the_box()
    {
        var paused = await Client().GetFromJsonAsync<string[]>("/api/games/paused");

        Assert.Empty(paused!);
    }

    [Fact]
    public async Task A_paused_daily_game_answers_503_and_the_others_still_play()
    {
        var client = Client("CareerPath");

        var paused = await client.GetAsync("/api/career-path/daily");
        var other = await client.GetAsync("/api/scouting-report/daily");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
        Assert.Equal("Career Path is taking a short break. The other games are still on.",
            (await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, other.StatusCode);
        Assert.Equal(["career-path"], await client.GetFromJsonAsync<string[]>("/api/games/paused"));
    }

    [Fact]
    public async Task A_paused_game_stays_paused_whatever_the_case_of_its_address()
    {
        var client = Client("CareerPath");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/Career-Path/daily")).StatusCode);
    }

    [Fact]
    public async Task Guesses_and_practice_are_paused_with_the_game()
    {
        var client = Client("TeamPhoto");

        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/team-photo/1/guess", new { playerId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/team-photo/practice", null)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/team-photo/archive")).StatusCode);
    }

    [Fact]
    public async Task Letter_Jacket_and_Numbers_Dont_Lie_have_their_own_flags()
    {
        var client = Client("LetterJacket", "NumbersDontLie");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/letter-jacket/games", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/numbers-dont-lie/daily")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/today")).StatusCode);
    }
}

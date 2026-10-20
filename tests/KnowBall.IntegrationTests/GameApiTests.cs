using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Application;
using KnowBall.Domain;

namespace KnowBall.IntegrationTests;

/// <summary>
/// The daily games over HTTP, as the Angular app plays them, on the player history from
/// tests/fixtures/bref with the seed roster on top.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GameApiTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureHistoryAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A browser: every request carries its own made-up player id.</summary>
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

    private async Task<T> Db<T>(Func<ITriviaDb, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ITriviaDb>());
    }

    private Task<Player> AnswerOf(int puzzleId) => Db(async db =>
    {
        var answerId = (await db.Puzzles.AsNoTracking().SingleAsync(p => p.Id == puzzleId)).AnswerPlayerId;
        return await db.Players.AsNoTracking().SingleAsync(p => p.Id == answerId);
    });

    [Fact]
    public async Task A_request_without_a_player_id_is_refused()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/scouting-report/daily");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Anon-Id", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Only_the_three_daily_games_answer_on_game_routes()
    {
        var client = NewPlayer();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/spordle/daily")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/players/daily")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/players/search?q=curry")).StatusCode);
    }

    [Fact]
    public async Task Scouting_report_never_sends_the_answer_before_the_game_ends()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/scouting-report/daily"));
        var puzzleId = daily.GetProperty("puzzleId").GetInt32();
        var answer = await AnswerOf(puzzleId);

        Assert.Equal(("scouting-report", 8, "playing"),
            (daily.GetProperty("game").GetString(), daily.GetProperty("maxGuesses").GetInt32(), daily.GetProperty("status").GetString()));
        AssertHidden(daily, answer);

        // Eight other current players: every guess is wrong.
        var wrong = await Db(db => db.Players.AsNoTracking()
            .Where(p => p.IsActive && p.Id != answer.Id).OrderBy(p => p.Id).Take(8).Select(p => p.Id).ToListAsync());

        for (var i = 0; i < wrong.Count; i++)
        {
            var state = await Json(await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = wrong[i] }));
            var last = i == wrong.Count - 1;

            Assert.Equal(i + 1, state.GetProperty("guesses").GetArrayLength());
            var feedback = state.GetProperty("guesses")[i].GetProperty("feedback");
            Assert.Equal(
                ["age", "conference", "division", "height", "number", "position", "team"],
                feedback.EnumerateObject().Select(p => p.Name).Order());
            Assert.All(feedback.EnumerateObject(), clue => Assert.Contains(clue.Value.GetProperty("state").GetString(), new[] { "ok", "close", "miss" }));

            if (!last)
            {
                AssertHidden(state, answer);
                AssertHidden(await Json(await client.GetAsync("/api/scouting-report/daily")), answer); // and on a reload
            }
            else
            {
                Assert.Equal("lost", state.GetProperty("status").GetString());
                Assert.Equal(answer.FullName, state.GetProperty("answer").GetProperty("name").GetString());
            }
        }

        var after = await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = answer.Id });
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
    }

    private static void AssertHidden(JsonElement state, Player answer)
    {
        Assert.Equal(JsonValueKind.Null, state.GetProperty("answer").ValueKind);
        var body = state.GetRawText();
        Assert.DoesNotContain(answer.FullName, body);
        Assert.DoesNotContain(answer.LastName, body);
        if (answer.HeadshotUrl is not null) Assert.DoesNotContain(answer.HeadshotUrl, body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Career_path_is_solved_through_the_search_box_with_retired_players()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/career-path/daily"));
        var puzzleId = daily.GetProperty("puzzleId").GetInt32();
        var answer = await AnswerOf(puzzleId);

        var stops = daily.GetProperty("payload").GetProperty("stops");
        Assert.True(stops.GetArrayLength() >= 3);
        Assert.All(stops.EnumerateArray(), stop => Assert.False(string.IsNullOrWhiteSpace(stop.GetProperty("team").GetString())));
        Assert.Equal(4, daily.GetProperty("clues").GetProperty("hints").GetArrayLength());
        AssertHidden(daily, answer);

        // The guess box: retired players only appear when asked for.
        var retired = await Json(await client.GetAsync("/api/players/search?q=vince%20carter&includeInactive=true"));
        Assert.Equal(("Vince Carter", false, "1998–2020"),
            (retired[0].GetProperty("fullName").GetString(), retired[0].GetProperty("isActive").GetBoolean(), retired[0].GetProperty("years").GetString()));
        Assert.Equal(0, (await Json(await client.GetAsync("/api/players/search?q=vince%20carter"))).GetArrayLength());

        var found = await Json(await client.GetAsync($"/api/players/search?q={Uri.EscapeDataString(answer.FullName)}&includeInactive=true"));
        var answerId = found.EnumerateArray().First(p => p.GetProperty("fullName").GetString() == answer.FullName).GetProperty("id").GetInt32();

        var won = await Json(await client.PostAsJsonAsync($"/api/career-path/{puzzleId}/guess", new { playerId = answerId }));
        Assert.Equal("won", won.GetProperty("status").GetString());
        Assert.Equal(answer.FullName, won.GetProperty("answer").GetProperty("name").GetString());

        // The win shows up everywhere this player looks.
        var stats = (await Json(await client.GetAsync("/api/me/stats"))).EnumerateArray().Single(s => s.GetProperty("game").GetString() == "career-path");
        Assert.Equal((1, 1, 1), (stats.GetProperty("played").GetInt32(), stats.GetProperty("wins").GetInt32(), stats.GetProperty("currentStreak").GetInt32()));
        Assert.Equal(1, stats.GetProperty("distribution")[0].GetInt32());

        var today = (await Json(await client.GetAsync("/api/me/today"))).EnumerateArray().Single(t => t.GetProperty("game").GetString() == "career-path");
        Assert.Equal(("won", 1), (today.GetProperty("status").GetString(), today.GetProperty("guesses").GetInt32()));

        var archive = await Json(await client.GetAsync("/api/career-path/archive"));
        Assert.Equal((puzzleId, "won"), (archive[0].GetProperty("puzzleId").GetInt32(), archive[0].GetProperty("status").GetString()));

        // Someone else has not played it.
        var other = (await Json(await NewPlayer().GetAsync("/api/me/today"))).EnumerateArray().Single(t => t.GetProperty("game").GetString() == "career-path");
        Assert.Equal("new", other.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_practice_round_is_a_fresh_puzzle_outside_the_calendar()
    {
        var client = NewPlayer();
        var daily = await Json(await client.GetAsync("/api/scouting-report/daily"));

        var practice = await Json(await client.PostAsync("/api/scouting-report/practice", null));

        Assert.True(practice.GetProperty("practice").GetBoolean());
        Assert.NotEqual(daily.GetProperty("puzzleId").GetInt32(), practice.GetProperty("puzzleId").GetInt32());
        Assert.Equal(JsonValueKind.Null, practice.GetProperty("number").ValueKind);

        var again = await Json(await client.GetAsync($"/api/scouting-report/puzzles/{practice.GetProperty("puzzleId").GetInt32()}"));
        Assert.True(again.GetProperty("practice").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/whos-that/practice", null)).StatusCode);
    }

    [Theory]
    [InlineData("/api/whos-that/daily")]
    [InlineData("/api/whos-that/archive")]
    [InlineData("/api/admin/overview")]
    [InlineData("/api/admin/whos-that/puzzles")]
    public async Task Removed_photo_game_endpoints_are_not_available(string path)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await NewPlayer().GetAsync(path)).StatusCode);
        var today = await Json(await NewPlayer().GetAsync("/api/me/today"));
        Assert.Equal(["scouting-report", "career-path"], today.EnumerateArray().Select(t => t.GetProperty("game").GetString()));
    }

    [Fact]
    public async Task A_guess_must_name_a_real_player()
    {
        var client = NewPlayer();
        var puzzleId = (await Json(await client.GetAsync("/api/scouting-report/daily"))).GetProperty("puzzleId").GetInt32();

        var noBody = await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { });
        var unknown = await client.PostAsJsonAsync($"/api/scouting-report/{puzzleId}/guess", new { playerId = 9_999_999 });
        var noPuzzle = await client.PostAsJsonAsync("/api/scouting-report/9999999/guess", new { playerId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Pick a player from the list.", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, noPuzzle.StatusCode);
    }
}

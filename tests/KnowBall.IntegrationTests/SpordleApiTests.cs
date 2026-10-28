using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.IntegrationTests;

/// <summary>The Spordle endpoints as the Angular app calls them.</summary>
[Collection(ApiCollection.Name)]
public sealed class SpordleApiTests(ApiFactory factory)
{
    // Real words that are not sports answers, so a game played with them is always lost.
    private static readonly string[] WrongGuesses = ["about", "other", "which", "their", "there", "would"];

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> StartGameAsync(int wordLength = 5)
    {
        var response = await _client.PostAsJsonAsync("/api/spordle/games", new { wordLength });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gameId").GetString()!;
    }

    private Task<HttpResponseMessage> GuessAsync(string gameId, string word) =>
        _client.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word });

    private Task<HttpResponseMessage> HintAsync(string gameId, string type) =>
        _client.PostAsJsonAsync($"/api/spordle/games/{gameId}/hint", new { type });

    [Fact]
    public async Task Resume_endpoint_returns_the_board_and_only_reveals_a_finished_answer()
    {
        var gameId = await StartGameAsync();
        await GuessAsync(gameId, "about");
        var hint = await (await HintAsync(gameId, "letter")).Content.ReadFromJsonAsync<JsonElement>();
        var response = await _client.GetAsync($"/api/spordle/games/{gameId}");
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(gameId, state.GetProperty("gameId").GetString());
        Assert.Equal(5, state.GetProperty("wordLength").GetInt32());
        Assert.False(state.GetProperty("complete").GetBoolean());
        Assert.True(!state.TryGetProperty("answer", out var answer) || answer.ValueKind == JsonValueKind.Null);
        var guess = Assert.Single(state.GetProperty("guesses").EnumerateArray());
        Assert.Equal("about", guess.GetProperty("word").GetString());
        Assert.All(guess.GetProperty("score").EnumerateArray(), s => Assert.Contains(s.GetString(), new[] { "correct", "present", "absent" }));
        Assert.Equal(hint.GetRawText(), state.GetProperty("letterHint").GetRawText());
        foreach (var word in WrongGuesses.Skip(1)) await GuessAsync(gameId, word);
        var finished = await _client.GetFromJsonAsync<JsonElement>($"/api/spordle/games/{gameId}");
        Assert.True(finished.GetProperty("complete").GetBoolean());
        Assert.False(finished.GetProperty("won").GetBoolean());
        Assert.Equal(6, finished.GetProperty("guesses").GetArrayLength());
        Assert.Equal(5, finished.GetProperty("answer").GetString()!.Length);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/spordle/games/nope")).StatusCode);
    }

    [Fact]
    public async Task Starting_a_game_returns_only_an_id_length_and_attempt_limit()
    {
        var response = await _client.PostAsJsonAsync("/api/spordle/games", new { wordLength = 4 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4, body.GetProperty("wordLength").GetInt32());
        Assert.Equal(6, body.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(["gameId", "maxAttempts", "wordLength"], body.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task A_game_can_start_without_a_request_body()
    {
        var response = await _client.PostAsync("/api/spordle/games", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(5, body.GetProperty("wordLength").GetInt32());
    }

    [Fact]
    public async Task A_nonsense_word_is_reported_invalid_and_nothing_else()
    {
        var gameId = await StartGameAsync();

        var body = await (await GuessAsync(gameId, "zzzzz")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.Single(body.EnumerateObject());
    }

    [Fact]
    public async Task The_answer_stays_hidden_until_the_sixth_miss()
    {
        var gameId = await StartGameAsync();

        for (var i = 0; i < WrongGuesses.Length; i++)
        {
            var body = await (await GuessAsync(gameId, WrongGuesses[i])).Content.ReadFromJsonAsync<JsonElement>();
            var last = i == WrongGuesses.Length - 1;

            Assert.True(body.GetProperty("valid").GetBoolean());
            Assert.Equal(5, body.GetProperty("score").GetArrayLength());
            Assert.All(body.GetProperty("score").EnumerateArray(), s => Assert.Contains(s.GetString(), new[] { "correct", "present", "absent" }));
            Assert.False(body.GetProperty("won").GetBoolean());
            Assert.Equal(last, body.GetProperty("complete").GetBoolean());
            Assert.Equal(5 - i, body.GetProperty("attemptsRemaining").GetInt32());
            Assert.Equal(last, body.TryGetProperty("answer", out var answer));
            if (last) Assert.Equal(5, answer.GetString()!.Length);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await GuessAsync(gameId, "about")).StatusCode);
    }

    [Fact]
    public async Task Hints_return_a_category_or_one_letter()
    {
        var gameId = await StartGameAsync(6);

        var category = await (await HintAsync(gameId, "category")).Content.ReadFromJsonAsync<JsonElement>();
        var letter = await (await HintAsync(gameId, "letter")).Content.ReadFromJsonAsync<JsonElement>();
        var letterAgain = await (await HintAsync(gameId, "letter")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(category.GetProperty("category").GetString()));
        Assert.Single(category.EnumerateObject());
        Assert.InRange(letter.GetProperty("position").GetInt32(), 1, 6);
        Assert.Matches("^[a-z]$", letter.GetProperty("letter").GetString()!);
        Assert.Equal(letter.GetRawText(), letterAgain.GetRawText());
    }

    [Fact]
    public async Task Bad_requests_get_an_error_message()
    {
        var gameId = await StartGameAsync();

        var malformed = await GuessAsync(gameId, "ab1cd");
        var wrongLength = await GuessAsync(gameId, "dunk");
        var unknownHint = await HintAsync(gameId, "answer");
        var unknownGame = await GuessAsync("no-such-game", "about");

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongLength.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownHint.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownGame.StatusCode);
        Assert.Equal("Guess must be 5 letters", (await wrongLength.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal("Game not found or expired", (await unknownGame.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_round_survives_host_restart_including_hints_and_retry_receipts()
    {
        string gameId, firstResponse, letter;
        var requestId = Guid.NewGuid();
        using (var firstHost = factory.WithWebHostBuilder(_ => { }))
        {
            using var client = firstHost.CreateClient();
            var started = await client.PostAsJsonAsync("/api/spordle/games", new { wordLength = 5 });
            gameId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gameId").GetString()!;
            var guess = await client.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word = "about", requestId });
            guess.EnsureSuccessStatusCode();
            firstResponse = await guess.Content.ReadAsStringAsync();
            var hint = await client.PostAsJsonAsync($"/api/spordle/games/{gameId}/hint", new { type = "letter" });
            hint.EnsureSuccessStatusCode();
            letter = await hint.Content.ReadAsStringAsync();
        }

        // A fresh host and service provider reading only the shared database.
        using var restarted = factory.WithWebHostBuilder(_ => { });
        using var resumed = restarted.CreateClient();
        var replay = await resumed.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word = "about", requestId });
        replay.EnsureSuccessStatusCode();
        Assert.Equal(firstResponse, await replay.Content.ReadAsStringAsync());
        var hintAgain = await resumed.PostAsJsonAsync($"/api/spordle/games/{gameId}/hint", new { type = "letter" });
        hintAgain.EnsureSuccessStatusCode();
        Assert.Equal(letter, await hintAgain.Content.ReadAsStringAsync());
        for (var i = 1; i < WrongGuesses.Length; i++)
        {
            var response = await resumed.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word = WrongGuesses[i] });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(5 - i, body.GetProperty("attemptsRemaining").GetInt32());
            Assert.Equal(i == 5, body.TryGetProperty("answer", out _));
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await resumed.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word = "other" })).StatusCode);
    }

    [Fact]
    public async Task Parallel_instances_do_not_lose_guesses_or_double_spend_a_retry()
    {
        using var hostA = factory.WithWebHostBuilder(_ => { });
        using var hostB = factory.WithWebHostBuilder(_ => { });
        using var a = hostA.CreateClient();
        using var b = hostB.CreateClient();
        var gameId = await StartGameAsync();
        var uri = $"/api/spordle/games/{gameId}/guesses";
        var requestId = Guid.NewGuid();
        var duplicate = await Task.WhenAll(
            a.PostAsJsonAsync(uri, new { word = "about", requestId }),
            b.PostAsJsonAsync(uri, new { word = "about", requestId }));
        foreach (var response in duplicate)
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal(5, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attemptsRemaining").GetInt32());
        }
        var separate = await Task.WhenAll(
            a.PostAsJsonAsync(uri, new { word = "other", requestId = Guid.NewGuid() }),
            b.PostAsJsonAsync(uri, new { word = "which", requestId = Guid.NewGuid() }));
        var remaining = new List<int>();
        foreach (var response in separate)
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            remaining.Add(body.GetProperty("attemptsRemaining").GetInt32());
            Assert.False(body.TryGetProperty("answer", out _));
        }
        Assert.Equal([3, 4], remaining.Order());

        var hints = await Task.WhenAll(
            a.PostAsJsonAsync($"/api/spordle/games/{gameId}/hint", new { type = "letter" }),
            b.PostAsJsonAsync($"/api/spordle/games/{gameId}/hint", new { type = "letter" }));
        Assert.All(hints, h => h.EnsureSuccessStatusCode());
        Assert.Equal(await hints[0].Content.ReadAsStringAsync(), await hints[1].Content.ReadAsStringAsync());
        var last = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? a : b).PostAsJsonAsync(uri, new { word = "there", requestId = Guid.NewGuid() })));
        Assert.Equal(3, last.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, last.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var session = await db.SpordleSessions.SingleAsync(s => s.Id == Guid.Parse(gameId));
        Assert.Equal(6, session.Attempts);
        Assert.True(session.Complete);
    }

    [Fact]
    public async Task Expired_database_rounds_return_404_and_are_purged_when_starting_a_round()
    {
        var gameId = await StartGameAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
        var session = await db.SpordleSessions.SingleAsync(s => s.Id == Guid.Parse(gameId));
        session.ExpiresAtUtc = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await GuessAsync(gameId, "about")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await HintAsync(gameId, "letter")).StatusCode);
        await StartGameAsync();
        Assert.False(await db.SpordleSessions.AnyAsync(s => s.Id == Guid.Parse(gameId)));
    }

    [Fact]
    public async Task Guessing_too_fast_is_rate_limited()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Spordle:GuessesPerMinute", "2"));
        var client = limited.CreateClient();
        var start = await client.PostAsJsonAsync("/api/spordle/games", new { wordLength = 5 });
        var gameId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gameId").GetString();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync($"/api/spordle/games/{gameId}/guesses", new { word = "zzzzz" })).StatusCode);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}

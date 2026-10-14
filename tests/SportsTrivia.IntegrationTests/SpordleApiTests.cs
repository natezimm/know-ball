using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace SportsTrivia.IntegrationTests;

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

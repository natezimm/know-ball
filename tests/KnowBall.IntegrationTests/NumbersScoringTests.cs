using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowBall.Domain;
using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

[Collection(ApiCollection.Name)]
public class NumbersScoringTests(ApiFactory factory)
{
    private (HttpClient Client, string Owner) Client()
    {
        var owner = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Anon-Id", owner);
        return (client, owner);
    }

    private async Task<string> CorrectOption(Guid id, int position)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<TriviaDbContext>().NumbersPlays.AsNoTracking().SingleAsync(x => x.Id == id);
        return JsonSerializer.Deserialize<JsonElement[]>(row.SecretJson)![position].GetProperty("correctOptionId").GetString()!;
    }

    private static object Move(JsonElement state, string optionId, int? confidence, Guid? requestId = null) => new {
        questionId = state.GetProperty("current").GetProperty("id").GetString(), optionId,
        requestId = requestId ?? Guid.NewGuid(), expectedRevision = state.GetProperty("revision").GetInt32(), confidence };

    [Fact]
    public async Task Points_follow_the_confidence_bet_and_a_retry_cannot_change_it()
    {
        var (client, _) = Client();
        using var _client = client;
        var started = await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",
            new { leagues = new[] { "nba" }, topics = new[] { "awards" }, requestId = Guid.NewGuid(), allowRepeats = true });
        started.EnsureSuccessStatusCode();
        var state = await started.Content.ReadFromJsonAsync<JsonElement>();
        var id = state.GetProperty("id").GetGuid();
        Assert.Equal(0, state.GetProperty("points").GetInt32());
        Assert.Equal(15, state.GetProperty("maxPoints").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("dailyStreak").ValueKind);

        var tooBold = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", Move(state, await CorrectOption(id, 0), 4));
        Assert.Equal(HttpStatusCode.BadRequest, tooBold.StatusCode);

        var requestId = Guid.NewGuid();
        var answer = Move(state, await CorrectOption(id, 0), 3, requestId);
        var response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", answer);
        response.EnsureSuccessStatusCode();
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, answered.GetProperty("points").GetInt32());
        Assert.Equal(3, answered.GetProperty("review")[0].GetProperty("confidence").GetInt32());
        // The same request again is the original result; the same request with another bet is a conflict.
        (await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", answer)).EnsureSuccessStatusCode();
        var changed = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", Move(state, await CorrectOption(id, 0), 1, requestId));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/continue", new {
            questionId = state.GetProperty("current").GetProperty("id").GetString(), requestId = Guid.NewGuid(),
            expectedRevision = answered.GetProperty("revision").GetInt32() });
        state = await response.Content.ReadFromJsonAsync<JsonElement>();
        var right = await CorrectOption(id, 1);
        var wrong = state.GetProperty("current").GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()!).First(o => o != right);
        response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", Move(state, wrong, null));
        response.EnsureSuccessStatusCode();
        answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, answered.GetProperty("points").GetInt32());
        Assert.Equal(2, answered.GetProperty("review")[1].GetProperty("confidence").GetInt32()); // the default bet

        // Answers saved before confidence existed have no field and count as the default.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var row = await db.NumbersPlays.SingleAsync(x => x.Id == id);
            var answers = JsonNode.Parse(row.AnswersJson)!.AsArray();
            foreach (var a in answers) a!.AsObject().Remove("Confidence");
            row.AnswersJson = answers.ToJsonString();
            await db.SaveChangesAsync();
        }
        var restored = await client.GetFromJsonAsync<JsonElement>($"/api/numbers-dont-lie/quizzes/{id}");
        Assert.Equal(2, restored.GetProperty("points").GetInt32());
        Assert.All(restored.GetProperty("review").EnumerateArray(), a => Assert.Equal(2, a.GetProperty("confidence").GetInt32()));
    }

    [Fact]
    public async Task Daily_streak_counts_consecutive_finished_days_up_to_today_or_yesterday()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
        var (finishedToday, ownerA) = Client();
        var (notYetToday, ownerB) = Client();
        using var a = finishedToday; using var b = notYetToday;
        NumbersPlay Finished(string owner, int daysAgo) => new() { Id = Guid.NewGuid(), Owner = owner,
            StartKey = today.AddDays(-daysAgo).ToString("yyyy-MM-dd"), Date = today.AddDays(-daysAgo), Position = 5 };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            // Today, 1 and 2 days ago, then a gap, then 4 days ago.
            db.NumbersPlays.AddRange(Finished(ownerA, 0), Finished(ownerA, 1), Finished(ownerA, 2), Finished(ownerA, 4));
            // Yesterday and the day before, plus an unfinished round 3 days ago that must not count.
            var unfinished = Finished(ownerB, 3);
            unfinished.Position = 2;
            db.NumbersPlays.AddRange(Finished(ownerB, 1), Finished(ownerB, 2), unfinished);
            await db.SaveChangesAsync();
        }
        Assert.Equal(3, (await a.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily")).GetProperty("dailyStreak").GetInt32());
        var started = await b.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily");
        Assert.False(started.GetProperty("complete").GetBoolean());
        Assert.Equal(2, started.GetProperty("dailyStreak").GetInt32());
    }
}

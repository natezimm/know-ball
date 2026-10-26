using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

[Collection(ApiCollection.Name)]
public class NumbersGameplayTests(ApiFactory factory)
{
    [Theory]
    [InlineData("nba")]
    [InlineData("nfl")]
    [InlineData("mlb")]
    [InlineData("nhl")]
    public async Task Season_questions_hide_catalogue_ids_and_answers_until_submission(string league)
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",
            new { leagues = new[] { league }, topics = new[] { "seasons" }, requestId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<JsonElement>();
        var question = state.GetProperty("current");
        Assert.True(Guid.TryParseExact(question.GetProperty("id").GetString(), "N", out _));
        Assert.Equal(JsonValueKind.Null, question.GetProperty("season").ValueKind);
        Assert.DoesNotContain("factFamily", state.GetRawText());
        Assert.DoesNotContain("correctOptionId", state.GetRawText());
        Assert.DoesNotContain("explanation", state.GetRawText());
        var id = state.GetProperty("id").GetGuid();
        var restored = await client.GetFromJsonAsync<JsonElement>($"/api/numbers-dont-lie/quizzes/{id}");
        Assert.Equal(question.GetRawText(), restored.GetProperty("current").GetRawText());
        var answer = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers", new {
            questionId = question.GetProperty("id").GetString(),
            optionId = question.GetProperty("options")[0].GetProperty("id").GetString(),
            requestId = Guid.NewGuid(), expectedRevision = 0
        });
        answer.EnsureSuccessStatusCode();
        Assert.Single((await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("review").EnumerateArray());
    }

    private HttpClient Client() { var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Anon-Id",Guid.NewGuid().ToString()); return client; }

    [Fact]
    public async Task Stats_practice_scores_closest_answers_after_shuffling_and_hides_supporting_values()
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",
            new { leagues = new[] { "nba" }, topics = new[] { "stats" }, requestId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = state.GetProperty("id").GetGuid();
        JsonElement[] snapshots;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
            var row = await db.NumbersPlays.AsNoTracking().SingleAsync(x => x.Id == id);
            snapshots = JsonSerializer.Deserialize<JsonElement[]>(row.SecretJson)!;
        }
        Assert.Contains(snapshots, q => q.GetProperty("template").GetString() == "closest-value");
        foreach (var snapshot in snapshots)
        {
            var question = state.GetProperty("current");
            Assert.DoesNotContain("\"value\"", question.GetRawText());
            Assert.DoesNotContain("targetValue", question.GetRawText());
            Assert.DoesNotContain("correctOptionId", question.GetRawText());
            var answer = snapshot.GetProperty("correctOptionId").GetString();
            if (snapshot.GetProperty("template").GetString() == "closest-value")
            {
                var target = snapshot.GetProperty("targetValue").GetDecimal();
                answer = snapshot.GetProperty("options").EnumerateArray()
                    .OrderBy(o => Math.Abs(o.GetProperty("value").GetDecimal() - target))
                    .First().GetProperty("id").GetString();
            }
            var questionId = question.GetProperty("id").GetString();
            response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers",
                new { questionId, optionId = answer, requestId = Guid.NewGuid(), expectedRevision = state.GetProperty("revision").GetInt32() });
            response.EnsureSuccessStatusCode();
            state = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(state.GetProperty("review").EnumerateArray().Last().GetProperty("correct").GetBoolean());
            response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/continue",
                new { questionId, requestId = Guid.NewGuid(), expectedRevision = state.GetProperty("revision").GetInt32() });
            response.EnsureSuccessStatusCode();
            state = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Assert.True(state.GetProperty("complete").GetBoolean());
        Assert.Equal(5, state.GetProperty("correctCount").GetInt32());
    }

    [Fact]
    public async Task Daily_is_shared_private_and_recovers_feedback_and_idempotent_final_answer()
    {
        using var client = Client(); using var other = Client();
        var state = await client.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily");
        var second = await other.GetFromJsonAsync<JsonElement>("/api/numbers-dont-lie/daily");
        Assert.Equal(state.GetProperty("current").GetRawText(), second.GetProperty("current").GetRawText());
        Assert.DoesNotContain("correctOptionId",state.GetRawText());
        var id = state.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/numbers-dont-lie/quizzes/{id}")).StatusCode);
        object? final = null;
        for(var i=0;i<5;i++)
        {
            string answer;
            await using(var scope = factory.Services.CreateAsyncScope()) {
                var db = scope.ServiceProvider.GetRequiredService<TriviaDbContext>();
                var row = await db.NumbersPlays.AsNoTracking().SingleAsync(x=>x.Id==id);
                answer = JsonSerializer.Deserialize<JsonElement[]>(row.SecretJson)![i].GetProperty("correctOptionId").GetString()!;
            }
            var question = state.GetProperty("current").GetProperty("id").GetString();
            var move = new { questionId=question, optionId=answer, requestId=Guid.NewGuid(), expectedRevision=state.GetProperty("revision").GetInt32() };
            var response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers",move);
            response.EnsureSuccessStatusCode();
            state = await response.Content.ReadFromJsonAsync<JsonElement>();
            var retry = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers",move);
            retry.EnsureSuccessStatusCode();
            Assert.Equal(i+1,(await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answeredCount").GetInt32());
            var restored = await client.GetFromJsonAsync<JsonElement>($"/api/numbers-dont-lie/quizzes/{id}");
            Assert.Equal(i+1,restored.GetProperty("review").GetArrayLength());
            var next = new { questionId=question, requestId=Guid.NewGuid(), expectedRevision=state.GetProperty("revision").GetInt32() };
            response = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/continue",next);
            response.EnsureSuccessStatusCode(); state = await response.Content.ReadFromJsonAsync<JsonElement>();
            if(i==4) final=move;
        }
        Assert.True(state.GetProperty("complete").GetBoolean());
        Assert.Equal(5,state.GetProperty("correctCount").GetInt32());
        var replay = await client.PostAsJsonAsync($"/api/numbers-dont-lie/quizzes/{id}/answers",final);
        replay.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Practice_start_retries_conflict_and_ownership_are_enforced()
    {
        using var client = Client(); using var other = Client();
        var requestId=Guid.NewGuid();
        var request=new {leagues=new[]{"nba"},topics=new[]{"stats","awards","teams","seasons"},requestId};
        var first=await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",request);
        first.EnsureSuccessStatusCode();
        var state=await first.Content.ReadFromJsonAsync<JsonElement>();
        var retry=await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",request);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(state.GetProperty("id").GetGuid(),(await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var conflict=await client.PostAsJsonAsync("/api/numbers-dont-lie/practice",new {leagues=new[]{"nfl"},request.topics,requestId});
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api/numbers-dont-lie/quizzes/"+state.GetProperty("id").GetString())).StatusCode);
        Assert.Equal("nba",state.GetProperty("current").GetProperty("league").GetString());
        Assert.DoesNotContain("correctOptionId",state.GetRawText());
    }
}

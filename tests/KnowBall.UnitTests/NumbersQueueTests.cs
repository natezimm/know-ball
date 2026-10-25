using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.UnitTests;

public class NumbersQueueTests
{
    [Fact]
    public async Task Queue_is_persistent_idempotent_and_preserves_existing_snapshots()
    {
        using var fixture = new TestDb();
        using var pool = QuizScheduleTests.Pool();
        var start = new DateOnly(2026, 11, 1);
        string id, first;
        await using (var db = fixture.NewContext())
        {
            id = (await new NumbersContentStore(db, TimeProvider.System).ImportAsync(pool.RootElement.GetRawText())).Id;
            var queue = new NumbersQueue(db, TimeProvider.System);
            Assert.Equal(30, await queue.FillAsync(id, start, start));
            first = (await db.NumbersQuizzes.SingleAsync(q => q.Date == start)).QuestionsJson;
            Assert.Equal(0, await queue.FillAsync(id, start, start));
        }
        await using (var db = fixture.NewContext())
        {
            Assert.Equal(1, await new NumbersQueue(db, TimeProvider.System).FillAsync(id, start.AddDays(1), start));
            Assert.Equal(first, (await db.NumbersQuizzes.SingleAsync(q => q.Date == start)).QuestionsJson);
            Assert.Equal(31, await db.NumbersQuizzes.CountAsync());
        }
    }

    [Fact]
    public async Task Shortage_after_one_day_rolls_back_entire_requested_queue()
    {
        using var fixture = new TestDb();
        using var pool = QuizScheduleTests.Pool();
        var root = System.Text.Json.Nodes.JsonNode.Parse(pool.RootElement.GetRawText())!;
        var questions = root["questions"]!.AsArray();
        // Keep only a single day's worth, retaining a valid mixed difficulty/league set.
        var day = KnowBall.Application.Numbers.QuizSchedule.Daily(pool.RootElement, new(2026,11,1), new(2026,11,1), []);
        questions.Clear();
        foreach (var q in day.Questions) questions.Add(System.Text.Json.Nodes.JsonNode.Parse(q.GetRawText()));
        await using var db = fixture.NewContext();
        var catalogue = await new NumbersContentStore(db, TimeProvider.System).ImportAsync(root.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new NumbersQueue(db, TimeProvider.System).FillAsync(catalogue.Id, new(2026,11,1), new(2026,11,1)));
        Assert.Equal(0, await db.NumbersQuizzes.CountAsync());
    }
}

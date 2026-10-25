using System.Text.Json;
using KnowBall.Application.Numbers;

namespace KnowBall.UnitTests;

public class QuizScheduleTests
{
    private static JsonDocument Pool()
    {
        var root = QuizCatalogueTests.Catalogue();
        var list = root["questions"]!.AsArray();
        var template = list[0]!.DeepClone();
        list.Clear();
        foreach (var league in QuizCatalogue.Leagues)
        foreach (var level in QuizCatalogue.Difficulties)
        for (var i = 0; i < 80; i++)
        {
            var q = template.DeepClone();
            q["id"] = $"{league}-{level}-{i}";
            q["factFamily"] = $"{league}-{level}-{i}";
            q["league"] = league; q["difficulty"] = level;
            list.Add(q);
        }
        return QuizCatalogue.Read(root.ToJsonString());
    }

    [Fact]
    public void Schedule_rotates_bonus_and_enforces_difficulty_and_thirty_day_exclusion()
    {
        using var pool = Pool();
        var start = new DateOnly(2026, 10, 25);
        var history = new List<ScheduledQuiz>();
        for (var i = 0; i < 35; i++)
        {
            var round = QuizSchedule.Daily(pool.RootElement, start.AddDays(i), start, history);
            Assert.Equal(2, round.Questions.Count(q => q.GetProperty("league").GetString() == QuizCatalogue.Leagues[i % 4]));
            Assert.Equal(new[] {2,2,1}, QuizCatalogue.Difficulties.Select(d => round.Questions.Count(q => q.GetProperty("difficulty").GetString() == d)).ToArray());
            var recent = history.Where(q => round.Date.DayNumber - q.Date.DayNumber < 30).SelectMany(q => q.Questions).Select(q => q.GetProperty("factFamily").GetString()).ToHashSet();
            Assert.All(round.Questions, q => Assert.DoesNotContain(q.GetProperty("factFamily").GetString(), recent));
            history.Add(round);
            Assert.Same(round, QuizSchedule.Daily(pool.RootElement, round.Date, start, history));
        }
        Assert.Null(QuizSchedule.Simulate(pool.RootElement, start).Shortage);
    }

    [Fact]
    public void Shortages_are_reported_without_partial_days()
    {
        using var pool = QuizCatalogue.Read(QuizCatalogueTests.Catalogue().ToJsonString());
        var result = QuizSchedule.Simulate(pool.RootElement, new(2026, 10, 25));
        Assert.Equal(0, result.CompletedDays);
        Assert.Contains("nba/approachable=0", result.Shortage);
    }
}

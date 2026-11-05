using KnowBall.Application.Numbers;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.UnitTests;

public class NumbersCatalogueTests
{
    [Fact]
    public void Embedded_bank_supports_every_league_and_topic_combination()
    {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-questions.json")!;
        using var reader = new StreamReader(stream);
        using var catalogue = QuizCatalogue.Read(reader.ReadToEnd());
        var questions = catalogue.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(262, questions.Length);
        // The bank links record variants and playoff plays to related facts.
        Assert.Equal(262 - 19, questions.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
        Assert.All(questions, q => Assert.Contains(q.GetProperty("topic").GetString(), NumbersPlayService.Topics));
        foreach (var league in QuizCatalogue.Leagues)
            Assert.Equal(10, questions.Count(q => q.GetProperty("league").GetString() == league &&
                q.GetProperty("topic").GetString() == "playoff-moments"));

        // All nonempty live filter combinations must produce a full round with no year bounds.
        for (var leaguesMask = 1; leaguesMask < 16; leaguesMask++)
        for (var topicsMask = 1; topicsMask < 1 << NumbersPlayService.Topics.Length; topicsMask++)
        {
            var leagues = QuizCatalogue.Leagues.Where((_, i) => (leaguesMask & (1 << i)) != 0).ToArray();
            var topics = NumbersPlayService.Topics.Where((_, i) => (topicsMask & (1 << i)) != 0).ToArray();
            var filters = new QuizFilters(leagues, topics);
            var round = QuizSelection.Select(catalogue.RootElement, filters, "catalogue-coverage", requireApproved: false, preferVariety: true);
            Assert.Equal(5, round.Length);
            Assert.Equal(5, round.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
            Assert.All(round, q => {
                Assert.Contains(q.GetProperty("league").GetString(), leagues);
                Assert.Contains(q.GetProperty("topic").GetString(), topics);
            });
            Assert.Equal(Math.Min(topics.Length, 5), round.Select(q => q.GetProperty("topic").GetString()).Distinct().Count());
            var counts = leagues.Select(l => round.Count(q => q.GetProperty("league").GetString() == l)).ToArray();
            Assert.True(counts.Max() - counts.Min() <= 1);
            Assert.Equal(round.Select(q => q.GetRawText()),
                QuizSelection.Select(catalogue.RootElement, filters, "catalogue-coverage", requireApproved: false, preferVariety: true).Select(q => q.GetRawText()));
        }
    }

    [Fact]
    public void Playoff_moments_respect_year_bounds_and_reserved_facts()
    {
        using var catalogue = Bank();
        var reserved = new HashSet<string> { "nba-lillard-wave" };
        var round = QuizSelection.Select(catalogue.RootElement, new(["nba"], ["playoff-moments"], 1979, 2020),
            "filtered", reserved, requireApproved: false, preferVariety: true);
        Assert.Equal(5, round.Length);
        Assert.All(round, q => {
            Assert.InRange(QuizCatalogue.SeasonYear(q)!.Value, 1979, 2020);
            Assert.DoesNotContain(q.GetProperty("factFamily").GetString()!, reserved);
        });
        Assert.Throws<InvalidOperationException>(() => QuizSelection.Select(catalogue.RootElement,
            new(["nba"], ["playoff-moments"], 2018, 2018), "too-narrow", requireApproved: false, preferVariety: true));
    }

    [Fact]
    public void Daily_rounds_draw_only_daily_eligible_questions_and_skip_ones_past_recheck()
    {
        using var catalogue = Bank();
        var all = new QuizFilters(QuizCatalogue.Leagues, NumbersPlayService.Topics);
        var asOf = new DateOnly(2026, 11, 3);
        for (var seed = 0; seed < 50; seed++)
        {
            var round = QuizSelection.Select(catalogue.RootElement, all, $"daily-{seed}", requireApproved: false,
                preferVariety: true, dailyOnly: true, asOf: asOf);
            Assert.All(round, q => Assert.False(q.TryGetProperty("dailyEligible", out var daily) && !daily.GetBoolean()));
        }
        var questions = catalogue.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        var lastSeason = questions.Where(q => q.TryGetProperty("recheckBy", out _)).ToArray();
        Assert.Equal(5, lastSeason.Length);
        Assert.All(lastSeason, q => Assert.True(QuizCatalogue.Eligible(q, all, dailyOnly: true, asOf: asOf)));
        Assert.All(lastSeason, q => Assert.False(QuizCatalogue.Eligible(q, all, dailyOnly: true, asOf: new DateOnly(2027, 11, 6))));
    }

    [Fact]
    public void Timeless_topics_drop_out_of_year_bounded_practice()
    {
        using var catalogue = Bank();
        var nickname = catalogue.RootElement.GetProperty("questions").EnumerateArray()
            .First(q => q.GetProperty("topic").GetString() == "nicknames");
        Assert.True(QuizCatalogue.Eligible(nickname, new(QuizCatalogue.Leagues, ["nicknames"])));
        Assert.False(QuizCatalogue.Eligible(nickname, new(QuizCatalogue.Leagues, ["nicknames"], 1980)));
        Assert.Throws<InvalidOperationException>(() => QuizSelection.Select(catalogue.RootElement,
            new(["nba"], ["nicknames"], 1990, 2000), "bounded", requireApproved: false, preferVariety: true));
    }

    private static System.Text.Json.JsonDocument Bank()
    {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-questions.json")!;
        using var reader = new StreamReader(stream);
        return QuizCatalogue.Read(reader.ReadToEnd());
    }
}

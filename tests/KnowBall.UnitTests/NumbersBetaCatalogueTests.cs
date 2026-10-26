using KnowBall.Application.Numbers;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.UnitTests;

public class NumbersBetaCatalogueTests
{
    [Fact]
    public void Embedded_bank_supports_every_league_and_topic_combination()
    {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-beta.json")!;
        using var reader = new StreamReader(stream);
        using var catalogue = QuizCatalogue.Read(reader.ReadToEnd());
        var questions = catalogue.RootElement.GetProperty("questions").EnumerateArray().ToArray();
        Assert.Equal(88, questions.Length);
        Assert.Equal(88, questions.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
        foreach (var league in QuizCatalogue.Leagues)
            Assert.Equal(22, questions.Count(q => q.GetProperty("league").GetString() == league));

        // All 225 nonempty filter combinations must produce a full round with no year bounds.
        for (var leaguesMask = 1; leaguesMask < 16; leaguesMask++)
        for (var topicsMask = 1; topicsMask < 16; topicsMask++)
        {
            var leagues = QuizCatalogue.Leagues.Where((_, i) => (leaguesMask & (1 << i)) != 0).ToArray();
            var topics = QuizCatalogue.Topics.Where((_, i) => (topicsMask & (1 << i)) != 0).ToArray();
            var filters = new QuizFilters(leagues, topics);
            var round = QuizSelection.Select(catalogue.RootElement, filters, "beta-coverage", requireApproved: false, preferVariety: true);
            Assert.Equal(5, round.Length);
            Assert.Equal(5, round.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
            Assert.All(round, q => {
                Assert.Contains(q.GetProperty("league").GetString(), leagues);
                Assert.Contains(q.GetProperty("topic").GetString(), topics);
            });
            Assert.Equal(topics.Length, round.Select(q => q.GetProperty("topic").GetString()).Distinct().Count());
            var counts = leagues.Select(l => round.Count(q => q.GetProperty("league").GetString() == l)).ToArray();
            Assert.True(counts.Max() - counts.Min() <= 1);
            Assert.Equal(round.Select(q => q.GetRawText()),
                QuizSelection.Select(catalogue.RootElement, filters, "beta-coverage", requireApproved: false, preferVariety: true).Select(q => q.GetRawText()));
        }
    }

    [Fact]
    public void Stats_rounds_mix_challenge_styles_across_seeds()
    {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-beta.json")!;
        using var reader = new StreamReader(stream);
        using var catalogue = QuizCatalogue.Read(reader.ReadToEnd());
        foreach (var league in QuizCatalogue.Leagues)
        for (var seed = 0; seed < 20; seed++)
        {
            var round = QuizSelection.Select(catalogue.RootElement, new([league], ["stats"]),
                $"styles-{seed}", requireApproved: false, preferVariety: true);
            Assert.Equal(3, round.Select(q => q.GetProperty("template").GetString()).Distinct().Count());
        }
    }

    [Fact]
    public void Variety_preserves_year_bounds_and_reserved_facts()
    {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-beta.json")!;
        using var reader = new StreamReader(stream);
        using var catalogue = QuizCatalogue.Read(reader.ReadToEnd());
        var reserved = new HashSet<string> { "nba-2024-regular-points" };
        var round = QuizSelection.Select(catalogue.RootElement, new(["nba"], ["stats"], 2023, 2023),
            "filtered", reserved, requireApproved: false, preferVariety: true);
        Assert.Equal(5, round.Length);
        Assert.All(round, q => {
            Assert.Equal("2023–24", q.GetProperty("season").GetString());
            Assert.DoesNotContain(q.GetProperty("factFamily").GetString()!, reserved);
        });
        Assert.Throws<InvalidOperationException>(() => QuizSelection.Select(catalogue.RootElement,
            new(["nba"], ["stats"], 1990, 1991), "empty", requireApproved: false, preferVariety: true));
    }
}

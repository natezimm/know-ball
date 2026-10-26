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
            var round = QuizSelection.Select(catalogue.RootElement, new(leagues, topics), "beta-coverage", requireApproved: false);
            Assert.Equal(5, round.Length);
            Assert.Equal(5, round.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
            Assert.All(round, q => {
                Assert.Contains(q.GetProperty("league").GetString(), leagues);
                Assert.Contains(q.GetProperty("topic").GetString(), topics);
            });
        }
    }
}

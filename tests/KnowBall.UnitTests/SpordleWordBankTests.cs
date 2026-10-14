using KnowBall.Application.Games.Spordle;

namespace KnowBall.UnitTests;

public class SpordleWordBankTests
{
    private static readonly SpordleWordBank Bank = SpordleWordBank.LoadEmbedded();

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Shipped_answers_are_the_right_length_and_plentiful(int length)
    {
        var answers = Bank.Answers(length);

        Assert.True(answers.Count >= 100, $"only {answers.Count} answers of length {length}");
        Assert.All(answers, word =>
        {
            Assert.Equal(length, word.Length);
            Assert.Matches("^[a-z]+$", word);
        });
        Assert.Equal(answers.Count, answers.Distinct().Count());
    }

    [Fact]
    public void Every_shipped_answer_has_a_category_and_is_an_allowed_guess()
    {
        foreach (var word in SpordleWordBank.SupportedLengths.SelectMany(Bank.Answers))
        {
            Assert.False(string.IsNullOrWhiteSpace(Bank.CategoryOf(word)), word);
            Assert.True(Bank.IsAllowedGuess(word), word);
        }
    }

    [Fact]
    public void Dictionary_words_are_allowed_and_nonsense_is_not()
    {
        Assert.True(Bank.IsAllowedGuess("about"));
        Assert.True(Bank.IsAllowedGuess("niners")); // not in the dictionary, but it is an answer
        Assert.False(Bank.IsAllowedGuess("zzzzz"));
    }

    [Fact]
    public void Category_comes_from_the_group_the_word_is_listed_under()
    {
        Assert.Equal("Golf", Bank.CategoryOf("birdie"));
        Assert.Equal("NFL team", Bank.CategoryOf("niners"));
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(6, 6)]
    [InlineData(null, 5)]
    [InlineData(3, 5)]
    [InlineData(7, 5)]
    public void Unsupported_lengths_fall_back_to_five(int? requested, int expected)
    {
        Assert.Equal(expected, SpordleWordBank.NormalizeLength(requested));
    }

    [Fact]
    public void A_word_listed_twice_or_of_the_wrong_shape_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SpordleWordBank(
            new Dictionary<string, string> { ["Golf"] = "putt", ["Baseball"] = "putt" }, []));
        Assert.Throws<ArgumentException>(() => new SpordleWordBank(
            new Dictionary<string, string> { ["Golf"] = "tee" }, []));
        Assert.Throws<ArgumentException>(() => new SpordleWordBank(
            new Dictionary<string, string> { ["Golf"] = "Putt" }, []));
    }
}

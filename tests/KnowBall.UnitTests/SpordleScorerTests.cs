using KnowBall.Application.Games.Spordle;
using static KnowBall.Application.Games.Spordle.LetterStatus;

namespace KnowBall.UnitTests;

public class SpordleScorerTests
{
    [Fact]
    public void Marks_exact_present_and_absent_letters()
    {
        Assert.Equal([Absent, Absent, Absent, Correct, Absent], SpordleScorer.Score("blitz", "punts"));
        Assert.Equal([Present, Absent, Absent, Absent, Absent], SpordleScorer.Score("blitz", "teams"));
        Assert.Equal([Correct, Correct, Correct, Correct, Correct], SpordleScorer.Score("blitz", "blitz"));
    }

    [Fact]
    public void A_repeated_letter_is_only_present_as_often_as_the_answer_has_it()
    {
        // "allow" has two Ls. One is matched in place, one is elsewhere, so the third L in the guess is absent.
        Assert.Equal([Present, Present, Correct, Absent, Absent], SpordleScorer.Score("allow", "lolly"));
    }

    [Fact]
    public void An_in_place_match_takes_priority_over_an_earlier_copy_of_the_letter()
    {
        // "eagle" has two Es. The guess has three; the two in place are correct and the middle one is absent.
        Assert.Equal([Correct, Absent, Absent, Absent, Correct], SpordleScorer.Score("eagle", "eerie"));
    }

    [Fact]
    public void Rejects_a_guess_of_the_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => SpordleScorer.Score("blitz", "dunk"));
    }
}

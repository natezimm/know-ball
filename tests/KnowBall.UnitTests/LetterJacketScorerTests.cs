using KnowBall.Application.Games.LetterJacket;
using static KnowBall.Application.Games.LetterJacket.LetterStatus;

namespace KnowBall.UnitTests;

public class LetterJacketScorerTests
{
    [Fact]
    public void Marks_exact_present_and_absent_letters()
    {
        Assert.Equal([Absent, Absent, Absent, Correct, Absent], LetterJacketScorer.Score("blitz", "punts"));
        Assert.Equal([Present, Absent, Absent, Absent, Absent], LetterJacketScorer.Score("blitz", "teams"));
        Assert.Equal([Correct, Correct, Correct, Correct, Correct], LetterJacketScorer.Score("blitz", "blitz"));
    }

    [Fact]
    public void A_repeated_letter_is_only_present_as_often_as_the_answer_has_it()
    {
        // "allow" has two Ls. One is matched in place, one is elsewhere, so the third L in the guess is absent.
        Assert.Equal([Present, Present, Correct, Absent, Absent], LetterJacketScorer.Score("allow", "lolly"));
    }

    [Fact]
    public void An_in_place_match_takes_priority_over_an_earlier_copy_of_the_letter()
    {
        // "eagle" has two Es. The guess has three; the two in place are correct and the middle one is absent.
        Assert.Equal([Correct, Absent, Absent, Absent, Correct], LetterJacketScorer.Score("eagle", "eerie"));
    }

    [Fact]
    public void Rejects_a_guess_of_the_wrong_length()
    {
        Assert.Throws<ArgumentException>(() => LetterJacketScorer.Score("blitz", "dunk"));
    }
}

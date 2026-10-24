using KnowBall.Application.Games.ScoutingReport;
using static KnowBall.Application.Games.ScoutingReport.AttributeComparer;

namespace KnowBall.UnitTests;

public class AttributeComparerTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    [Theory]
    [InlineData("BOS", "BOS", Ok)]
    [InlineData("bos", "BOS", Ok)]
    [InlineData("NYK", "BOS", Miss)]
    [InlineData(null, "BOS", Miss)]
    [InlineData(null, null, Miss)] // two free agents do not share a team
    public void Team_conference_and_division_are_right_or_wrong(string? guess, string? answer, string expected)
    {
        var clue = Exact(guess, answer);
        Assert.Equal(expected, clue.State);
        Assert.Null(clue.Arrow);
        Assert.Equal(guess ?? Unknown, clue.Value);
    }

    [Fact]
    public void Team_clue_carries_the_team_name_as_its_label() =>
        Assert.Equal("Celtics", Exact("BOS", "NYK", "Celtics").Label);

    [Theory]
    [InlineData("BOS", "BOS", Ok)]
    [InlineData("MIA", "BOS", Close)] // used to play for the Heat
    [InlineData("mia", "BOS", Close)]
    [InlineData("NYK", "BOS", Miss)]
    [InlineData(null, "BOS", Miss)]
    public void Team_is_close_when_the_answer_used_to_play_there(string? guess, string? answer, string expected) =>
        Assert.Equal(expected, Team(guess, answer, ["MIA", "OKC"]).State);

    [Fact]
    public void Team_without_a_former_team_list_is_right_or_wrong()
    {
        Assert.Equal(Miss, Team("MIA", "BOS", null).State);
        Assert.Equal("https://logos/mia.svg", Team("MIA", "BOS", null, "Heat", "https://logos/mia.svg").Image);
    }

    [Theory]
    [InlineData("G", "G", Ok)]
    [InlineData("G-F", "G-F", Ok)]
    [InlineData("G", "G-F", Close)]
    [InlineData("G-F", "F", Close)]
    [InlineData("F-G", "G-F", Close)] // same two positions, listed in the other order
    [InlineData("F-C", "C-F", Close)]
    [InlineData("PG", "SG", Close)] // both guards
    [InlineData("PF", "F", Close)]
    [InlineData("G", "C", Miss)]
    [InlineData("G", "F-C", Miss)]
    [InlineData("C", null, Miss)]
    [InlineData(null, "C", Miss)]
    public void Position_is_close_when_a_position_is_shared(string? guess, string? answer, string expected) =>
        Assert.Equal(expected, Position(guess, answer).State);

    [Theory]
    [InlineData(80, 80, Ok, null)]
    [InlineData(78, 80, Close, Up)]
    [InlineData(82, 80, Close, Down)]
    [InlineData(77, 80, Miss, Up)]
    [InlineData(83, 80, Miss, Down)]
    public void Height_is_close_within_two_inches_and_points_at_the_answer(int guess, int answer, string state, string? arrow)
    {
        var clue = Height(guess, answer);
        Assert.Equal(state, clue.State);
        Assert.Equal(arrow, clue.Arrow);
    }

    [Fact]
    public void Height_is_shown_in_feet_and_inches()
    {
        Assert.Equal("6′8″", Height(80, 80).Value);
        Assert.Equal("7′0″", Height(84, 80).Value);
        Assert.Equal(Unknown, Height(null, 80).Value);
        Assert.Equal(Miss, Height(null, 80).State);
        Assert.Null(Height(80, null).Arrow);
    }

    [Theory]
    [InlineData("1998-03-03", "1998-08-20", Ok, null)]  // both 28
    [InlineData("1996-03-03", "1998-08-20", Close, Down)] // 30 against 28
    [InlineData("2000-12-25", "1998-08-20", Miss, Up)]    // 25 against 28
    [InlineData("1990-01-01", "1998-08-20", Miss, Down)]
    public void Age_is_close_within_two_years(string guessBorn, string answerBorn, string state, string? arrow)
    {
        var clue = Age(DateOnly.Parse(guessBorn), DateOnly.Parse(answerBorn), Today);
        Assert.Equal(state, clue.State);
        Assert.Equal(arrow, clue.Arrow);
    }

    [Fact]
    public void Age_counts_whole_years_up_to_the_puzzle_day()
    {
        Assert.Equal(27, AgeOn(new DateOnly(1998, 10, 14), Today)); // birthday is tomorrow
        Assert.Equal(28, AgeOn(new DateOnly(1998, 10, 13), Today)); // birthday is today
        Assert.Equal("28", Age(new DateOnly(1998, 10, 13), null, Today).Value);
        Assert.Equal(Unknown, Age(null, new DateOnly(1998, 1, 1), Today).Value);
    }

    [Theory]
    [InlineData("0", "0", Ok, null)]
    [InlineData("2", "0", Close, Down)]
    [InlineData("3", "0", Miss, Down)]
    [InlineData("11", "30", Miss, Up)]
    [InlineData("28", "30", Close, Up)]
    [InlineData("27", "30", Miss, Up)]
    [InlineData("00", "0", Close, null)] // different shirts with the same value
    [InlineData("00", "00", Ok, null)]
    [InlineData("7", null, Miss, null)]
    [InlineData(null, "7", Miss, null)]
    public void Number_is_close_within_two(string? guess, string? answer, string state, string? arrow)
    {
        var clue = Number(guess, answer);
        Assert.Equal(state, clue.State);
        Assert.Equal(arrow, clue.Arrow);
    }

    [Fact]
    public void A_full_comparison_fills_all_seven_clues()
    {
        var tatum = new ScoutingProfile("BOS", "Celtics", null, "East", "Atlantic", "F-G", 80, new DateOnly(1998, 3, 3), "0");
        var brunson = new ScoutingProfile("NYK", "Knicks", "https://logos/nyk.svg", "East", "Atlantic", "G", 74, new DateOnly(1996, 8, 31), "11");

        var f = ScoutingReportGame.Compare(brunson, tatum, Today);

        Assert.Equal((Miss, "NYK", "Knicks", "https://logos/nyk.svg"), (f.Team.State, f.Team.Value, f.Team.Label, f.Team.Image));
        Assert.Equal(Ok, f.Conference.State);
        Assert.Equal(Ok, f.Division.State);
        Assert.Equal(Close, f.Position.State);
        Assert.Equal((Miss, Up, "6′2″"), (f.Height.State, f.Height.Arrow, f.Height.Value));
        Assert.Equal((Close, Down, "30"), (f.Age.State, f.Age.Arrow, f.Age.Value));
        Assert.Equal((Miss, Down), (f.Number.State, f.Number.Arrow));

        Assert.Equal(Close, ScoutingReportGame.Compare(brunson, tatum, Today, ["NYK"]).Team.State);
    }
}

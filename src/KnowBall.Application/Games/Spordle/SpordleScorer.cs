namespace KnowBall.Application.Games.Spordle;

public enum LetterStatus
{
    Absent,
    Present,
    Correct,
}

public static class SpordleScorer
{
    /// <summary>
    /// Scores a guess against the answer, one status per letter. A repeated letter is only
    /// marked present as many times as it is still unmatched in the answer.
    /// </summary>
    public static LetterStatus[] Score(string answer, string guess)
    {
        if (answer.Length != guess.Length)
            throw new ArgumentException("Guess and answer must be the same length.", nameof(guess));

        var score = new LetterStatus[guess.Length];
        var unmatched = new Dictionary<char, int>();

        for (var i = 0; i < guess.Length; i++)
        {
            if (guess[i] == answer[i])
                score[i] = LetterStatus.Correct;
            else
                unmatched[answer[i]] = unmatched.GetValueOrDefault(answer[i]) + 1;
        }

        for (var i = 0; i < guess.Length; i++)
        {
            if (score[i] == LetterStatus.Correct) continue;
            if (unmatched.GetValueOrDefault(guess[i]) == 0) continue;

            score[i] = LetterStatus.Present;
            unmatched[guess[i]]--;
        }

        return score;
    }
}

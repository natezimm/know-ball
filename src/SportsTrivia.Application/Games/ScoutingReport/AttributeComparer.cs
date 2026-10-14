using System.Globalization;

namespace SportsTrivia.Application.Games.ScoutingReport;

/// <summary>
/// One clue on a guess card: what the guessed player has, and how it compares with the answer.
/// </summary>
/// <param name="State">"ok" (same), "close" or "miss".</param>
/// <param name="Arrow">"up" if the answer is higher, "down" if lower; null when the clue is right or cannot be compared.</param>
/// <param name="Label">A longer form of the value for display, such as the team's name.</param>
public sealed record Clue(string Value, string State, string? Arrow = null, string? Label = null);

/// <summary>The rules that colour each clue. Pure functions, so every rule is unit-tested.</summary>
public static class AttributeComparer
{
    public const string Ok = "ok";
    public const string Close = "close";
    public const string Miss = "miss";
    public const string Up = "up";
    public const string Down = "down";
    public const string Unknown = "—";

    public const int HeightWindow = 2;
    public const int AgeWindow = 2;
    public const int NumberWindow = 3;

    /// <summary>Team, conference and division: right or wrong, nothing in between.</summary>
    public static Clue Exact(string? guess, string? answer, string? label = null) =>
        new(guess ?? Unknown, guess is not null && string.Equals(guess, answer, StringComparison.OrdinalIgnoreCase) ? Ok : Miss, Label: label);

    /// <summary>
    /// Identical is right. Sharing a position is close: a G-F is close to a G and to an F, and a
    /// point guard is close to a shooting guard.
    /// </summary>
    public static Clue Position(string? guess, string? answer)
    {
        if (guess is null) return new Clue(Unknown, Miss);
        if (string.Equals(guess, answer, StringComparison.OrdinalIgnoreCase)) return new Clue(guess, Ok);
        return new Clue(guess, answer is not null && Roles(guess).Overlaps(Roles(answer)) ? Close : Miss);
    }

    public static Clue Height(int? guessInches, int? answerInches) =>
        Numeric(guessInches, answerInches, HeightWindow, guessInches is { } h ? FormatHeight(h) : Unknown);

    public static Clue Age(DateOnly? guessBorn, DateOnly? answerBorn, DateOnly asOf)
    {
        int? guess = guessBorn is { } g ? AgeOn(g, asOf) : null;
        int? answer = answerBorn is { } a ? AgeOn(a, asOf) : null;
        return Numeric(guess, answer, AgeWindow, guess?.ToString(CultureInfo.InvariantCulture) ?? Unknown);
    }

    /// <summary>Jersey numbers compare as numbers, but "0" and "00" are different shirts: close, not the same.</summary>
    public static Clue Number(string? guess, string? answer)
    {
        if (guess is null) return new Clue(Unknown, Miss);
        if (guess == answer) return new Clue(guess, Ok);
        if (!int.TryParse(guess, NumberStyles.None, CultureInfo.InvariantCulture, out var g)
            || !int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var a))
            return new Clue(guess, Miss);

        return g == a ? new Clue(guess, Close) : Numeric(g, a, NumberWindow, guess);
    }

    /// <summary>Whole years lived by <paramref name="asOf"/>.</summary>
    public static int AgeOn(DateOnly born, DateOnly asOf)
    {
        var age = asOf.Year - born.Year;
        return asOf < born.AddYears(age) ? age - 1 : age;
    }

    /// <summary>80 → 6′8″.</summary>
    public static string FormatHeight(int inches) => $"{inches / 12}′{inches % 12}″";

    private static Clue Numeric(int? guess, int? answer, int window, string display)
    {
        if (guess is not { } g) return new Clue(display, Miss);
        if (answer is not { } a) return new Clue(display, Miss);
        if (g == a) return new Clue(display, Ok);
        return new Clue(display, Math.Abs(g - a) <= window ? Close : Miss, a > g ? Up : Down);
    }

    /// <summary>"PG" → {G}; "F-C" → {F, C}.</summary>
    private static HashSet<char> Roles(string position)
    {
        var roles = new HashSet<char>();
        foreach (var part in position.ToUpperInvariant().Split('-', '/', ' '))
        {
            if (part.Length > 0) roles.Add(part[^1]);
        }
        return roles;
    }
}

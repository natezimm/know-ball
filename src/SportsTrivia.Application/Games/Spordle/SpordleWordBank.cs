using System.Reflection;
using System.Text.Json;

namespace SportsTrivia.Application.Games.Spordle;

/// <summary>
/// The words Spordle can use: hand-picked sports answers grouped by category, plus a general
/// English dictionary of guesses that are accepted but never chosen as the answer.
/// </summary>
public sealed class SpordleWordBank
{
    public const int DefaultLength = 5;
    public static readonly IReadOnlyList<int> SupportedLengths = [4, 5, 6];

    private readonly Dictionary<int, string[]> _answersByLength;
    private readonly Dictionary<string, string> _categoryByAnswer = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allowedGuesses;

    /// <param name="answersByCategory">Category name to a space-separated list of answers.</param>
    /// <param name="dictionary">Extra words that count as valid guesses.</param>
    public SpordleWordBank(IReadOnlyDictionary<string, string> answersByCategory, IEnumerable<string> dictionary)
    {
        foreach (var (category, words) in answersByCategory)
        {
            foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SupportedLengths.Contains(word.Length) || !word.All(c => c is >= 'a' and <= 'z'))
                    throw new ArgumentException($"'{word}' in '{category}' must be 4 to 6 lower-case letters.");
                if (!_categoryByAnswer.TryAdd(word, category))
                    throw new ArgumentException($"'{word}' is listed under both '{_categoryByAnswer[word]}' and '{category}'.");
            }
        }

        _answersByLength = SupportedLengths.ToDictionary(
            length => length,
            length => _categoryByAnswer.Keys.Where(w => w.Length == length).Order(StringComparer.Ordinal).ToArray());

        _allowedGuesses = new HashSet<string>(dictionary, StringComparer.Ordinal);
        _allowedGuesses.UnionWith(_categoryByAnswer.Keys);
    }

    /// <summary>The lists shipped with the app: sports-words.json and guess-words.txt.</summary>
    public static SpordleWordBank LoadEmbedded()
    {
        using var answers = OpenResource("sports-words.json");
        var byCategory = JsonSerializer.Deserialize<Dictionary<string, string>>(answers)
                         ?? throw new InvalidOperationException("sports-words.json is empty");

        // The 4 to 6 letter words from the MIT-licensed `word-list` package, the same dictionary Nerdle uses.
        using var reader = new StreamReader(OpenResource("guess-words.txt"));
        var dictionary = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SpordleWordBank(byCategory, dictionary);
    }

    public static int NormalizeLength(int? requested) =>
        requested is { } length && SupportedLengths.Contains(length) ? length : DefaultLength;

    public IReadOnlyList<string> Answers(int length) => _answersByLength[NormalizeLength(length)];

    public string CategoryOf(string answer) => _categoryByAnswer[answer];

    /// <summary>True for any answer or dictionary word. Expects lower case.</summary>
    public bool IsAllowedGuess(string word) => _allowedGuesses.Contains(word);

    private static Stream OpenResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        return asm.GetManifestResourceStream(resource)!;
    }
}

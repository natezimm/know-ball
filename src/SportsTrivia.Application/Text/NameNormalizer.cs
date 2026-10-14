using System.Globalization;
using System.Text;

namespace SportsTrivia.Application.Text;

public static class NameNormalizer
{
    /// <summary>
    /// "Nikola Jokić" → "nikola jokic", "Shai Gilgeous-Alexander" → "shai gilgeous alexander",
    /// "De'Aaron Fox" → "deaaron fox". Used for search and for matching guesses.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            var c = ch switch
            {
                'ı' => 'i', // Turkish dotless i (Şengün etc.)
                'ø' or 'Ø' => 'o',
                'đ' or 'Đ' => 'd',
                'ł' or 'Ł' => 'l',
                'ß' => 's',
                _ => ch,
            };

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (c is '\'' or '’' or '.')
            {
                // drop apostrophes and periods inside names: "D'Angelo" → "dangelo", "P.J." → "pj"
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString().TrimEnd();
    }
}

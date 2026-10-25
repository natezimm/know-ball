using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowBall.Application.Numbers;

public sealed record QuizFilters(string[] Leagues, string[] Topics, int? FromYear = null, int? ThroughYear = null);

public static class QuizSelection
{
    /// <summary>Deterministic offline selection; returns cloned private snapshots or fails atomically.</summary>
    public static JsonElement[] Select(JsonElement catalogue, QuizFilters filters, string seed,
        IReadOnlySet<string>? reservedFamilies = null, bool requireApproved = true)
    {
        var errors = QuizCatalogue.Validate(catalogue, requireApproved);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        if (filters.Leagues.Length == 0 || filters.Leagues.Any(l => !QuizCatalogue.Leagues.Contains(l)) ||
            filters.Topics.Length == 0 || filters.Topics.Any(t => !QuizCatalogue.Topics.Contains(t)) ||
            filters.FromYear > filters.ThroughYear)
            throw new ArgumentException("Select supported leagues/topics and an ordered year range.");
        string Rank(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed + ":" + id)));
        var leagues = filters.Leagues.Distinct().OrderBy(Rank, StringComparer.Ordinal).ToArray();
        var pools = catalogue.GetProperty("questions").EnumerateArray().Where(q =>
            filters.Topics.Contains(QuizCatalogue.Text(q, "topic")) &&
            !(reservedFamilies?.Contains(QuizCatalogue.Text(q, "factFamily")) ?? false) &&
            int.TryParse(QuizCatalogue.Text(q, "season").AsSpan(0, Math.Min(4, QuizCatalogue.Text(q, "season").Length)), out var year) &&
            (!filters.FromYear.HasValue || year >= filters.FromYear) && (!filters.ThroughYear.HasValue || year <= filters.ThroughYear))
            .OrderBy(q => Rank(QuizCatalogue.Text(q, "id")), StringComparer.Ordinal).ToArray();
        var selected = new List<JsonElement>();
        var families = new HashSet<string>();
        for (var i = 0; i < 5; i++)
        {
            var league = leagues[i % leagues.Length];
            var candidates = pools.Where(q => QuizCatalogue.Text(q, "league") == league && !families.Contains(QuizCatalogue.Text(q, "factFamily"))).ToArray();
            if (candidates.Length == 0) throw new InvalidOperationException($"Insufficient eligible {league} facts for a balanced five-question round.");
            var choice = candidates[0];
            families.Add(QuizCatalogue.Text(choice, "factFamily"));
            selected.Add(choice.Clone());
        }
        return selected.ToArray();
    }
}

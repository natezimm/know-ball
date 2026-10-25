using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowBall.Application.Numbers;

public sealed record ScheduledQuiz(DateOnly Date, JsonElement[] Questions);
public sealed record SupplyReport(int CompletedDays, string? Shortage, int IndependentFamilies);

/// <summary>Offline scheduling policy. Never publishes or changes an existing snapshot.</summary>
public static class QuizSchedule
{
    public const string Version = "daily-v1";

    public static ScheduledQuiz Daily(JsonElement catalogue, DateOnly date, DateOnly launch,
        IReadOnlyList<ScheduledQuiz> history)
    {
        var errors = QuizCatalogue.Validate(catalogue, true);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        if (date < launch) throw new ArgumentOutOfRangeException(nameof(date));
        var existing = history.SingleOrDefault(q => q.Date == date);
        if (existing is not null) return existing;
        var reserved = history.Where(q => Math.Abs(q.Date.DayNumber - date.DayNumber) < 30)
            .SelectMany(q => q.Questions).Select(q => QuizCatalogue.Text(q, "factFamily")).ToHashSet();
        var bonus = QuizCatalogue.Leagues[(date.DayNumber - launch.DayNumber) % 4];
        var quotas = QuizCatalogue.Leagues.ToDictionary(l => l, l => l == bonus ? 2 : 1);
        string Rank(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Version}:{date:yyyy-MM-dd}:{id}")));
        var pool = catalogue.GetProperty("questions").EnumerateArray()
            .Where(q => !reserved.Contains(QuizCatalogue.Text(q, "factFamily")))
            .OrderBy(q => Rank(QuizCatalogue.Text(q, "id")), StringComparer.Ordinal).ToArray();
        string[] levels = ["approachable", "approachable", "medium", "medium", "harder"];
        var chosen = new List<JsonElement>();
        var families = new HashSet<string>();
        var focuses = new HashSet<string>();
        var visits = 0;
        bool Search(int depth)
        {
            if (++visits > 100000) throw new InvalidOperationException("Scheduling search budget exhausted; review catalogue constraints.");
            if (depth == 5) return true;
            foreach (var q in pool)
            {
                var league = QuizCatalogue.Text(q, "league");
                var family = QuizCatalogue.Text(q, "factFamily");
                var focus = QuizCatalogue.Text(q, "focusKey");
                if (quotas[league] == 0 || QuizCatalogue.Text(q, "difficulty") != levels[depth] ||
                    families.Contains(family) || (focus.Length > 0 && focuses.Contains(focus))) continue;
                quotas[league]--; families.Add(family); if (focus.Length > 0) focuses.Add(focus); chosen.Add(q);
                if (Search(depth + 1)) return true;
                chosen.RemoveAt(chosen.Count - 1); quotas[league]++; families.Remove(family); focuses.Remove(focus);
            }
            return false;
        }
        if (!Search(0))
        {
            var counts = string.Join(", ", QuizCatalogue.Leagues.SelectMany(l => QuizCatalogue.Difficulties.Select(d =>
                $"{l}/{d}={pool.Count(q => QuizCatalogue.Text(q, "league") == l && QuizCatalogue.Text(q, "difficulty") == d)}")));
            throw new InvalidOperationException($"{date:yyyy-MM-dd}: insufficient compatible facts (bonus {bonus}); eligible counts: {counts}");
        }
        return new(date, chosen.OrderBy(q => Rank("order:" + QuizCatalogue.Text(q, "id")), StringComparer.Ordinal).Select(q => q.Clone()).ToArray());
    }

    public static SupplyReport Simulate(JsonElement catalogue, DateOnly launch, int days = 120)
    {
        if (days is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(days));
        var errors = QuizCatalogue.Validate(catalogue, true);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var facts = catalogue.GetProperty("questions").EnumerateArray().ToArray();
        var families = facts.Select(q => QuizCatalogue.Text(q, "factFamily")).Distinct().Count();
        var history = new List<ScheduledQuiz>();
        for (var i = 0; i < days; i++)
        {
            try { history.Add(Daily(catalogue, launch.AddDays(i), launch, history)); }
            catch (InvalidOperationException e) { return new(i, e.Message, families); }
        }
        // Reserve the first 30 queued days, then check every nonempty league selection.
        var reserved = history.Take(30).SelectMany(q => q.Questions).Select(q => QuizCatalogue.Text(q, "factFamily")).ToHashSet();
        for (var mask = 1; mask < 16; mask++)
        {
            var leagues = QuizCatalogue.Leagues.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            try { QuizSelection.Select(catalogue, new(leagues, QuizCatalogue.Topics), "supply-check", reserved); }
            catch (InvalidOperationException e) { return new(days, $"Practice {string.Join("+", leagues)}: {e.Message}", families); }
        }
        return new(days, null, families);
    }
}

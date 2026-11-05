using System.Text.Json;

namespace KnowBall.Application.Numbers;

/// <summary>Offline content preflight. Never register this catalogue as a public API response.</summary>
public static class QuizCatalogue
{
    public static readonly string[] Leagues = ["nba", "nfl", "mlb", "nhl"];
    public static readonly string[] Topics = ["stats", "awards", "teams", "seasons", "history", "nicknames", "playoff-moments"];
    /// <summary>Topics whose questions are about a whole career or era, so a season is optional and never shown.</summary>
    public static readonly string[] TimelessTopics = ["history", "nicknames"];
    public static readonly string[] Difficulties = ["approachable", "medium", "harder"];

    public static JsonDocument Read(string json)
    {
        var document = JsonDocument.Parse(json);
        var errors = Validate(document.RootElement);
        if (errors.Count == 0) return document;
        document.Dispose();
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    public static IReadOnlyList<string> Validate(JsonElement root, bool requireApproved = false)
    {
        var errors = new List<string>();
        if (root.ValueKind != JsonValueKind.Object) return ["Catalogue must be an object."];
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1)
            errors.Add("schemaVersion must be 1.");
        if (!root.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            return [.. errors, "questions must be an array."];
        if (questions.GetArrayLength() == 0) errors.Add("questions must not be empty.");
        var ids = new HashSet<string>();
        foreach (var q in questions.EnumerateArray())
        {
            if (q.ValueKind != JsonValueKind.Object) { errors.Add("Question must be an object."); continue; }
            var id = Text(q, "id");
            void Error(string message) => errors.Add($"{(id.Length == 0 ? "(missing id)" : id)}: {message}");
            if (id.Length == 0 || !ids.Add(id)) Error("ID must be nonempty and unique.");
            foreach (var field in new[] { "factFamily", "prompt", "explanation" })
                if (Text(q, field).Length == 0) Error($"{field} is required.");
            if (Text(q, "season").Length == 0 && !TimelessTopics.Contains(Text(q, "topic"))) Error("season is required.");
            if (Text(q, "season").Length > 0 && SeasonYear(q) is null) Error("season must start with a four-digit year.");
            if (q.TryGetProperty("dailyEligible", out var daily) && daily.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                Error("dailyEligible must be true or false.");
            if (q.TryGetProperty("recheckBy", out _) && RecheckBy(q) is null) Error("recheckBy must be an ISO date.");
            if (!Leagues.Contains(Text(q, "league"))) Error("Unsupported league.");
            if (!Topics.Contains(Text(q, "topic"))) Error("Unsupported topic.");
            if (!Difficulties.Contains(Text(q, "difficulty"))) Error("Unsupported difficulty.");
            if (Text(q, "scope") is not ("regular-season" or "postseason")) Error("Unsupported scope.");
            if (Text(q, "template") is not ("compare-four" or "match-value" or "closest-value" or "trivia")) Error("Unsupported template.");
            if (!q.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt32(out var rev) || rev < 1) Error("Positive revision required.");
            if (!DateOnly.TryParseExact(Text(q, "checkedAt"), "yyyy-MM-dd", out _)) Error("checkedAt must be an ISO date.");
            if (!q.TryGetProperty("source", out var source) ||
                !Uri.TryCreate(Text(source, "url"), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                Text(source, "credit").Length == 0) Error("HTTPS source URL and credit required.");
            if (requireApproved && Text(q, "publicationStatus") != "approved") Error("Not approved for publication.");
            if (!q.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array || options.GetArrayLength() != 4)
            { Error("Exactly four options required."); continue; }
            var choices = options.EnumerateArray().ToArray();
            if (choices.Any(o => o.ValueKind != JsonValueKind.Object)) { Error("Options must be objects."); continue; }
            foreach (var field in new[] { "id", "label" })
                if (choices.Any(o => Text(o, field).Length == 0) || choices.Select(o => Text(o, field)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                    Error($"Option {field}s must be distinct and nonempty.");
            if (choices.Count(o => Text(o, "id") == Text(q, "correctOptionId")) != 1) Error("Correct option must identify exactly one choice.");
            if (Text(q, "template") != "trivia")
            {
                var values = choices.Select(o => o.TryGetProperty("value", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetDecimal(out var value) ? (decimal?)value : null).ToArray();
                if (values.Any(n => n is null or < 0)) { Error("Nonnegative numeric supporting values required."); continue; }
                decimal? target = Text(q, "template") == "compare-four" ? values.Max() :
                    q.TryGetProperty("targetValue", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetDecimal(out var targetValue) ? targetValue : null;
                if (target is null or < 0) { Error("Nonnegative numeric target required."); continue; }
                var distances = values.Select(n => Math.Abs(n!.Value - target.Value)).ToArray();
                var winningDistance = Text(q, "template") == "closest-value" ? distances.Min() : 0;
                if (distances.Count(n => n == winningDistance) != 1 ||
                    Text(choices[Array.FindIndex(distances, n => n == winningDistance)], "id") != Text(q, "correctOptionId"))
                    Error("Supporting values must identify the unique correct answer.");
            }
        }
        return errors;
    }

    /// <summary>The first year of the question's season, or null for a timeless question.</summary>
    public static int? SeasonYear(JsonElement q) =>
        Text(q, "season") is { Length: >= 4 } season && int.TryParse(season.AsSpan(0, 4), out var year) ? year : null;

    public static DateOnly? RecheckBy(JsonElement q) =>
        DateOnly.TryParseExact(Text(q, "recheckBy"), "yyyy-MM-dd", out var date) ? date : null;

    /// <summary>
    /// Whether a question can be drawn under these filters. A timeless question has no season, so any
    /// year bound leaves it out. A question past its recheckBy date may be out of date and is skipped.
    /// </summary>
    public static bool Eligible(JsonElement q, QuizFilters filters, bool dailyOnly = false, DateOnly? asOf = null)
    {
        if (!filters.Leagues.Contains(Text(q, "league")) || !filters.Topics.Contains(Text(q, "topic"))) return false;
        if (dailyOnly && q.TryGetProperty("dailyEligible", out var daily) && daily.ValueKind == JsonValueKind.False) return false;
        if (asOf.HasValue && RecheckBy(q) is { } recheck && asOf > recheck) return false;
        if (!filters.FromYear.HasValue && !filters.ThroughYear.HasValue) return SeasonYear(q).HasValue || TimelessTopics.Contains(Text(q, "topic"));
        return SeasonYear(q) is { } year && (!filters.FromYear.HasValue || year >= filters.FromYear) && (!filters.ThroughYear.HasValue || year <= filters.ThroughYear);
    }

    internal static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()!.Trim() : "";
}

using System.Text.Json;

namespace KnowBall.Application.Numbers;

/// <summary>Offline content preflight. Never register this catalogue as a public API response.</summary>
public static class QuizCatalogue
{
    public static readonly string[] Leagues = ["nba", "nfl", "mlb", "nhl"];
    public static readonly string[] Topics = ["stats", "awards", "teams", "seasons"];
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
            foreach (var field in new[] { "factFamily", "season", "prompt", "explanation" })
                if (Text(q, field).Length == 0) Error($"{field} is required.");
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

    internal static string Text(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()!.Trim() : "";
}

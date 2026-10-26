using System.Text.Json;
using System.Text.Json.Nodes;
using KnowBall.Application.Numbers;

namespace KnowBall.UnitTests;

public class QuizCatalogueTests
{
    internal static JsonObject Catalogue()
    {
        var questions = new JsonArray();
        foreach (var league in QuizCatalogue.Leagues)
        for (var i = 0; i < 8; i++)
        {
            questions.Add(JsonSerializer.SerializeToNode(new {
                id = $"{league}-{i}", revision = 1, factFamily = $"{league}-{i}", league,
                season = "2010", prompt = "Who won?", explanation = "A won the award.",
                topic = "awards", scope = "regular-season", template = "trivia",
                difficulty = "medium", checkedAt = "2026-10-24", publicationStatus = "approved",
                source = new { url = "https://example.com/source", credit = "Test fixture" },
                correctOptionId = "a", options = new[] {
                    new { id = "a", label = "A" }, new { id = "b", label = "B" },
                    new { id = "c", label = "C" }, new { id = "d", label = "D" } }
            }));
        }
        return new JsonObject { ["schemaVersion"] = 1, ["questions"] = questions };
    }

    [Theory]
    [InlineData("league", "unsupported")]
    [InlineData("correctOptionId", "missing")]
    [InlineData("checkedAt", "bad date")]
    [InlineData("topic", "unknown")]
    public void Invalid_content_is_rejected(string field, string value)
    {
        var root = Catalogue();
        root["questions"]![0]![field] = value;
        Assert.Throws<InvalidDataException>(() => QuizCatalogue.Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"1\",\"questions\":[null,7]}")]
    public void Malformed_shape_returns_actionable_errors(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.NotEmpty(QuizCatalogue.Validate(doc.RootElement));
    }

    [Theory]
    [InlineData(100, "b", true)] // 102 is closer than 97; the target need not be an option.
    [InlineData(102, "b", true)] // An exact hit also wins.
    [InlineData(100, "a", false)] // Reject an incorrect answer key.
    [InlineData(105, "b", false)] // 102 and 108 tie.
    [InlineData(-1, "a", false)]
    public void Closest_value_requires_a_unique_nearest_answer(int target, string answer, bool valid)
    {
        var root = Catalogue();
        var question = root["questions"]![0]!;
        question["template"] = "closest-value";
        question["targetValue"] = target;
        question["correctOptionId"] = answer;
        var values = new[] { 97, 102, 108, 130 };
        for (var i = 0; i < values.Length; i++) question["options"]![i]!["value"] = values[i];
        using var doc = JsonDocument.Parse(root.ToJsonString());
        Assert.Equal(valid, QuizCatalogue.Validate(doc.RootElement).Count == 0);
    }

    [Fact]
    public void Closest_value_requires_a_target_and_supporting_values()
    {
        var root = Catalogue();
        var question = root["questions"]![0]!;
        question["template"] = "closest-value";
        foreach (var option in question["options"]!.AsArray()) option!["value"] = 10;
        Assert.Throws<InvalidDataException>(() => QuizCatalogue.Read(root.ToJsonString()));
        question["targetValue"] = 10;
        question["options"]![0]!.AsObject().Remove("value");
        Assert.Throws<InvalidDataException>(() => QuizCatalogue.Read(root.ToJsonString()));
    }

    [Fact]
    public void Preview_content_cannot_be_selected_for_publication()
    {
        var root = Catalogue();
        root["questions"]![0]!["publicationStatus"] = "prototype-only";
        using var doc = QuizCatalogue.Read(root.ToJsonString());
        Assert.Throws<InvalidDataException>(() => QuizSelection.Select(doc.RootElement,
            new(QuizCatalogue.Leagues, ["awards"]), "seed"));
    }

    [Fact]
    public void All_league_combinations_are_balanced_and_repeatable()
    {
        using var doc = QuizCatalogue.Read(Catalogue().ToJsonString());
        for (var mask = 1; mask < 16; mask++)
        {
            var leagues = QuizCatalogue.Leagues.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            var filters = new QuizFilters(leagues, ["awards"], 2000, 2019);
            var result = QuizSelection.Select(doc.RootElement, filters, "same-seed");
            Assert.Equal(5, result.Length);
            Assert.Equal(5, result.Select(q => q.GetProperty("factFamily").GetString()).Distinct().Count());
            Assert.All(result, q => Assert.Contains(q.GetProperty("league").GetString(), leagues));
            var counts = leagues.Select(l => result.Count(q => q.GetProperty("league").GetString() == l)).ToArray();
            Assert.True(counts.Max() - counts.Min() <= 1);
            Assert.Equal(result.Select(q => q.GetRawText()), QuizSelection.Select(doc.RootElement, filters, "same-seed").Select(q => q.GetRawText()));
        }
    }

    [Fact]
    public void Reservations_and_filters_fail_without_a_partial_round()
    {
        using var doc = QuizCatalogue.Read(Catalogue().ToJsonString());
        Assert.Throws<InvalidOperationException>(() => QuizSelection.Select(doc.RootElement,
            new(["nba"], ["awards"]), "seed", Enumerable.Range(0, 8).Select(i => $"nba-{i}").ToHashSet()));
        Assert.Throws<InvalidOperationException>(() => QuizSelection.Select(doc.RootElement,
            new(["nba"], ["seasons"]), "seed"));
        Assert.Throws<ArgumentException>(() => QuizSelection.Select(doc.RootElement,
            new([], ["awards"]), "seed"));
    }
}

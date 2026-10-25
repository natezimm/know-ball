namespace KnowBall.Domain;

public sealed class NumbersCatalogue
{
    public string Id { get; set; } = ""; // SHA-256 of canonical content
    public string ContentJson { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>Private frozen content. Not a public API DTO.</summary>
public sealed class NumbersQuiz
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string CatalogueId { get; set; } = "";
    public string QuestionsJson { get; set; } = "";
    public string AlgorithmVersion { get; set; } = "selection-v1";
    public DateTimeOffset CreatedAt { get; set; }
}

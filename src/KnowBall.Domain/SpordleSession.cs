namespace KnowBall.Domain;

/// <summary>Server-only round state. Never serialize this entity in an API response.</summary>
public sealed class SpordleSession
{
    public Guid Id { get; set; }
    public string Answer { get; set; } = "";
    public string Category { get; set; } = "";
    public int Attempts { get; set; }
    public int SolvedPositions { get; set; }
    public int? LetterHintPosition { get; set; }
    public bool Complete { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Optimistic concurrency token shared by API instances.</summary>
    public int Revision { get; set; }
    /// <summary>At most six accepted guesses, including their request IDs and original results.</summary>
    public string GuessesJson { get; set; } = "[]";
}

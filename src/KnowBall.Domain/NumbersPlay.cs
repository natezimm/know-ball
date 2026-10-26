namespace KnowBall.Domain;

public sealed class NumbersPlay
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public string StartKey { get; set; } = "";
    public string Filters { get; set; } = "";
    public DateOnly? Date { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string SecretJson { get; set; } = "[]";
    public string AnswersJson { get; set; } = "[]";
    public int Position { get; set; }
    public int Revision { get; set; }
}

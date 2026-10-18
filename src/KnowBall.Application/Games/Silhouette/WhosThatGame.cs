using System.Text.Json.Nodes;
using KnowBall.Domain;

namespace KnowBall.Application.Games.Silhouette;

/// <summary>
/// A hand-made puzzle: the three renderings of one photo, and the facts released as hints.
/// The image addresses are random names, so one cannot be guessed before it is released.
/// </summary>
public sealed record WhosThatSecret(
    int PlayerId,
    string Name,
    string SilhouetteUrl,
    string BlurredUrl,
    string PhotoUrl,
    int Year,
    string Team,
    string? Caption,
    string? HeadshotUrl = null,
    PhotoAttribution? Attribution = null);

public sealed record PhotoAttribution(string? SourceUrl, string? Credit, string? License);

/// <summary>
/// Name the player from the outline of an iconic photo. Five guesses; each miss releases the
/// next rung of the ladder: the year, the team, then the photo itself, blurred.
/// </summary>
public sealed class WhosThatGame : IGame
{
    public const string GameSlug = "whos-that";

    public GameType Type => GameType.Silhouette;
    public string Slug => GameSlug;
    public string Name => "Who's That Sportsman?";
    public int MaxGuesses => 5;
    public bool IsGenerated => false;
    public bool IncludesRetired => true;

    public Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken) =>
        Task.FromResult<GeneratedPuzzle?>(null);

    public Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken) =>
        Task.FromResult(new GuessEvaluation(guess.Id == puzzle.AnswerPlayerId));

    public JsonNode? Clues(Puzzle puzzle, int misses, bool complete)
    {
        var secret = GameJson.Read<WhosThatSecret>(puzzle.SecretJson);
        var rung = complete ? 3 : misses;
        return new JsonObject
        {
            ["silhouetteUrl"] = secret.SilhouetteUrl,
            ["year"] = rung >= 1 ? secret.Year : null,
            ["team"] = rung >= 2 ? secret.Team : null,
            ["blurredUrl"] = rung >= 3 ? secret.BlurredUrl : null,
        };
    }

    public JsonNode Reveal(Puzzle puzzle)
    {
        var secret = GameJson.Read<WhosThatSecret>(puzzle.SecretJson);
        return new JsonObject
        {
            ["playerId"] = secret.PlayerId,
            ["name"] = secret.Name,
            ["headshotUrl"] = secret.HeadshotUrl,
            ["photoUrl"] = secret.PhotoUrl,
            ["caption"] = secret.Caption,
            ["attribution"] = secret.Attribution is null ? null : GameJson.Node(secret.Attribution),
            ["year"] = secret.Year,
            ["team"] = secret.Team,
        };
    }
}

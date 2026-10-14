using KnowBall.Domain;

namespace KnowBall.Application.Games;

/// <summary>The games the engine runs, in home-page order.</summary>
public sealed class GameRegistry(IEnumerable<IGame> games)
{
    public IReadOnlyList<IGame> All { get; } = games.ToList();

    public IGame? Find(string? slug) =>
        All.FirstOrDefault(g => g.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

    public IGame? Find(GameType type) => All.FirstOrDefault(g => g.Type == type);
}

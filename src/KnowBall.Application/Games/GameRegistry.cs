using Microsoft.Extensions.Options;
using KnowBall.Domain;

namespace KnowBall.Application.Games;

/// <summary>The games the engine runs, in home-page order, and which league editions of each are live.</summary>
public sealed class GameRegistry(IEnumerable<IGame> games, IOptions<GameLeagueOptions>? options = null)
{
    private readonly GameLeagueOptions _leagues = options?.Value ?? new GameLeagueOptions();

    public IReadOnlyList<IGame> All { get; } = games.ToList();

    public IGame? Find(string? slug) =>
        All.FirstOrDefault(g => g.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

    public IGame? Find(GameType type) => All.FirstOrDefault(g => g.Type == type);

    /// <summary>The game's live editions, the default league first.</summary>
    public IReadOnlyList<string> LeaguesOf(IGame game) => game.Leagues.Where(_leagues.IsLive).ToList();

    /// <summary>The league asked for, lower-cased, or the game's default when none was named; null if the game has no live edition for it.</summary>
    public string? League(IGame game, string? requested)
    {
        var live = LeaguesOf(game);
        if (string.IsNullOrWhiteSpace(requested)) return live.FirstOrDefault();
        var code = requested.Trim().ToLowerInvariant();
        return live.Contains(code) ? code : null;
    }
}

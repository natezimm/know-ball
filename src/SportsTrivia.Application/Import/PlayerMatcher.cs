using SportsTrivia.Application.Text;
using SportsTrivia.Domain;

namespace SportsTrivia.Application.Import;

/// <summary>
/// Works out which stored player a source row refers to. Sources share no common id, so a row is
/// matched on the source's own id if it has been seen before, and otherwise on name and birth date.
/// </summary>
public sealed class PlayerMatcher
{
    private static readonly HashSet<string> Suffixes = ["jr", "sr", "ii", "iii", "iv", "v"];

    private readonly string _idSource;
    private readonly Dictionary<string, Player> _bySourceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Player> _byPrimaryId = new(StringComparer.Ordinal);
    private readonly Dictionary<DateOnly, List<Player>> _byBirthDate = [];
    private readonly Dictionary<string, List<Player>> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<Player> _bound = [];

    /// <param name="players">Every player in the league, loaded with <see cref="Player.ExternalIds"/>.</param>
    public PlayerMatcher(string idSource, IEnumerable<Player> players)
    {
        _idSource = idSource;
        foreach (var player in players) Index(player);
    }

    /// <summary>The stored player for this row, or null if it is someone new.</summary>
    public Player? Find(string externalId, string fullName, DateOnly? birthDate)
    {
        if (_bySourceId.TryGetValue(externalId, out var known)) return known;

        // Rows written before ids were namespaced carry the source's id as their only id.
        if (_byPrimaryId.TryGetValue(externalId, out var legacy) && legacy.ExternalIds.Count == 0) return legacy;

        var name = NameNormalizer.Normalize(fullName);
        if (name.Length == 0) return null;

        if (birthDate is { } born && _byBirthDate.TryGetValue(born, out var sameDay))
        {
            var free = sameDay.Where(p => !_bound.Contains(p)).ToList();
            var match = Only(free.Where(p => p.NormalizedName == name))
                        ?? Only(free.Where(p => Core(p.NormalizedName) == Core(name)))
                        ?? Only(free.Where(p => SameSurnameAndInitial(p.NormalizedName, name)));
            if (match is not null) return match;
        }

        // Name alone is only trusted when one side has no birth date to disagree with.
        if (_byName.TryGetValue(name, out var sameName))
        {
            var candidates = sameName.Where(p => !_bound.Contains(p) && (p.BirthDate is null || birthDate is null)).ToList();
            if (candidates.Count == 1 && sameName.Count == 1) return candidates[0];
        }

        return null;
    }

    /// <summary>Records that <paramref name="player"/> is known to this source as <paramref name="externalId"/>.</summary>
    public void Bind(Player player, string externalId)
    {
        if (!player.ExternalIds.Any(x => x.Source == _idSource))
            player.ExternalIds.Add(new PlayerExternalId { Source = _idSource, Value = externalId });
        Index(player);
    }

    /// <summary>A free value for <see cref="Player.ExternalId"/>: the source's id, prefixed if another source used the same one.</summary>
    public string PrimaryIdFor(string externalId) =>
        _byPrimaryId.ContainsKey(externalId) ? $"{_idSource}:{externalId}" : externalId;

    private void Index(Player player)
    {
        _byPrimaryId.TryAdd(player.ExternalId, player);

        var id = player.ExternalIds.FirstOrDefault(x => x.Source == _idSource);
        if (id is not null)
        {
            _bySourceId[id.Value] = player;
            _bound.Add(player);
        }

        if (player.BirthDate is { } born) AddTo(_byBirthDate, born, player);
        if (player.NormalizedName.Length > 0) AddTo(_byName, player.NormalizedName, player);
    }

    private static void AddTo<TKey>(Dictionary<TKey, List<Player>> index, TKey key, Player player) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = [];
        if (!list.Contains(player)) list.Add(player);
    }

    private static Player? Only(IEnumerable<Player> players)
    {
        Player? found = null;
        foreach (var p in players)
        {
            if (found is not null) return null;
            found = p;
        }
        return found;
    }

    /// <summary>"jaren jackson jr" → "jaren jackson".</summary>
    private static string Core(string normalized) =>
        string.Join(' ', normalized.Split(' ').Where(t => !Suffixes.Contains(t)));

    /// <summary>"nic claxton" and "nicolas claxton": same surname, same first letter.</summary>
    private static bool SameSurnameAndInitial(string a, string b)
    {
        var x = Core(a).Split(' ');
        var y = Core(b).Split(' ');
        return x.Length > 1 && y.Length > 1 && x[^1] == y[^1] && x[0][0] == y[0][0];
    }
}

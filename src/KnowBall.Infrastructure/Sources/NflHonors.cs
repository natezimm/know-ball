using System.Reflection;
using System.Text.Json;
using KnowBall.Application.Import;
using KnowBall.Application.Text;

namespace KnowBall.Infrastructure.Sources;

/// <summary>
/// The AP All-Pro teams and Pro Bowl rosters of every season since 1999, which no open data set has by season.
/// They come from Wikipedia's yearly pages through <c>scripts/nfl-honors.py</c> into the embedded
/// <c>nfl-honors.json</c>, as a name and the franchise he played for. A name is placed on the player of that name
/// who was on that franchise that season; a name the record cannot place on exactly one player is left out.
/// </summary>
internal sealed class NflHonors
{
    private sealed record HonorsFile(Dictionary<string, Dictionary<string, List<List<string>>>> Seasons);

    /// <summary>The file's lists and the award each one is.</summary>
    private static readonly (string List, string Type)[] Lists =
    [
        ("allPro1", AwardTypes.AllPro + "1"),
        ("allPro2", AwardTypes.AllPro + "2"),
        ("proBowl", AwardTypes.ProBowl),
    ];

    public static readonly Lazy<NflHonors> Instance = new(Load);

    /// <summary>One honor as the file has it: season, award, name and franchise code.</summary>
    internal sealed record Honor(int Season, string Type, string Name, string Franchise);

    public IReadOnlyList<Honor> Honors { get; }

    internal NflHonors(IReadOnlyList<Honor> honors) => Honors = honors;

    /// <summary>
    /// The honors that land on exactly one player, by gsis id, and the ones that could not be placed. A player is a
    /// match by name when the names agree once accents, punctuation, spaces and suffixes are dropped ("Chris Harris,
    /// Jr.", "J. J. Watt"), and is then picked by the franchise he played for that season.
    /// </summary>
    public (Dictionary<string, List<HistoryAward>> Awards, List<Honor> Unplaced) Place(
        IReadOnlyDictionary<string, string> names,
        IReadOnlyDictionary<string, List<NflverseHistorySource.RosterSpell>> spells,
        NflTeams teams)
    {
        var byName = new Dictionary<string, List<string>>();
        var byLastName = new Dictionary<string, List<string>>();
        foreach (var (id, name) in names)
        {
            if (!spells.ContainsKey(id)) continue;
            Index(byName, NameKey(name), id);
            Index(byLastName, LastNameKey(name), id);
        }

        var awards = new Dictionary<string, List<HistoryAward>>();
        var unplaced = new List<Honor>();
        foreach (var honor in Honors)
        {
            var played = Played(byName.GetValueOrDefault(NameKey(honor.Name)), honor);
            var onTeam = OnTeam(played, honor);
            var pick = onTeam.Count == 1 ? onTeam[0] : null;
            if (onTeam.Count == 0)
            {
                // Known by another name in one record or the other ("Darius Leonard" and "Shaquille Leonard", "Josh Allen"
                // and "Josh Hines-Allen"): the one player of that last name on that franchise that season. A player of
                // the same name on another team is not taken: he is more often someone else than a roster gap.
                var sameLast = OnTeam(Played(byLastName.GetValueOrDefault(LastNameKey(honor.Name)), honor), honor);
                if (sameLast.Count > 1) sameLast = sameLast.Where(id => FirstNameKey(names[id]) == FirstNameKey(honor.Name)).ToList();
                pick = sameLast.Count == 1 ? sameLast[0] : null;
            }
            if (pick is null)
            {
                unplaced.Add(honor);
                continue;
            }

            if (!awards.TryGetValue(pick, out var list)) awards[pick] = list = [];
            var award = new HistoryAward(honor.Season, honor.Type);
            if (!list.Contains(award)) list.Add(award);
        }

        // A player voted onto the first team at one position and the second at another is a first-team All-Pro.
        foreach (var list in awards.Values)
            list.RemoveAll(a => a.Type == AwardTypes.AllPro + "2" && list.Contains(a with { Type = AwardTypes.AllPro + "1" }));
        return (awards, unplaced);

        List<string> Played(List<string>? ids, Honor honor) => ids?.Where(id => spells[id].Any(s => s.Season == honor.Season)).ToList() ?? [];
        List<string> OnTeam(List<string> ids, Honor honor) =>
            ids.Where(id => spells[id].Any(s => s.Season == honor.Season && teams.Franchise(s.TeamKey) == honor.Franchise)).ToList();
    }

    private static void Index(Dictionary<string, List<string>> index, string key, string id)
    {
        if (!index.TryGetValue(key, out var ids)) index[key] = ids = [];
        ids.Add(id);
    }

    private static readonly HashSet<string> Suffixes = ["jr", "sr", "ii", "iii", "iv", "v"];

    /// <summary>Letters only, without a suffix: "J. J. Watt" and "J.J. Watt" are both "jjwatt".</summary>
    internal static string NameKey(string name) => string.Concat(Words(name));

    internal static string LastNameKey(string name) => Words(name).LastOrDefault() ?? "";

    private static string FirstNameKey(string name) => Words(name).FirstOrDefault() ?? "";

    private static IEnumerable<string> Words(string name) =>
        NameNormalizer.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Suffixes.Contains(w));

    internal static NflHonors Parse(Stream stream)
    {
        var file = JsonSerializer.Deserialize<HonorsFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException("nfl-honors.json is empty");
        var honors = new List<Honor>();
        foreach (var (season, row) in file.Seasons)
        {
            foreach (var (list, type) in Lists)
            {
                if (!row.TryGetValue(list, out var entries)) continue;
                honors.AddRange(entries.Where(e => e.Count == 2).Select(e => new Honor(int.Parse(season), type, e[0], e[1])));
            }
        }
        return new NflHonors(honors);
    }

    private static NflHonors Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("nfl-honors.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        return Parse(stream);
    }
}

using System.Reflection;
using System.Text.Json;
using SportsTrivia.Application.Import;

namespace SportsTrivia.Infrastructure.Sources;

/// <summary>
/// Static team metadata the stats feeds don't carry: conference, division, arena and
/// coordinates (for Scouting Report hints and the Geo Sports map). Shipped as an embedded JSON file.
/// </summary>
public sealed record TeamReference(
    string ExternalId,
    string Abbreviation,
    string City,
    string Name,
    string Conference,
    string Division,
    string Arena,
    double Latitude,
    double Longitude)
{
    private static readonly Lazy<IReadOnlyDictionary<string, TeamReference>> NbaTeams = new(() => Load("nba-teams.json"));

    /// <summary>NBA teams keyed by abbreviation.</summary>
    public static IReadOnlyDictionary<string, TeamReference> Nba => NbaTeams.Value;

    /// <summary>The league's own logo art, addressed by the team's NBA id.</summary>
    public string LogoUrl => $"https://cdn.nba.com/logos/nba/{ExternalId}/primary/L/logo.svg";

    public SourceTeam ToSourceTeam() =>
        new(ExternalId, City, Name, Abbreviation, Conference, Division, Arena, Latitude, Longitude, LogoUrl);

    private static IReadOnlyDictionary<string, TeamReference> Load(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        var list = JsonSerializer.Deserialize<List<TeamReference>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException($"{fileName} is empty");
        return list.ToDictionary(t => t.Abbreviation, StringComparer.OrdinalIgnoreCase);
    }
}

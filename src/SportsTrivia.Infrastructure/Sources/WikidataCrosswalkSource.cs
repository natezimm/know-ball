using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsTrivia.Application.Import;

namespace SportsTrivia.Infrastructure.Sources;

public sealed class WikidataOptions
{
    public string Endpoint { get; set; } = "https://query.wikidata.org/sparql";
    /// <summary>Wikidata asks every client to say who it is.</summary>
    public string UserAgent { get; set; } = "KnowBall/1.0 (personal sports trivia project)";
    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>
/// Wikidata as the link between sources: for everyone with a Basketball-Reference id it gives
/// their NBA.com and ESPN ids and where they were born. Two queries, because asking for ids and
/// place names in one runs past the service's 60 second limit and comes back cut short.
/// </summary>
public sealed class WikidataCrosswalkSource(
    HttpClient http,
    IOptions<WikidataOptions> options,
    ILogger<WikidataCrosswalkSource> logger) : ICrosswalkSource
{
    public const string SourceName = "wikidata";
    public string Name => SourceName;

    // P2685 Basketball-Reference id, P3647 NBA.com id, P3685 ESPN NBA id, P19 place of birth, P625 coordinates.
    internal const string IdsQuery =
        "SELECT ?bref ?nba ?espn ?place ?coord WHERE { ?p wdt:P2685 ?bref . " +
        "OPTIONAL { ?p wdt:P3647 ?nba } OPTIONAL { ?p wdt:P3685 ?espn } " +
        "OPTIONAL { ?p wdt:P19 ?place . OPTIONAL { ?place wdt:P625 ?coord } } }";

    // P17 country.
    internal const string PlacesQuery =
        "SELECT ?place ?placeLabel ?countryLabel WHERE { " +
        "{ SELECT DISTINCT ?place WHERE { ?p wdt:P2685 ?bref ; wdt:P19 ?place . } } " +
        "OPTIONAL { ?place rdfs:label ?placeLabel . FILTER(LANG(?placeLabel) = \"en\") } " +
        "OPTIONAL { ?place wdt:P17 ?country . ?country rdfs:label ?countryLabel . FILTER(LANG(?countryLabel) = \"en\") } }";

    public async Task<IReadOnlyList<CrosswalkEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        using var ids = await QueryAsync(IdsQuery, cancellationToken);

        Dictionary<string, (string? Name, string? Country)> places = [];
        try
        {
            using var labels = await QueryAsync(PlacesQuery, cancellationToken);
            places = ParsePlaces(labels.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Ids and coordinates are still worth having without the place names.
            logger.LogWarning("Wikidata place names could not be loaded: {Error}", ex.Message);
        }

        var entries = ParseEntries(ids.RootElement, places);
        logger.LogInformation("wikidata returned {Entries} players, {WithNba} with an NBA id",
            entries.Count, entries.Count(e => e.Ids.ContainsKey(CrosswalkImportService.NbaIdSource)));
        return entries;
    }

    private async Task<JsonDocument> QueryAsync(string sparql, CancellationToken ct)
    {
        var opts = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{opts.Endpoint}?query={Uri.EscapeDataString(sparql)}");
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        request.Headers.UserAgent.ParseAdd(opts.UserAgent);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        // A query that times out still answers 200 and breaks off mid-document, which fails to parse here.
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    internal static Dictionary<string, (string? Name, string? Country)> ParsePlaces(JsonElement root)
    {
        var places = new Dictionary<string, (string?, string?)>();
        foreach (var row in Bindings(root))
        {
            if (Value(row, "place") is not { } place) continue;
            // A town that has changed hands lists several countries; the first one is kept.
            places.TryAdd(place, (Value(row, "placeLabel"), Country(Value(row, "countryLabel"))));
        }
        return places;
    }

    internal static List<CrosswalkEntry> ParseEntries(JsonElement root, Dictionary<string, (string? Name, string? Country)> places)
    {
        var entries = new Dictionary<string, CrosswalkEntry>();
        foreach (var row in Bindings(root))
        {
            // Stored as "j/jamesle01".
            if (Value(row, "bref") is not { } raw) continue;
            var bref = raw[(raw.LastIndexOf('/') + 1)..];
            if (bref.Length == 0 || entries.ContainsKey(bref)) continue;

            var ids = new Dictionary<string, string>();
            if (Value(row, "nba") is { } nba) ids[CrosswalkImportService.NbaIdSource] = nba;
            if (Value(row, "espn") is { } espn) ids[EspnRosterSource.SourceName] = espn;

            (string? Name, string? Country) place = default;
            if (Value(row, "place") is { } placeId) places.TryGetValue(placeId, out place);
            var (latitude, longitude) = ParsePoint(Value(row, "coord"));

            entries[bref] = new CrosswalkEntry(BrefCsvSource.SourceName, bref, ids, place.Name, place.Country, latitude, longitude);
        }
        return entries.Values.ToList();
    }

    /// <summary>"Point(-81.5177 41.0730)" is longitude then latitude.</summary>
    internal static (double? Latitude, double? Longitude) ParsePoint(string? wkt)
    {
        if (wkt is null || !wkt.StartsWith("Point(", StringComparison.OrdinalIgnoreCase) || !wkt.EndsWith(')')) return (null, null);
        var parts = wkt[6..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            ? (Math.Round(latitude, 5), Math.Round(longitude, 5))
            : (null, null);
    }

    /// <summary>The roster feed and the seed file say "USA".</summary>
    private static string? Country(string? label) => label is "United States" or "United States of America" ? "USA" : label;

    private static IEnumerable<JsonElement> Bindings(JsonElement root) =>
        root.TryGetProperty("results", out var results) && results.TryGetProperty("bindings", out var bindings)
            ? bindings.EnumerateArray()
            : throw new InvalidOperationException("Wikidata response has no results; the query may have failed.");

    private static string? Value(JsonElement row, string name) =>
        row.TryGetProperty(name, out var cell) && cell.TryGetProperty("value", out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

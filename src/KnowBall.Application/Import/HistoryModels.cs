namespace KnowBall.Application.Import;

/// <summary>
/// The full record of a league: everyone who ever played, season by season. Loaded in bulk a
/// few times a year, unlike <see cref="IPlayerSource"/>, which is the nightly roster check.
/// </summary>
public interface IHistorySource
{
    /// <summary>Stable name used on the command line and in ImportRun.Source.</summary>
    string Name { get; }
    Task<HistorySnapshot> FetchAsync(CancellationToken cancellationToken);
}

/// <param name="IdSource">Namespace of the player ids, e.g. "bref".</param>
/// <param name="LatestSeason">Start year of the newest season in the data.</param>
public sealed record HistorySnapshot(
    string LeagueCode,
    string IdSource,
    int LatestSeason,
    IReadOnlyList<HistoryTeam> Teams,
    IReadOnlyList<HistoryPlayer> Players);

/// <param name="Key">What season rows call the team.</param>
/// <param name="ActiveTo">Last season under this name; null while it still plays.</param>
/// <param name="FranchiseKey">Key of the team this one became; null for a current team.</param>
public sealed record HistoryTeam(string Key, SourceTeam Team, int? ActiveFrom, int? ActiveTo, string? FranchiseKey);

public sealed record HistoryPlayer(
    string ExternalId,
    string FirstName,
    string LastName,
    string? Position,
    int? HeightInches,
    int? WeightLbs,
    DateOnly? BirthDate,
    string? College,
    int? FromYear,
    int? ToYear,
    bool HallOfFame,
    int? DraftYear,
    int? DraftNumber,
    IReadOnlyList<HistorySeason> Seasons,
    IReadOnlyList<HistoryAward> Awards);

/// <summary>
/// One line of a player's record, in the order played. A season split between teams has one
/// line per team (<see cref="IsSeasonTotal"/> false) and a combined line with no team.
/// </summary>
public sealed record HistorySeason(
    int Season,
    string? TeamKey,
    bool IsSeasonTotal,
    int? Games,
    double? MinutesPerGame,
    double? PointsPerGame,
    double? ReboundsPerGame,
    double? AssistsPerGame,
    string StatsJson = "{}");

public sealed record HistoryAward(int Season, string Type);

/// <summary>Award type names shared by the importer and the games.</summary>
public static class AwardTypes
{
    public const string AllStar = "AllStar";
    public const string Mvp = "MVP";
    public const string DefensivePlayer = "DPOY";
    public const string Rookie = "ROY";
    public const string SixthMan = "SMOY";
    public const string MostImproved = "MIP";
    public const string Clutch = "ClutchPOY";
    /// <summary>Followed by 1, 2 or 3 for first, second or third team.</summary>
    public const string AllLeague = "AllNBA";
    public const string AllDefense = "AllDefense";
    public const string AllRookie = "AllRookie";
}

/// <summary>Links between data sources, and facts only the linking source has (birthplaces).</summary>
public interface ICrosswalkSource
{
    string Name { get; }
    Task<IReadOnlyList<CrosswalkEntry>> FetchAsync(CancellationToken cancellationToken);
}

/// <param name="KeySource">Namespace of <paramref name="KeyValue"/>, e.g. "bref".</param>
/// <param name="Ids">The same player's ids elsewhere: "nba" → 2544, "espn" → 1966.</param>
public sealed record CrosswalkEntry(
    string KeySource,
    string KeyValue,
    IReadOnlyDictionary<string, string> Ids,
    string? BirthPlace = null,
    string? Country = null,
    double? Latitude = null,
    double? Longitude = null);

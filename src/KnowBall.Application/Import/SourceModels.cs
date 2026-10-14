namespace KnowBall.Application.Import;

/// <summary>Where current rosters come from (a live feed, a bundled seed file, ...).</summary>
public interface IPlayerSource
{
    /// <summary>Stable name used on the command line and in ImportRun.Source.</summary>
    string Name { get; }
    Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken);
}

/// <param name="IdSource">
/// The namespace the player ids belong to ("espn", "seed"); defaults to the source's name.
/// A player is matched on this id first, then on name and birth date.
/// </param>
/// <param name="InSeason">
/// False before opening night and after the Finals. A team change seen then happened between
/// seasons, so the old spell ended with the previous season, not this one.
/// </param>
/// <param name="Supplement">
/// True for a feed that only knows today's rosters and sits on top of the full player history.
/// It sets team, jersey number and active status, and fills in facts that are missing; it never
/// overwrites a name, position, height or popularity that the history import supplied.
/// </param>
public sealed record SourceSnapshot(
    string LeagueCode,
    int Season,
    IReadOnlyList<SourceTeam> Teams,
    IReadOnlyList<SourcePlayer> Players,
    string? IdSource = null,
    bool InSeason = true,
    bool Supplement = false);

public sealed record SourceTeam(
    string ExternalId,
    string City,
    string Name,
    string Abbreviation,
    string? Conference = null,
    string? Division = null,
    string? Arena = null,
    double? Latitude = null,
    double? Longitude = null,
    string? LogoUrl = null);

public sealed record SourcePlayer(
    string ExternalId,
    string FirstName,
    string LastName,
    string? TeamExternalId,
    string? Position = null,
    int? HeightInches = null,
    int? WeightLbs = null,
    string? JerseyNumber = null,
    string? College = null,
    string? Country = null,
    int? DraftYear = null,
    int? DraftNumber = null,
    int? FromYear = null,
    int? ToYear = null,
    DateOnly? BirthDate = null,
    double? PointsPerGame = null,
    double? ReboundsPerGame = null,
    double? AssistsPerGame = null,
    string? HeadshotUrl = null,
    // Optional fixed popularity (seed data); otherwise computed.
    double? Popularity = null,
    string? BirthPlace = null);

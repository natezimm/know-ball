namespace SportsTrivia.Domain;

public class League
{
    public int Id { get; set; }
    /// <summary>Short stable code used in URLs, e.g. "nba".</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sport { get; set; } = "";
    public List<Team> Teams { get; set; } = [];
}

public class Team
{
    public int Id { get; set; }
    public int LeagueId { get; set; }
    public League? League { get; set; }
    /// <summary>Id of the team in the upstream data source.</summary>
    public string ExternalId { get; set; } = "";
    public string City { get; set; } = "";
    public string Name { get; set; } = "";
    public string Abbreviation { get; set; } = "";
    public string? Conference { get; set; }
    public string? Division { get; set; }
    public string? Arena { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LogoUrl { get; set; }
    /// <summary>First and last season (start year) under this name and city. Both null for seed data; ActiveTo is null while the team still plays.</summary>
    public int? ActiveFrom { get; set; }
    public int? ActiveTo { get; set; }
    /// <summary>The team this one became, for defunct identities: the SuperSonics point at the Thunder. Null for current teams.</summary>
    public int? FranchiseId { get; set; }
    public Team? Franchise { get; set; }

    public string FullName => $"{City} {Name}".Trim();
}

public class Player
{
    public int Id { get; set; }
    public int LeagueId { get; set; }
    public League? League { get; set; }
    public string ExternalId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName { get; set; } = "";
    /// <summary>Lower-case, accent-free name used for search ("nikola jokic").</summary>
    public string NormalizedName { get; set; } = "";
    public DateOnly? BirthDate { get; set; }
    public int? HeightInches { get; set; }
    public int? WeightLbs { get; set; }
    public string? Position { get; set; }
    public string? JerseyNumber { get; set; }
    public string? College { get; set; }
    public string? Country { get; set; }
    public int? DraftYear { get; set; }
    public int? DraftNumber { get; set; }
    public int? FromYear { get; set; }
    /// <summary>Last season played (start year). Null when unknown.</summary>
    public int? ToYear { get; set; }
    public string? BirthPlace { get; set; }
    public double? BirthLatitude { get; set; }
    public double? BirthLongitude { get; set; }
    public bool IsHallOfFame { get; set; }
    public int? CurrentTeamId { get; set; }
    public Team? CurrentTeam { get; set; }
    public string? HeadshotUrl { get; set; }
    public string? SilhouetteUrl { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>0-100. Daily puzzles favour well-known players.</summary>
    public double Popularity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PlayerTeamStint> Stints { get; set; } = [];
    public List<PlayerSeasonStat> SeasonStats { get; set; } = [];
    public List<Award> Awards { get; set; } = [];
    public List<PlayerExternalId> ExternalIds { get; set; } = [];
}

/// <summary>
/// What another data source calls a player: "bref" jamesle01, "nba" 2544, "espn" 1966.
/// Lets sources that share no id be joined to the same player.
/// </summary>
public class PlayerExternalId
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }
    public string Source { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>A continuous spell at one team. Powers the Career Path game.</summary>
public class PlayerTeamStint
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    /// <summary>Season start year: 2025 means the 2025-26 season.</summary>
    public int StartSeason { get; set; }
    /// <summary>Last season with the team (start year). Null while the player is still there.</summary>
    public int? EndSeason { get; set; }
    /// <summary>Seasons actually played for the team in this spell. 0 when unknown (roster-only data).</summary>
    public int Seasons { get; set; }
    public int Order { get; set; }
}

public class PlayerSeasonStat
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }
    public int Season { get; set; }
    /// <summary>Null on the combined row of a season split between teams.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    /// <summary>
    /// True on the one row that covers the player's whole season: the only row of a one-team
    /// season, or the combined row of a traded player's. Per-team rows of a split season are false.
    /// </summary>
    public bool IsSeasonTotal { get; set; } = true;
    public int? Games { get; set; }
    public double? MinutesPerGame { get; set; }
    public double? PointsPerGame { get; set; }
    public double? ReboundsPerGame { get; set; }
    public double? AssistsPerGame { get; set; }
    /// <summary>Sport-specific extra stats as JSON.</summary>
    public string StatsJson { get; set; } = "{}";
}

public class Award
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }
    public int Season { get; set; }
    public string Type { get; set; } = "";
}

public enum GameType
{
    ScoutingReport = 1,
    Silhouette = 2,
    CareerPath = 3,
    StatQuiz = 4,
    GeoSports = 5,
    Sportle = 6,
    SoundItOut = 7,
    /// <summary>The Wordle-style word game. (Value 1 was called Spordle before the rename.)</summary>
    Spordle = 8,
}

public enum PuzzleStatus
{
    /// <summary>Made in the admin area and not on the calendar yet.</summary>
    Draft = 0,
    /// <summary>Has a date. It is live once that date arrives.</summary>
    Scheduled = 1,
    /// <summary>A one-off practice round: no date, no number, not counted in stats.</summary>
    Practice = 2,
}

public class Puzzle
{
    public int Id { get; set; }
    public GameType GameType { get; set; }
    public PuzzleStatus Status { get; set; } = PuzzleStatus.Scheduled;
    /// <summary>The day it is the daily puzzle. Null for drafts and practice rounds.</summary>
    public DateOnly? Date { get; set; }
    /// <summary>"Scouting Report #142". Counts days from the game's first puzzle.</summary>
    public int? Number { get; set; }
    public int LeagueId { get; set; }
    public League? League { get; set; }
    public int Difficulty { get; set; }
    /// <summary>Snapshot of what the client may see, frozen at generation time.</summary>
    public string PayloadJson { get; set; } = "{}";
    /// <summary>The answer and anything released step by step. Never sent to the client as is.</summary>
    public string SecretJson { get; set; } = "{}";
    public int? AnswerPlayerId { get; set; }
    public Player? AnswerPlayer { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class UserResult
{
    public int Id { get; set; }
    /// <summary>Signed-in user id, or an anonymous id from the browser.</summary>
    public string UserKey { get; set; } = "";
    public int PuzzleId { get; set; }
    public Puzzle? Puzzle { get; set; }
    public string GuessesJson { get; set; } = "[]";
    public bool Solved { get; set; }
    /// <summary>Guesses used when solved; 0 otherwise.</summary>
    public int Score { get; set; }
    public int HintsUsed { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>Null while the game is still in progress.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum ImportStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>One run of the data importer; the audit trail behind the "nightly import works" gate.</summary>
public class ImportRun
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string LeagueCode { get; set; } = "";
    public int? Season { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public ImportStatus Status { get; set; }
    public int TeamsUpserted { get; set; }
    public int PlayersInserted { get; set; }
    public int PlayersUpdated { get; set; }
    public int PlayersDeactivated { get; set; }
    public int TeamChanges { get; set; }
    public string? Error { get; set; }
}

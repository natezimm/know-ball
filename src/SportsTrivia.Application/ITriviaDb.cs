using Microsoft.EntityFrameworkCore;
using SportsTrivia.Domain;

namespace SportsTrivia.Application;

/// <summary>The slice of the EF Core context that application services use.</summary>
public interface ITriviaDb
{
    DbSet<League> Leagues { get; }
    DbSet<Team> Teams { get; }
    DbSet<Player> Players { get; }
    DbSet<PlayerTeamStint> PlayerTeamStints { get; }
    DbSet<PlayerSeasonStat> PlayerSeasonStats { get; }
    DbSet<Award> Awards { get; }
    DbSet<PlayerExternalId> PlayerExternalIds { get; }
    DbSet<Puzzle> Puzzles { get; }
    DbSet<UserResult> UserResults { get; }
    DbSet<ImportRun> ImportRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops tracking every entity, discarding unsaved changes.</summary>
    void ClearTracked();
}

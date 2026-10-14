using Microsoft.EntityFrameworkCore;
using KnowBall.Application;
using KnowBall.Domain;

namespace KnowBall.Infrastructure.Persistence;

public class TriviaDbContext(DbContextOptions<TriviaDbContext> options) : DbContext(options), ITriviaDb
{
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerTeamStint> PlayerTeamStints => Set<PlayerTeamStint>();
    public DbSet<PlayerSeasonStat> PlayerSeasonStats => Set<PlayerSeasonStat>();
    public DbSet<Award> Awards => Set<Award>();
    public DbSet<PlayerExternalId> PlayerExternalIds => Set<PlayerExternalId>();
    public DbSet<Puzzle> Puzzles => Set<Puzzle>();
    public DbSet<UserResult> UserResults => Set<UserResult>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public void ClearTracked() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<League>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.Sport).HasMaxLength(32);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Team>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.City).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.Abbreviation).HasMaxLength(8);
            e.Property(x => x.Conference).HasMaxLength(32);
            e.Property(x => x.Division).HasMaxLength(32);
            e.Property(x => x.Arena).HasMaxLength(128);
            e.Property(x => x.LogoUrl).HasMaxLength(512);
            e.Ignore(x => x.FullName);
            e.HasIndex(x => new { x.LeagueId, x.ExternalId }).IsUnique();
            e.HasOne(x => x.League).WithMany(l => l.Teams).HasForeignKey(x => x.LeagueId);
            e.HasOne(x => x.Franchise).WithMany().HasForeignKey(x => x.FranchiseId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Player>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.FirstName).HasMaxLength(64);
            e.Property(x => x.LastName).HasMaxLength(64);
            e.Property(x => x.FullName).HasMaxLength(128);
            e.Property(x => x.NormalizedName).HasMaxLength(128);
            e.Property(x => x.Position).HasMaxLength(16);
            e.Property(x => x.JerseyNumber).HasMaxLength(8);
            e.Property(x => x.College).HasMaxLength(128);
            e.Property(x => x.Country).HasMaxLength(64);
            e.Property(x => x.HeadshotUrl).HasMaxLength(512);
            e.Property(x => x.SilhouetteUrl).HasMaxLength(512);
            e.Property(x => x.BirthPlace).HasMaxLength(128);
            e.HasIndex(x => new { x.LeagueId, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.NormalizedName);
            e.HasIndex(x => new { x.LeagueId, x.IsActive, x.Popularity });
            e.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId);
            e.HasOne(x => x.CurrentTeam).WithMany().HasForeignKey(x => x.CurrentTeamId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PlayerExternalId>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.Value).HasMaxLength(64);
            e.HasIndex(x => new { x.Source, x.Value }).IsUnique();
            e.HasIndex(x => new { x.PlayerId, x.Source }).IsUnique();
            e.HasOne(x => x.Player).WithMany(p => p.ExternalIds).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlayerTeamStint>(e =>
        {
            e.HasIndex(x => new { x.PlayerId, x.Order }).IsUnique();
            e.HasOne(x => x.Player).WithMany(p => p.Stints).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PlayerSeasonStat>(e =>
        {
            // One row per team played for, plus a combined row (TeamId null) when a season was split.
            e.HasIndex(x => new { x.PlayerId, x.Season, x.TeamId }).IsUnique();
            e.HasOne(x => x.Player).WithMany(p => p.SeasonStats).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.StatsJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : null);
        });

        b.Entity<Award>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(32);
            e.HasIndex(x => new { x.PlayerId, x.Season, x.Type }).IsUnique();
            e.HasOne(x => x.Player).WithMany(p => p.Awards).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Puzzle>(e =>
        {
            e.Property(x => x.GameType).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PayloadJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : null);
            e.Property(x => x.SecretJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : null);
            // Drafts and practice rounds have no date; both databases let many null dates share the index.
            e.HasIndex(x => new { x.GameType, x.LeagueId, x.Date }).IsUnique();
            e.HasIndex(x => new { x.GameType, x.Status });
            e.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId);
            e.HasOne(x => x.AnswerPlayer).WithMany().HasForeignKey(x => x.AnswerPlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<UserResult>(e =>
        {
            e.Property(x => x.UserKey).HasMaxLength(64);
            e.Property(x => x.GuessesJson).HasColumnType(Database.IsNpgsql() ? "jsonb" : null);
            e.HasIndex(x => new { x.UserKey, x.PuzzleId }).IsUnique();
            e.HasOne(x => x.Puzzle).WithMany().HasForeignKey(x => x.PuzzleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ImportRun>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.LeagueCode).HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.StartedAt);
        });

        // SQLite can't ORDER BY DateTimeOffset; store as UTC ticks there.
        if (Database.IsSqlite())
        {
            foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                prop.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KnowBall.Application.Text;
using KnowBall.Domain;

namespace KnowBall.Application.Import;

/// <summary>
/// The nightly roster check. Pulls a snapshot from an <see cref="IPlayerSource"/> and upserts
/// leagues, teams, players, team stints and current-season stats. Every run is recorded as an
/// <see cref="ImportRun"/>. Safe to run repeatedly: a second run with the same data changes nothing.
/// </summary>
public sealed class PlayerImportService(ITriviaDb db, TimeProvider clock, ILogger<PlayerImportService> logger)
{
    /// <summary>
    /// If a source suddenly returns far fewer players than we have active, assume the
    /// source is broken (partial response, API change) and refuse to deactivate everyone.
    /// </summary>
    public const double MinRetainedFraction = 0.5;

    public async Task<ImportRun> RunAsync(IPlayerSource source, CancellationToken cancellationToken = default)
    {
        var run = new ImportRun
        {
            Source = source.Name,
            StartedAt = clock.GetUtcNow(),
            Status = ImportStatus.Running,
        };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Import {RunId} started from source {Source}", run.Id, source.Name);

        try
        {
            var snapshot = await source.FetchAsync(cancellationToken);
            run.LeagueCode = snapshot.LeagueCode;
            run.Season = snapshot.Season;

            await ApplyAsync(snapshot, snapshot.IdSource ?? source.Name, run, cancellationToken);
            run.Status = ImportStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Import {RunId} succeeded: {Teams} teams, {Inserted} new players, {Updated} updated, {Deactivated} deactivated, {Moves} team changes",
                run.Id, run.TeamsUpserted, run.PlayersInserted, run.PlayersUpdated, run.PlayersDeactivated, run.TeamChanges);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Import {RunId} from {Source} failed", run.Id, source.Name);

            // Throw away half-applied changes, keep only the failed run record.
            db.ClearTracked();
            run.Status = ImportStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            run.FinishedAt = clock.GetUtcNow();
            run.TeamsUpserted = run.PlayersInserted = run.PlayersUpdated = run.PlayersDeactivated = run.TeamChanges = 0;
            db.ImportRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return run;
    }

    private async Task ApplyAsync(SourceSnapshot snapshot, string idSource, ImportRun run, CancellationToken ct)
    {
        if (snapshot.Players.Count == 0)
            throw new InvalidOperationException($"Source returned no players for {snapshot.LeagueCode} {snapshot.Season}.");

        var now = clock.GetUtcNow();
        var season = snapshot.Season;
        var league = await LeagueDirectory.GetOrCreateAsync(db, snapshot.LeagueCode, ct);

        var teams = await LeagueDirectory.UpsertTeamsAsync(db, league, snapshot.Teams, ct);
        run.TeamsUpserted = snapshot.Teams.Count;

        // ---- Players -----------------------------------------------------------------------
        var existing = league.Id == 0
            ? []
            : await db.Players
                .Where(p => p.LeagueId == league.Id)
                .Include(p => p.CurrentTeam)
                .Include(p => p.Stints)
                .Include(p => p.ExternalIds)
                .Include(p => p.SeasonStats.Where(s => s.Season == season))
                .AsSplitQuery()
                .ToListAsync(ct);

        var activeBefore = existing.Count(p => p.IsActive);
        // Count each player once, so a feed that repeats rows cannot look complete.
        var listedCount = snapshot.Players.Select(p => p.ExternalId).Distinct().Count();
        if (activeBefore >= 20 && listedCount < activeBefore * MinRetainedFraction)
        {
            throw new InvalidOperationException(
                $"Source returned {listedCount} players but {activeBefore} are active; " +
                "refusing to deactivate more than half the league. Check the source.");
        }

        var matcher = new PlayerMatcher(idSource, existing);
        var seenIds = new HashSet<string>();
        var listed = new HashSet<Player>();
        foreach (var sp in snapshot.Players)
        {
            if (!seenIds.Add(sp.ExternalId)) continue; // duplicate rows in source

            Team? team = null;
            if (sp.TeamExternalId is { } teamExt && !teams.TryGetValue(teamExt, out team))
                logger.LogWarning("Player {Player} references unknown team {Team}", sp.ExternalId, teamExt);

            var player = matcher.Find(sp.ExternalId, $"{sp.FirstName} {sp.LastName}", sp.BirthDate);
            if (player is not null && !listed.Add(player)) continue; // two rows for one person

            if (player is null)
            {
                player = new Player { League = league, ExternalId = matcher.PrimaryIdFor(sp.ExternalId) };
                CopyFields(player, sp, season);
                player.UpdatedAt = now;
                db.Players.Add(player);
                existing.Add(player);
                listed.Add(player);
                run.PlayersInserted++;
            }
            else
            {
                var changed = snapshot.Supplement ? FillGaps(player, sp) : CopyFields(player, sp, season);
                if (!player.IsActive) { player.IsActive = true; changed = true; }
                if (changed)
                {
                    player.UpdatedAt = now;
                    run.PlayersUpdated++;
                }
            }

            matcher.Bind(player, sp.ExternalId);
            if (MoveToTeam(player, team, season, snapshot.InSeason)) run.TeamChanges++;
            UpsertSeasonStat(player, sp, season, team);
        }

        // ---- Players no longer listed ------------------------------------------------------
        foreach (var player in existing)
        {
            if (listed.Contains(player) || !player.IsActive) continue;
            player.IsActive = false;
            MoveToTeam(player, null, season, snapshot.InSeason);
            player.UpdatedAt = now;
            run.PlayersDeactivated++;
        }
    }

    /// <summary>Copies source fields onto the player. Returns true if anything changed.</summary>
    private static bool CopyFields(Player p, SourcePlayer sp, int season)
    {
        var changed = false;
        void Set<T>(T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changed = true;
        }

        var fullName = $"{sp.FirstName} {sp.LastName}".Trim();
        Set(p.FirstName, sp.FirstName, v => p.FirstName = v);
        Set(p.LastName, sp.LastName, v => p.LastName = v);
        Set(p.FullName, fullName, v => p.FullName = v);
        Set(p.NormalizedName, NameNormalizer.Normalize(fullName), v => p.NormalizedName = v);
        Set(p.Position, sp.Position ?? p.Position, v => p.Position = v);
        Set(p.HeightInches, sp.HeightInches ?? p.HeightInches, v => p.HeightInches = v);
        Set(p.WeightLbs, sp.WeightLbs ?? p.WeightLbs, v => p.WeightLbs = v);
        Set(p.JerseyNumber, sp.JerseyNumber ?? p.JerseyNumber, v => p.JerseyNumber = v);
        Set(p.College, sp.College ?? p.College, v => p.College = v);
        Set(p.Country, sp.Country ?? p.Country, v => p.Country = v);
        Set(p.BirthPlace, sp.BirthPlace ?? p.BirthPlace, v => p.BirthPlace = v);
        Set(p.DraftYear, sp.DraftYear ?? p.DraftYear, v => p.DraftYear = v);
        Set(p.DraftNumber, sp.DraftNumber ?? p.DraftNumber, v => p.DraftNumber = v);
        Set(p.FromYear, sp.FromYear ?? p.FromYear, v => p.FromYear = v);
        Set(p.BirthDate, sp.BirthDate ?? p.BirthDate, v => p.BirthDate = v);
        Set(p.HeadshotUrl, sp.HeadshotUrl ?? p.HeadshotUrl, v => p.HeadshotUrl = v);
        Set(p.Popularity, PopularityCalculator.Compute(sp, season), v => p.Popularity = v);
        return changed;
    }

    /// <summary>
    /// For a roster feed layered on the full history: the jersey number follows the feed, and
    /// everything else is only filled in where nothing is known yet. Returns true if anything changed.
    /// </summary>
    private static bool FillGaps(Player p, SourcePlayer sp)
    {
        var changed = false;
        void Fill<T>(T? current, T? value, Action<T> assign) where T : class
        {
            if (current is not null || value is null) return;
            assign(value);
            changed = true;
        }
        void FillValue<T>(T? current, T? value, Action<T> assign) where T : struct
        {
            if (current is not null || value is null) return;
            assign(value.Value);
            changed = true;
        }

        if (sp.JerseyNumber is { } jersey && jersey != p.JerseyNumber)
        {
            p.JerseyNumber = jersey;
            changed = true;
        }

        Fill(p.Position, sp.Position, v => p.Position = v);
        Fill(p.College, sp.College, v => p.College = v);
        Fill(p.Country, sp.Country, v => p.Country = v);
        Fill(p.BirthPlace, sp.BirthPlace, v => p.BirthPlace = v);
        Fill(p.HeadshotUrl, sp.HeadshotUrl, v => p.HeadshotUrl = v);
        FillValue(p.HeightInches, sp.HeightInches, v => p.HeightInches = v);
        FillValue(p.WeightLbs, sp.WeightLbs, v => p.WeightLbs = v);
        FillValue(p.DraftYear, sp.DraftYear, v => p.DraftYear = v);
        FillValue(p.DraftNumber, sp.DraftNumber, v => p.DraftNumber = v);
        FillValue(p.FromYear, sp.FromYear, v => p.FromYear = v);
        FillValue(p.BirthDate, sp.BirthDate, v => p.BirthDate = v);
        return changed;
    }

    /// <summary>Closes the open stint and opens a new one when the team changes. Returns true on a move.</summary>
    private static bool MoveToTeam(Player player, Team? team, int season, bool inSeason)
    {
        if (ReferenceEquals(player.CurrentTeam, team)) return false;

        var hadTeam = player.CurrentTeam is not null;
        var open = player.Stints.FirstOrDefault(s => s.EndSeason is null);
        // A move between seasons ended the old spell with the season before.
        if (open is not null) open.EndSeason = inSeason ? season : Math.Max(open.StartSeason, season - 1);

        player.CurrentTeam = team;
        if (team is null)
        {
            player.CurrentTeamId = null;
            return hadTeam;
        }

        // Back with the team he was last on (after a year out injured, say): the same spell carries on.
        var last = player.Stints.MaxBy(s => s.Order);
        if (!hadTeam && last is not null && (ReferenceEquals(last.Team, team) || (team.Id != 0 && last.TeamId == team.Id)))
        {
            last.EndSeason = null;
            return false;
        }

        player.Stints.Add(new PlayerTeamStint
        {
            Team = team,
            StartSeason = season,
            Order = last is null ? 1 : last.Order + 1,
        });
        // A player's first-ever team is an arrival, not a move.
        return hadTeam;
    }

    private static void UpsertSeasonStat(Player player, SourcePlayer sp, int season, Team? team)
    {
        if (sp.PointsPerGame is null && sp.ReboundsPerGame is null && sp.AssistsPerGame is null) return;

        var stat = player.SeasonStats.FirstOrDefault(s => s.Season == season && s.IsSeasonTotal);
        if (stat is null)
        {
            stat = new PlayerSeasonStat { Season = season, IsSeasonTotal = true };
            player.SeasonStats.Add(stat);
        }

        if (team is not null) stat.Team = team;
        stat.PointsPerGame = sp.PointsPerGame;
        stat.ReboundsPerGame = sp.ReboundsPerGame;
        stat.AssistsPerGame = sp.AssistsPerGame;
    }
}

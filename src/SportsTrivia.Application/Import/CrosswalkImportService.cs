using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SportsTrivia.Domain;

namespace SportsTrivia.Application.Import;

/// <summary>
/// Joins players to their ids in other sources and adds what only the linking source knows:
/// where they were born. An NBA id also gives the player a headshot.
/// </summary>
public sealed class CrosswalkImportService(ITriviaDb db, TimeProvider clock, ILogger<CrosswalkImportService> logger)
{
    public const string NbaIdSource = "nba";
    public const string NbaHeadshotUrl = "https://cdn.nba.com/headshots/nba/latest/260x190/{0}.png";

    public async Task<ImportRun> RunAsync(ICrosswalkSource source, string leagueCode = "nba", CancellationToken cancellationToken = default)
    {
        var run = new ImportRun { Source = source.Name, LeagueCode = leagueCode, StartedAt = clock.GetUtcNow(), Status = ImportStatus.Running };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var entries = await source.FetchAsync(cancellationToken);
            if (entries.Count == 0) throw new InvalidOperationException("Crosswalk source returned nothing.");

            run.PlayersUpdated = await ApplyAsync(entries, leagueCode, cancellationToken);
            run.Status = ImportStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Crosswalk {RunId} from {Source}: {Updated} players updated", run.Id, source.Name, run.PlayersUpdated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Crosswalk {RunId} from {Source} failed", run.Id, source.Name);

            db.ClearTracked();
            run.Status = ImportStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            run.FinishedAt = clock.GetUtcNow();
            run.PlayersUpdated = 0;
            db.ImportRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return run;
    }

    private async Task<int> ApplyAsync(IReadOnlyList<CrosswalkEntry> entries, string leagueCode, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var players = await db.Players
            .Where(p => p.League!.Code == leagueCode)
            .Include(p => p.ExternalIds)
            .ToListAsync(ct);

        // An id belongs to one player. If two entries or two sources disagree, the first holder keeps it.
        var owners = players
            .SelectMany(p => p.ExternalIds.Select(x => (Key: (x.Source, x.Value), Player: p)))
            .ToDictionary(x => x.Key, x => x.Player);

        var updated = 0;
        var conflicts = 0;
        foreach (var entry in entries)
        {
            if (!owners.TryGetValue((entry.KeySource, entry.KeyValue), out var player)) continue;

            var changed = false;
            foreach (var (idSource, value) in entry.Ids)
            {
                if (owners.TryGetValue((idSource, value), out var owner))
                {
                    if (owner != player) conflicts++;
                    continue;
                }
                if (player.ExternalIds.Any(x => x.Source == idSource)) continue;

                player.ExternalIds.Add(new PlayerExternalId { Source = idSource, Value = value });
                owners[(idSource, value)] = player;
                changed = true;
            }

            var nbaId = player.ExternalIds.FirstOrDefault(x => x.Source == NbaIdSource)?.Value;
            if (nbaId is not null)
            {
                var headshot = string.Format(System.Globalization.CultureInfo.InvariantCulture, NbaHeadshotUrl, nbaId);
                if (player.HeadshotUrl != headshot) { player.HeadshotUrl = headshot; changed = true; }
            }

            if (entry.BirthPlace is not null && player.BirthPlace != entry.BirthPlace) { player.BirthPlace = entry.BirthPlace; changed = true; }
            if (entry.Latitude is not null && entry.Longitude is not null
                && (player.BirthLatitude != entry.Latitude || player.BirthLongitude != entry.Longitude))
            {
                player.BirthLatitude = entry.Latitude;
                player.BirthLongitude = entry.Longitude;
                changed = true;
            }
            if (player.Country is null && entry.Country is not null) { player.Country = entry.Country; changed = true; }

            if (!changed) continue;
            player.UpdatedAt = now;
            updated++;
        }

        if (conflicts > 0)
            logger.LogWarning("{Conflicts} ids in the crosswalk already belong to a different player and were skipped", conflicts);
        return updated;
    }
}

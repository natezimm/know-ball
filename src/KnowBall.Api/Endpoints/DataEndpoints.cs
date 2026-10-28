using Microsoft.EntityFrameworkCore;
using KnowBall.Application;
using KnowBall.Application.Players;
using KnowBall.Domain;

namespace KnowBall.Api.Endpoints;

/// <summary>Player search for the guess boxes, and read-only views of the data for monitoring.</summary>
public static class DataEndpoints
{
    public static IEndpointRouteBuilder MapData(this IEndpointRouteBuilder api)
    {
        api.MapGet("/players/search", async (
                string? q,
                string? league,
                int? limit,
                bool? includeInactive,
                int? puzzleId,
                PlayerSearchService search,
                CancellationToken ct) =>
            {
                var results = await search.SearchAsync(q, league ?? "nba", limit ?? 10, includeInactive ?? false, ct, puzzleId);
                return Results.Ok(results);
            })
            .WithName("SearchPlayers")
            .WithSummary("Autocomplete players by name (min 2 characters, accent-insensitive). includeInactive adds retired players.");

        api.MapGet("/import-runs/latest", async (ITriviaDb db, CancellationToken ct) =>
            {
                var run = await db.ImportRuns.AsNoTracking()
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(ct);
                return run is null ? Results.NotFound() : Results.Ok(run);
            })
            .WithName("LatestImportRun")
            .WithSummary("The most recent importer run, for monitoring the nightly job.");

        api.MapGet("/stats/summary", async (ITriviaDb db, CancellationToken ct) => Results.Ok(new
            {
                leagues = await db.Leagues.CountAsync(ct),
                teams = await db.Teams.CountAsync(ct),
                players = await db.Players.CountAsync(ct),
                activePlayers = await db.Players.CountAsync(p => p.IsActive, ct),
                playersWithHeadshots = await db.Players.CountAsync(p => p.HeadshotUrl != null, ct),
                lastSuccessfulImport = await db.ImportRuns.AsNoTracking()
                    .Where(r => r.Status == ImportStatus.Succeeded)
                    .OrderByDescending(r => r.Id)
                    .Select(r => r.FinishedAt)
                    .FirstOrDefaultAsync(ct),
            }))
            .WithName("DataSummary");

        return api;
    }
}

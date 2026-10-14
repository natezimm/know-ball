using System.Text.Json;
using Microsoft.Extensions.Options;
using SportsTrivia.Application.Import;

namespace SportsTrivia.Infrastructure.Sources;

public sealed class SeedFileOptions
{
    /// <summary>Path to the seed JSON, relative to the app's folder unless absolute.</summary>
    public string Path { get; set; } = "Data/nba-sample-players.json";
}

/// <summary>
/// Offline source for local development, CI and demos: a small hand-written list of
/// well-known players plus the full team reference list. Not a source of truth.
/// </summary>
public sealed class SeedFilePlayerSource(IOptions<SeedFileOptions> options) : IPlayerSource
{
    public const string SourceName = "seed";
    public string Name => SourceName;

    public async Task<SourceSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.Path;
        if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(AppContext.BaseDirectory, path);

        await using var stream = File.OpenRead(path);
        var seed = await JsonSerializer.DeserializeAsync<SeedFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
                   ?? throw new InvalidOperationException($"Seed file {path} is empty");

        var teams = TeamReference.Nba;
        var players = seed.Players.Select(p =>
        {
            string? teamId = null;
            if (p.Team is not null)
            {
                if (!teams.TryGetValue(p.Team, out var team))
                    throw new InvalidOperationException($"Seed player {p.FirstName} {p.LastName} has unknown team '{p.Team}'");
                teamId = team.ExternalId;
            }

            return new SourcePlayer(
                ExternalId: p.ExternalId,
                FirstName: p.FirstName,
                LastName: p.LastName,
                TeamExternalId: teamId,
                Position: p.Position,
                HeightInches: p.HeightInches,
                JerseyNumber: p.JerseyNumber,
                College: p.College,
                Country: p.Country,
                DraftYear: p.DraftYear,
                FromYear: p.DraftYear,
                BirthDate: p.BirthDate,
                Popularity: p.Popularity);
        }).ToList();

        return new SourceSnapshot(seed.LeagueCode, seed.Season, teams.Values.Select(t => t.ToSourceTeam()).ToList(), players);
    }

    private sealed record SeedFile(string LeagueCode, int Season, List<SeedPlayer> Players);

    private sealed record SeedPlayer(
        string ExternalId,
        string FirstName,
        string LastName,
        string? Team,
        string? Position,
        int? HeightInches,
        string? JerseyNumber,
        string? College,
        string? Country,
        int? DraftYear,
        double? Popularity,
        DateOnly? BirthDate = null);
}

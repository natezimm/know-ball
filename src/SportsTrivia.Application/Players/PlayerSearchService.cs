using Microsoft.EntityFrameworkCore;
using SportsTrivia.Application.Text;

namespace SportsTrivia.Application.Players;

/// <param name="Years">First and last calendar years of the career, "1996–2016"; open-ended while still playing.</param>
public sealed record PlayerSearchResult(
    int Id,
    string FullName,
    string? Position,
    string? TeamAbbreviation,
    string? TeamName,
    string? HeadshotUrl,
    bool IsActive = true,
    string? Years = null);

/// <summary>Autocomplete for every guess box on the site.</summary>
public sealed class PlayerSearchService(ITriviaDb db)
{
    public const int MinQueryLength = 2;
    public const int MaxLimit = 25;

    public async Task<IReadOnlyList<PlayerSearchResult>> SearchAsync(
        string? query,
        string leagueCode = "nba",
        int limit = 10,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var term = NameNormalizer.Normalize(query);
        if (term.Length < MinQueryLength) return [];

        limit = Math.Clamp(limit, 1, MaxLimit);
        leagueCode = leagueCode.ToLowerInvariant();

        // NormalizedName is already lower-case and accent-free, so a plain Contains/StartsWith
        // translates to LIKE on both Postgres and SQLite and uses the same rules everywhere.
        var wordStart = " " + term;
        var candidates = db.Players
            .AsNoTracking()
            .Where(p => p.League!.Code == leagueCode)
            .Where(p => includeInactive || p.IsActive)
            .Where(p => p.NormalizedName.StartsWith(term) || p.NormalizedName.Contains(wordStart));

        var rows = await candidates
            .OrderByDescending(p => p.NormalizedName.StartsWith(term))
            .ThenByDescending(p => p.Popularity)
            .ThenBy(p => p.FullName)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.Position,
                TeamAbbreviation = p.CurrentTeam != null ? p.CurrentTeam.Abbreviation : null,
                TeamName = p.CurrentTeam != null ? p.CurrentTeam.City + " " + p.CurrentTeam.Name : null,
                p.HeadshotUrl,
                p.IsActive,
                p.FromYear,
                p.ToYear,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(p => new PlayerSearchResult(
                p.Id, p.FullName, p.Position, p.TeamAbbreviation, p.TeamName, p.HeadshotUrl, p.IsActive,
                CareerYears(p.FromYear, p.ToYear, p.IsActive)))
            .ToList();
    }

    /// <summary>Seasons are stored by start year; a career that ended with the 2015-16 season ended in 2016.</summary>
    internal static string? CareerYears(int? from, int? to, bool isActive)
    {
        if (from is null) return null;
        return isActive || to is null ? $"{from}–" : $"{from}–{to + 1}";
    }
}

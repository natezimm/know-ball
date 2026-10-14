using Microsoft.EntityFrameworkCore;
using SportsTrivia.Domain;

namespace SportsTrivia.Application.Import;

internal static class LeagueDirectory
{
    private static readonly Dictionary<string, (string Name, string Sport)> Known = new()
    {
        ["nba"] = ("NBA", "Basketball"),
        ["wnba"] = ("WNBA", "Basketball"),
        ["nfl"] = ("NFL", "Football"),
        ["mlb"] = ("MLB", "Baseball"),
        ["nhl"] = ("NHL", "Hockey"),
    };

    /// <summary>The stored league, or a new one that is added but not yet saved.</summary>
    public static async Task<League> GetOrCreateAsync(ITriviaDb db, string code, CancellationToken ct)
    {
        code = code.ToLowerInvariant();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Code == code, ct);
        if (league is not null) return league;

        var (name, sport) = Known.TryGetValue(code, out var known) ? known : (code.ToUpperInvariant(), "Unknown");
        league = new League { Code = code, Name = name, Sport = sport };
        db.Leagues.Add(league);
        return league;
    }

    /// <summary>Adds or updates teams by their external id and returns all of the league's teams keyed by it.</summary>
    public static async Task<Dictionary<string, Team>> UpsertTeamsAsync(
        ITriviaDb db, League league, IEnumerable<SourceTeam> sourceTeams, CancellationToken ct)
    {
        var teams = league.Id == 0
            ? new Dictionary<string, Team>()
            : await db.Teams.Where(t => t.LeagueId == league.Id).ToDictionaryAsync(t => t.ExternalId, ct);

        foreach (var st in sourceTeams)
        {
            if (!teams.TryGetValue(st.ExternalId, out var team))
            {
                team = new Team { League = league, ExternalId = st.ExternalId };
                db.Teams.Add(team);
                teams[st.ExternalId] = team;
            }

            team.City = st.City;
            team.Name = st.Name;
            team.Abbreviation = st.Abbreviation;
            team.Conference = st.Conference ?? team.Conference;
            team.Division = st.Division ?? team.Division;
            team.Arena = st.Arena ?? team.Arena;
            team.Latitude = st.Latitude ?? team.Latitude;
            team.Longitude = st.Longitude ?? team.Longitude;
            team.LogoUrl = st.LogoUrl ?? team.LogoUrl;
        }

        return teams;
    }
}

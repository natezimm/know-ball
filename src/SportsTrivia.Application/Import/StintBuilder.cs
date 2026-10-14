namespace SportsTrivia.Application.Import;

/// <summary>A continuous spell with one team, in season start years.</summary>
public sealed record StintSpan(string TeamKey, int StartSeason, int EndSeason, int Seasons);

/// <summary>Turns a player's season-by-season team rows into the stops of a career.</summary>
public static class StintBuilder
{
    /// <param name="rows">
    /// One entry per team per season, in the order played. A player who sits out a year and
    /// comes back to the same team stays in one spell; only a different team starts a new one.
    /// </param>
    public static IReadOnlyList<StintSpan> Build(IEnumerable<(int Season, string TeamKey)> rows)
    {
        var spans = new List<StintSpan>();
        foreach (var (season, team) in rows)
        {
            if (spans.Count > 0 && spans[^1].TeamKey == team)
            {
                var last = spans[^1];
                if (season == last.EndSeason) continue;
                spans[^1] = last with { EndSeason = season, Seasons = last.Seasons + 1 };
            }
            else
            {
                spans.Add(new StintSpan(team, season, season, 1));
            }
        }
        return spans;
    }
}

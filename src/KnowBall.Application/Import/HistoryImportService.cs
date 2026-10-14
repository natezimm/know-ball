using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KnowBall.Application.Text;
using KnowBall.Domain;

namespace KnowBall.Application.Import;

/// <summary>
/// Loads a league's full record from an <see cref="IHistorySource"/>: every player, their
/// season lines, awards, the stops of their career and a popularity score. The record owns the
/// facts that do not change (names, birth dates, positions, past teams); who is on which team
/// today is left to the nightly roster import. Safe to run repeatedly.
/// </summary>
public sealed class HistoryImportService(ITriviaDb db, TimeProvider clock, ILogger<HistoryImportService> logger)
{
    public async Task<ImportRun> RunAsync(IHistorySource source, CancellationToken cancellationToken = default)
    {
        var run = new ImportRun { Source = source.Name, StartedAt = clock.GetUtcNow(), Status = ImportStatus.Running };
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("History import {RunId} started from {Source}", run.Id, source.Name);

        try
        {
            var snapshot = await source.FetchAsync(cancellationToken);
            run.LeagueCode = snapshot.LeagueCode;
            run.Season = snapshot.LatestSeason;

            await ApplyAsync(snapshot, run, cancellationToken);
            run.Status = ImportStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "History import {RunId} succeeded: {Teams} teams, {Inserted} new players, {Updated} updated",
                run.Id, run.TeamsUpserted, run.PlayersInserted, run.PlayersUpdated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "History import {RunId} from {Source} failed", run.Id, source.Name);

            db.ClearTracked();
            run.Status = ImportStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            run.FinishedAt = clock.GetUtcNow();
            run.TeamsUpserted = run.PlayersInserted = run.PlayersUpdated = 0;
            db.ImportRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return run;
    }

    private async Task ApplyAsync(HistorySnapshot snapshot, ImportRun run, CancellationToken ct)
    {
        if (snapshot.Players.Count == 0)
            throw new InvalidOperationException($"History source returned no players for {snapshot.LeagueCode}.");

        var now = clock.GetUtcNow();
        var league = await LeagueDirectory.GetOrCreateAsync(db, snapshot.LeagueCode, ct);

        // ---- Teams, then the links from old identities to the team they became -------------
        var teamsByExternalId = await LeagueDirectory.UpsertTeamsAsync(db, league, snapshot.Teams.Select(t => t.Team), ct);
        var teams = snapshot.Teams.ToDictionary(t => t.Key, t => teamsByExternalId[t.Team.ExternalId]);
        foreach (var st in snapshot.Teams)
        {
            var team = teams[st.Key];
            team.ActiveFrom = st.ActiveFrom;
            team.ActiveTo = st.ActiveTo;
            team.Franchise = st.FranchiseKey is { } key && teams.TryGetValue(key, out var franchise) && franchise != team
                ? franchise
                : null;
            if (team.Franchise is null) team.FranchiseId = null;
        }
        run.TeamsUpserted = snapshot.Teams.Count;

        // ---- Players -----------------------------------------------------------------------
        var existing = league.Id == 0
            ? []
            : await db.Players
                .Where(p => p.LeagueId == league.Id)
                .Include(p => p.CurrentTeam)
                .Include(p => p.Stints)
                .Include(p => p.ExternalIds)
                .Include(p => p.SeasonStats)
                .Include(p => p.Awards)
                .AsSplitQuery()
                .ToListAsync(ct);

        var matcher = new PlayerMatcher(snapshot.IdSource, existing);
        var seen = new HashSet<string>();
        foreach (var hp in snapshot.Players)
        {
            if (!seen.Add(hp.ExternalId)) continue;

            var fullName = $"{hp.FirstName} {hp.LastName}".Trim();
            var player = matcher.Find(hp.ExternalId, fullName, hp.BirthDate);
            var isNew = player is null;
            if (player is null)
            {
                player = new Player { League = league, ExternalId = matcher.PrimaryIdFor(hp.ExternalId), IsActive = false };
                db.Players.Add(player);
            }

            var spans = StintBuilder.Build(hp.Seasons
                .Where(s => s.TeamKey is not null && teams.ContainsKey(s.TeamKey))
                .Select(s => (s.Season, s.TeamKey!)));

            var changed = CopyFacts(player, hp, fullName);
            if (isNew && hp.ToYear == snapshot.LatestSeason && spans.Count > 0)
            {
                // Until a roster import says otherwise, whoever played last season is still on that team.
                player.IsActive = true;
                player.CurrentTeam = teams[spans[^1].TeamKey];
            }

            changed |= SyncSeasons(player, hp, teams);
            changed |= SyncAwards(player, hp);
            changed |= SyncStints(player, spans, teams, snapshot.LatestSeason);

            var popularity = PopularityCalculator.FromCareer(Summarise(hp, player.IsActive, snapshot.LatestSeason));
            if (player.Popularity != popularity)
            {
                player.Popularity = popularity;
                changed = true;
            }

            matcher.Bind(player, hp.ExternalId);
            if (isNew)
            {
                player.UpdatedAt = now;
                run.PlayersInserted++;
            }
            else if (changed)
            {
                player.UpdatedAt = now;
                run.PlayersUpdated++;
            }
        }
    }

    private static bool CopyFacts(Player p, HistoryPlayer hp, string fullName)
    {
        var changed = false;
        void Set<T>(T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changed = true;
        }

        Set(p.FirstName, hp.FirstName, v => p.FirstName = v);
        Set(p.LastName, hp.LastName, v => p.LastName = v);
        Set(p.FullName, fullName, v => p.FullName = v);
        Set(p.NormalizedName, NameNormalizer.Normalize(fullName), v => p.NormalizedName = v);
        Set(p.Position, hp.Position ?? p.Position, v => p.Position = v);
        Set(p.HeightInches, hp.HeightInches ?? p.HeightInches, v => p.HeightInches = v);
        Set(p.WeightLbs, hp.WeightLbs ?? p.WeightLbs, v => p.WeightLbs = v);
        Set(p.BirthDate, hp.BirthDate ?? p.BirthDate, v => p.BirthDate = v);
        Set(p.College, hp.College ?? p.College, v => p.College = v);
        Set(p.FromYear, hp.FromYear ?? p.FromYear, v => p.FromYear = v);
        Set(p.ToYear, hp.ToYear ?? p.ToYear, v => p.ToYear = v);
        Set(p.DraftYear, hp.DraftYear ?? p.DraftYear, v => p.DraftYear = v);
        Set(p.DraftNumber, hp.DraftNumber ?? p.DraftNumber, v => p.DraftNumber = v);
        Set(p.IsHallOfFame, hp.HallOfFame, v => p.IsHallOfFame = v);
        return changed;
    }

    private static bool SyncSeasons(Player player, HistoryPlayer hp, Dictionary<string, Team> teams)
    {
        var changed = false;
        var kept = new HashSet<PlayerSeasonStat>();

        foreach (var s in hp.Seasons)
        {
            Team? team = null;
            if (s.TeamKey is not null && !teams.TryGetValue(s.TeamKey, out team)) continue;

            var stat = player.SeasonStats.FirstOrDefault(x => x.Season == s.Season && SameTeam(x, team));
            if (stat is null)
            {
                stat = new PlayerSeasonStat { Season = s.Season, Team = team };
                player.SeasonStats.Add(stat);
                changed = true;
            }
            if (!kept.Add(stat)) continue;

            if (stat.IsSeasonTotal != s.IsSeasonTotal || stat.Games != s.Games || stat.MinutesPerGame != s.MinutesPerGame
                || stat.PointsPerGame != s.PointsPerGame || stat.ReboundsPerGame != s.ReboundsPerGame
                || stat.AssistsPerGame != s.AssistsPerGame || stat.StatsJson != s.StatsJson)
            {
                stat.IsSeasonTotal = s.IsSeasonTotal;
                stat.Games = s.Games;
                stat.MinutesPerGame = s.MinutesPerGame;
                stat.PointsPerGame = s.PointsPerGame;
                stat.ReboundsPerGame = s.ReboundsPerGame;
                stat.AssistsPerGame = s.AssistsPerGame;
                stat.StatsJson = s.StatsJson;
                changed = true;
            }
        }

        // Lines the record no longer has, such as a roster import's guess at a season it now covers.
        var covered = hp.Seasons.Select(s => s.Season).ToHashSet();
        foreach (var stale in player.SeasonStats.Where(x => covered.Contains(x.Season) && !kept.Contains(x)).ToList())
        {
            player.SeasonStats.Remove(stale);
            changed = true;
        }
        return changed;
    }

    private static bool SameTeam(PlayerSeasonStat stat, Team? team) =>
        team is null ? stat.Team is null && stat.TeamId is null : ReferenceEquals(stat.Team, team) || (team.Id != 0 && stat.TeamId == team.Id);

    private static bool SyncAwards(Player player, HistoryPlayer hp)
    {
        var changed = false;
        var wanted = hp.Awards.Select(a => (a.Season, a.Type)).ToHashSet();

        foreach (var stale in player.Awards.Where(a => !wanted.Contains((a.Season, a.Type))).ToList())
        {
            player.Awards.Remove(stale);
            changed = true;
        }

        var have = player.Awards.Select(a => (a.Season, a.Type)).ToHashSet();
        foreach (var (season, type) in wanted.Where(w => !have.Contains(w)))
        {
            player.Awards.Add(new Award { Season = season, Type = type });
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Replaces the player's stints with the ones the record shows, then reopens or adds the
    /// spell with the current team, which the record may not have reached yet.
    /// </summary>
    private static bool SyncStints(Player player, IReadOnlyList<StintSpan> spans, Dictionary<string, Team> teams, int latestSeason)
    {
        var wanted = spans
            .Select(s => (Team: teams[s.TeamKey], s.StartSeason, EndSeason: (int?)s.EndSeason, s.Seasons))
            .ToList();

        if (player.IsActive && player.CurrentTeam is { } current)
        {
            if (wanted.Count > 0 && ReferenceEquals(wanted[^1].Team, current))
            {
                wanted[^1] = wanted[^1] with { EndSeason = null };
            }
            else
            {
                var open = player.Stints.FirstOrDefault(s => s.EndSeason is null && SameTeam(s, current));
                var start = open?.StartSeason ?? latestSeason + 1;
                if (wanted.Count > 0) start = Math.Max(start, wanted[^1].EndSeason ?? start);
                wanted.Add((current, start, null, 0));
            }
        }

        var changed = false;
        var have = player.Stints.OrderBy(s => s.Order).ToList();
        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            if (i >= have.Count)
            {
                player.Stints.Add(new PlayerTeamStint { Team = w.Team, StartSeason = w.StartSeason, EndSeason = w.EndSeason, Seasons = w.Seasons, Order = i + 1 });
                changed = true;
                continue;
            }

            var stint = have[i];
            if (!SameTeam(stint, w.Team) || stint.StartSeason != w.StartSeason || stint.EndSeason != w.EndSeason
                || stint.Seasons != w.Seasons || stint.Order != i + 1)
            {
                stint.Team = w.Team;
                stint.StartSeason = w.StartSeason;
                stint.EndSeason = w.EndSeason;
                stint.Seasons = w.Seasons;
                stint.Order = i + 1;
                changed = true;
            }
        }

        foreach (var extra in have.Skip(wanted.Count))
        {
            player.Stints.Remove(extra);
            changed = true;
        }
        return changed;
    }

    private static bool SameTeam(PlayerTeamStint stint, Team team) =>
        ReferenceEquals(stint.Team, team) || (team.Id != 0 && stint.TeamId == team.Id);

    private static CareerSummary Summarise(HistoryPlayer hp, bool isActive, int latestSeason)
    {
        var totals = hp.Seasons.Where(s => s.IsSeasonTotal).ToList();
        var careerPoints = totals.Sum(s => (s.PointsPerGame ?? 0) * (s.Games ?? 0));
        double? recent = isActive
            ? totals.Where(s => s.Season >= latestSeason - 1).Select(s => s.PointsPerGame ?? 0).DefaultIfEmpty(0).Max()
            : null;

        int Count(Func<string, bool> match) => hp.Awards.Count(a => match(a.Type));

        return new CareerSummary(
            CareerPoints: careerPoints,
            AllStarSelections: Count(t => t == AwardTypes.AllStar),
            MvpAwards: Count(t => t == AwardTypes.Mvp),
            AllLeagueSelections: Count(t => t.StartsWith(AwardTypes.AllLeague, StringComparison.Ordinal)),
            OtherAwards: Count(t => t is AwardTypes.DefensivePlayer or AwardTypes.Rookie or AwardTypes.SixthMan),
            HallOfFame: hp.HallOfFame,
            RecentPointsPerGame: recent);
    }
}

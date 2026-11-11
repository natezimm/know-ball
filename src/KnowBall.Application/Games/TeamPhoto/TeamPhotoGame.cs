using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using KnowBall.Domain;

namespace KnowBall.Application.Games.TeamPhoto;

/// <summary>The team on the plaque. <see cref="LogoUrl"/> is null for a team that no longer exists under that name.</summary>
public sealed record PhotoTeam(string City, string Name, string Abbreviation, string? LogoUrl);

/// <summary>A blank spot in the photo: what the player sees before the name is found.</summary>
/// <param name="Row">"front" for the guards, "back" for the forwards and centres.</param>
public sealed record PhotoSlot(string Position, string Row, int Games, double Points, double Rebounds, double Assists);

/// <param name="Season">Start year: 2015 is 2015-16.</param>
/// <param name="Wins">The regular-season record, when the importer has it.</param>
/// <param name="Finals">"won" or "lost" for a Finals team; null for a practice team that did not get there.</param>
public sealed record PhotoPayload(PhotoTeam Team, int Season, int? Wins, int? Losses, string? Finals, IReadOnlyList<PhotoSlot> Slots);

public sealed record PhotoAnswer(int PlayerId, string Name, string? HeadshotUrl);

/// <param name="Key">The team-season, "2015-GSW", which keeps it from coming round again too soon.</param>
/// <param name="Slots">The starters, in the same order as the payload's slots.</param>
/// <param name="Bench">Everyone else who played for the team that season: naming one is not a miss.</param>
public sealed record PhotoSecret(string Key, IReadOnlyList<PhotoAnswer> Slots, IReadOnlyList<int> Bench);

/// <summary>A team in the daily pool. <see cref="Swap"/> replaces a starter by Basketball-Reference id, with the reason in <see cref="Why"/>.</summary>
public sealed record FinalsTeam(int Season, string Team, string Finals, IReadOnlyDictionary<string, string>? Swap = null, string? Why = null)
{
    public string Key => $"{Season}-{Team}";
}

/// <summary>One player's line for one team in one season: what a lineup is worked out from.</summary>
public sealed record RosterLine(int PlayerId, int Games, double Minutes, int? Started, string? Position, double Points, double Rebounds, double Assists);

/// <summary>
/// A blank team photo of one team-season: name its five starters. Starters are free and so are
/// bench players; four names that were not on the team end the round. Daily puzzles are the teams
/// that reached the Finals since the 1976 merger; practice draws from every playoff team since
/// 1981-82 whose starting five is clear.
/// </summary>
public sealed class TeamPhotoGame(ITriviaDb db) : IGame
{
    public const string GameSlug = "team-photo";
    public const int Starters = 5;
    /// <summary>Games started were first recorded in 1981-82; practice rounds start there.</summary>
    public const int FirstStartsSeason = 1981;
    /// <summary>A practice lineup is clear when the fifth starter started this many games...</summary>
    public const int ClearMinStarts = 30;
    /// <summary>...and this many more than the sixth.</summary>
    public const int ClearMargin = 10;

    private static readonly Lazy<IReadOnlyList<FinalsTeam>> Pool = new(LoadFinalsTeams);

    /// <summary>Every team that reached the Finals since the merger, champion and runner-up, from the embedded list.</summary>
    public static IReadOnlyList<FinalsTeam> FinalsTeams => Pool.Value;

    public GameType Type => GameType.TeamPhoto;
    public string Slug => GameSlug;
    public string Name => "Team Photo";
    /// <summary>Misses allowed; right answers and bench players are free.</summary>
    public int MaxGuesses => 4;
    public bool LimitsMisses => true;
    /// <summary>Repeats are by team-season, which only the secret records.</summary>
    public bool ReadsRecentSecrets => true;
    public bool IsGenerated => true;
    public bool IncludesRetired => true;

    public int AnswersIn(Puzzle puzzle) => GameJson.Read<PhotoSecret>(puzzle.SecretJson).Slots.Count;

    public async Task<GeneratedPuzzle?> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
    {
        var teams = await db.Teams.AsNoTracking().Where(t => t.LeagueId == context.League.Id).ToListAsync(cancellationToken);
        return context.Practice
            ? await PracticeAsync(context, teams, cancellationToken)
            : await DailyAsync(context, teams, cancellationToken);
    }

    public Task<GuessEvaluation> EvaluateAsync(Puzzle puzzle, Player guess, CancellationToken cancellationToken)
    {
        var secret = GameJson.Read<PhotoSecret>(puzzle.SecretJson);
        for (var i = 0; i < secret.Slots.Count; i++)
        {
            if (secret.Slots[i].PlayerId == guess.Id)
                return Task.FromResult(new GuessEvaluation(true, new JsonObject { ["slot"] = i }));
        }
        return Task.FromResult(secret.Bench.Contains(guess.Id)
            ? new GuessEvaluation(false, new JsonObject { ["bench"] = true }, Miss: false)
            : new GuessEvaluation(false));
    }

    /// <summary>Everything the photo shows is in the payload from the start.</summary>
    public JsonNode? Clues(Puzzle puzzle, int misses, bool complete) => null;

    public JsonNode Reveal(Puzzle puzzle) =>
        new JsonObject { ["slots"] = GameJson.Node(GameJson.Read<PhotoSecret>(puzzle.SecretJson).Slots) };

    /// <summary>
    /// A Finals team not used in the recent window, chosen at random. Once every team has been used the one
    /// shown longest ago comes round again, so the pool cycles rather than running dry.
    /// </summary>
    private async Task<GeneratedPuzzle?> DailyAsync(GenerationContext context, List<Team> teams, CancellationToken ct)
    {
        var lastUsed = new Dictionary<string, int>();
        var recent = context.RecentSecrets ?? [];
        for (var i = 0; i < recent.Count; i++)
        {
            if (KeyOf(recent[i]) is { } key) lastUsed[key] = i;
        }

        var fresh = FinalsTeams.Where(f => !lastUsed.ContainsKey(f.Key)).OrderBy(_ => context.Random.Next()).ToList();
        var stale = FinalsTeams.Where(f => lastUsed.ContainsKey(f.Key)).OrderBy(f => lastUsed[f.Key]);
        foreach (var entry in fresh.Concat(stale))
        {
            if (FindTeam(teams, entry.Team, entry.Season) is not { } team) continue;
            var puzzle = await BuildAsync(team, entry.Season, entry.Finals, entry.Swap, ct);
            if (puzzle is not null) return puzzle;
        }
        return null;
    }

    /// <summary>A playoff team since 1981-82 with a clear starting five, weighted towards famous lineups. Finals teams are left to the dailies.</summary>
    private async Task<GeneratedPuzzle?> PracticeAsync(GenerationContext context, List<Team> teams, CancellationToken ct)
    {
        var current = context.AsOf.Month >= 10 ? context.AsOf.Year : context.AsOf.Year - 1;
        var finals = FinalsTeams
            .Select(f => FindTeam(teams, f.Team, f.Season) is { } t ? (t.Id, f.Season) : (0, 0))
            .ToHashSet();
        var playoffs = (await db.TeamSeasons.AsNoTracking()
                .Where(t => t.Team!.LeagueId == context.League.Id && t.Season >= FirstStartsSeason)
                .Select(t => new { t.TeamId, t.Season, t.MadePlayoffs })
                .ToListAsync(ct))
            .ToDictionary(t => (t.TeamId, t.Season), t => t.MadePlayoffs);

        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Where(s => s.TeamId != null && s.Season >= FirstStartsSeason && s.Season < current && s.Player!.LeagueId == context.League.Id)
            .Select(s => new { s.PlayerId, TeamId = s.TeamId!.Value, s.Season, s.Games, s.MinutesPerGame, s.StatsJson, s.Player!.Popularity })
            .ToListAsync(ct);

        var candidates = new List<((int TeamId, int Season) Key, double Fame)>();
        foreach (var group in rows.GroupBy(r => (r.TeamId, r.Season)))
        {
            if (finals.Contains(group.Key)) continue;
            // Before the importer has team records every team-season qualifies; after, only playoff teams.
            if (playoffs.Count > 0 && !playoffs.GetValueOrDefault(group.Key)) continue;

            var lines = group.Select(r => Line(r.PlayerId, r.Games, r.MinutesPerGame, r.StatsJson, null, 0, 0, 0)).ToList();
            if (!IsClear(lines)) continue;
            var five = Lineup(lines, null).Select(l => l.PlayerId).ToHashSet();
            candidates.Add((group.Key, group.Where(r => five.Contains(r.PlayerId)).Sum(r => r.Popularity)));
        }
        if (candidates.Count == 0) return null;

        var pick = WeightedPick.From(candidates.OrderBy(c => c.Key).ToList(), c => c.Fame * c.Fame, context.Random).Key;
        return await BuildAsync(teams.First(t => t.Id == pick.TeamId), pick.Season, null, null, ct);
    }

    private async Task<GeneratedPuzzle?> BuildAsync(Team team, int season, string? finals, IReadOnlyDictionary<string, string>? swap, CancellationToken ct)
    {
        var rows = await db.PlayerSeasonStats.AsNoTracking()
            .Include(s => s.Player)
            .Where(s => s.TeamId == team.Id && s.Season == season)
            .ToListAsync(ct);
        if (rows.Count < Starters) return null;

        Dictionary<int, int>? swaps = null;
        if (swap is { Count: > 0 })
        {
            var ids = swap.Keys.Concat(swap.Values).ToList();
            var byRef = await db.PlayerExternalIds.AsNoTracking()
                .Where(x => x.Source == "bref" && ids.Contains(x.Value))
                .ToDictionaryAsync(x => x.Value, x => x.PlayerId, ct);
            swaps = swap
                .Where(s => byRef.ContainsKey(s.Key) && byRef.ContainsKey(s.Value))
                .ToDictionary(s => byRef[s.Key], s => byRef[s.Value]);
        }

        var lines = rows.Select(r => Line(r.PlayerId, r.Games, r.MinutesPerGame, r.StatsJson, r.Player!.Position,
            r.PointsPerGame ?? 0, r.ReboundsPerGame ?? 0, r.AssistsPerGame ?? 0)).ToList();
        var five = Order(Lineup(lines, swaps));
        if (five.Count < Starters) return null;

        var players = rows.ToDictionary(r => r.PlayerId, r => r.Player!);
        var record = await db.TeamSeasons.AsNoTracking().FirstOrDefaultAsync(t => t.TeamId == team.Id && t.Season == season, ct);
        var payload = new PhotoPayload(
            new PhotoTeam(team.City, team.Name, team.Abbreviation, team.LogoUrl),
            season,
            record?.Wins,
            record?.Losses,
            finals,
            five.Select(l => new PhotoSlot(l.Position ?? "", RowOf(l.Position), l.Games, Math.Round(l.Points, 1), Math.Round(l.Rebounds, 1), Math.Round(l.Assists, 1))).ToList());
        var starters = five.Select(l => l.PlayerId).ToHashSet();
        var secret = new PhotoSecret(
            $"{season}-{team.Abbreviation}",
            five.Select(l => new PhotoAnswer(l.PlayerId, players[l.PlayerId].FullName, players[l.PlayerId].HeadshotUrl)).ToList(),
            players.Keys.Where(id => !starters.Contains(id)).Order().ToList());

        // Older teams and lesser-known lineups are harder.
        var fame = five.Average(l => players[l.PlayerId].Popularity);
        var difficulty = (int)Math.Clamp(Math.Round(100 - fame + Math.Max(0, 2000 - season) / 2.0), 0, 100);
        return new GeneratedPuzzle(null, GameJson.Write(payload), GameJson.Write(secret), difficulty);
    }

    /// <summary>
    /// The five who started the most games, by minutes played on a tie. A season from before games started were
    /// recorded falls back to the five who played the most minutes. Then any swaps from the curated list.
    /// </summary>
    internal static List<RosterLine> Lineup(IReadOnlyList<RosterLine> lines, IReadOnlyDictionary<int, int>? swaps)
    {
        var ordered = Ranked(lines);
        var five = ordered.Take(Starters).ToList();
        foreach (var (outId, inId) in swaps ?? new Dictionary<int, int>())
        {
            var at = five.FindIndex(l => l.PlayerId == outId);
            var incoming = lines.FirstOrDefault(l => l.PlayerId == inId);
            if (at >= 0 && incoming is not null && five.All(l => l.PlayerId != inId)) five[at] = incoming;
        }
        return five;
    }

    /// <summary>True when the fifth starter started at least <see cref="ClearMinStarts"/> games and <see cref="ClearMargin"/> more than the sixth.</summary>
    internal static bool IsClear(IReadOnlyList<RosterLine> lines)
    {
        if (!HasStarts(lines)) return false;
        var ordered = Ranked(lines);
        if (ordered.Count < Starters) return false;
        var fifth = ordered[Starters - 1].Started ?? 0;
        var sixth = ordered.Count > Starters ? ordered[Starters].Started ?? 0 : 0;
        return fifth >= ClearMinStarts && fifth - sixth >= ClearMargin;
    }

    /// <summary>Back row first (small forward, power forward, centre), then the guards in front (point, then shooting).</summary>
    internal static List<RosterLine> Order(IEnumerable<RosterLine> five) =>
        five.OrderBy(l => RowOf(l.Position) == "front" ? 1 : 0)
            .ThenBy(l => Rank(l.Position))
            .ToList();

    /// <summary>"front" for a guard ("PG", "SG", "G", "G-F"), "back" for anyone else.</summary>
    internal static string RowOf(string? position) => Primary(position) is "PG" or "SG" or "G" ? "front" : "back";

    private static int Rank(string? position) => Primary(position) switch
    {
        "SF" or "F" => 0,
        "PF" => 1,
        "C" => 2,
        "PG" or "G" => 0,
        "SG" => 1,
        _ => 1,
    };

    private static string Primary(string? position) => (position ?? "").Split('-')[0].Trim().ToUpperInvariant();

    private static List<RosterLine> Ranked(IReadOnlyList<RosterLine> lines) =>
        (HasStarts(lines)
            ? lines.OrderByDescending(l => l.Started ?? 0).ThenByDescending(l => l.Games * l.Minutes)
            : lines.OrderByDescending(l => l.Games * l.Minutes))
        .ThenBy(l => l.PlayerId)
        .ToList();

    /// <summary>Whether starts were recorded for everyone who played a real part of the season (20 games).</summary>
    private static bool HasStarts(IReadOnlyList<RosterLine> lines) =>
        lines.Where(l => l.Games >= 20).All(l => l.Started is not null) && lines.Any(l => l.Started is not null);

    private static RosterLine Line(int playerId, int? games, double? minutes, string statsJson, string? careerPosition, double points, double rebounds, double assists)
    {
        int? started = null;
        string? position = null;
        if (!string.IsNullOrWhiteSpace(statsJson) && JsonNode.Parse(statsJson) is JsonObject json)
        {
            if (json["gs"] is JsonValue gs && gs.TryGetValue<double>(out var value)) started = (int)value;
            if (json["pos"] is JsonValue pos && pos.TryGetValue<string>(out var text)) position = text;
        }
        return new RosterLine(playerId, games ?? 0, minutes ?? 0, started, position ?? careerPosition, points, rebounds, assists);
    }

    /// <summary>The team under that abbreviation in that season: former names ("SEA") keep their own abbreviation.</summary>
    private static Team? FindTeam(List<Team> teams, string abbreviation, int season) =>
        teams.FirstOrDefault(t => t.Abbreviation == abbreviation
                                  && (t.ActiveFrom is null || t.ActiveFrom <= season)
                                  && (t.ActiveTo is null || t.ActiveTo >= season));

    private static string? KeyOf(string secretJson)
    {
        try
        {
            return JsonNode.Parse(secretJson)?["key"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<FinalsTeam> LoadFinalsTeams()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("finals-teams.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<List<FinalsTeam>>(stream, GameJson.Options)
               ?? throw new InvalidOperationException("finals-teams.json is empty");
    }
}

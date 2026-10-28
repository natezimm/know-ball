using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using KnowBall.Application.Games;
using KnowBall.Application.Games.ScoutingReport;
using KnowBall.Application.Games.CareerPath;
using KnowBall.Application.Players;
using Xunit;

namespace KnowBall.UnitTests;

public class PlayerGameRecoveryTests
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    [Fact]
    public async Task Saved_roster_survives_trade_and_retirement()
    {
        using var world = new GameWorld();
        using var engine = world.NewEngine();
        var state = (await engine.Service.GetDailyAsync("scouting-report", "snapshot-user", default)).Result!;
        var puzzle = await engine.Db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
        var secret = GameJson.Read<ScoutingSecret>(puzzle.SecretJson);
        var answer = await engine.Db.Players.SingleAsync(p => p.Id == secret.PlayerId);
        answer.CurrentTeamId = world.Teams.Values.First(t => t.Abbreviation != secret.Profile.Team).Id;
        answer.IsActive = false;
        await engine.Db.SaveChangesAsync();
        using var request = world.NewEngine();
        var rows = await new PlayerSearchService(request.Db, world.Clock).SearchAsync(answer.FullName, puzzleId: puzzle.Id);
        Require(rows.Any(p => p.Id == answer.Id && p.TeamAbbreviation == secret.Profile.Team), "Saved answer must remain searchable with its saved team");
        var won = (await request.Service.GuessAsync("scouting-report", puzzle.Id, answer.Id, "snapshot-user", default)).Result!;
        Require(won.Status == "won", "Answer should win");
        Require(won.Guesses.Last().Feedback!.AsObject().All(c => c.Value!["state"]!.GetValue<string>() == "ok"), "Winning clues must agree");
        var peer = secret.Roster!.First(p => p.Key != answer.Id);
        var peerEntity = await request.Db.Players.SingleAsync(p => p.Id == peer.Key);
        peerEntity.CurrentTeamId = answer.CurrentTeamId;
        peerEntity.JerseyNumber = "99";
        await request.Db.SaveChangesAsync();
        var feedback = await new ScoutingReportGame(request.Db).EvaluateAsync(puzzle, peerEntity, default);
        Require(feedback.Feedback!["number"]!["value"]!.GetValue<string>() == peer.Value.Number, "Other guesses must use saved profiles too");
    }

    [Fact]
    public async Task Guess_receipts_recover_wins_and_reject_reuse()
    {
        using var world = new GameWorld();
        foreach (var slug in new[] { "scouting-report", "career-path" })
        {
            using var engine = world.NewEngine();
            var state = (await engine.Service.GetDailyAsync(slug, "retry-user", default)).Result!;
            var puzzle = await engine.Db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
            var id = Guid.NewGuid();
            var won = await engine.Service.GuessAsync(slug, puzzle.Id, puzzle.AnswerPlayerId!.Value, "retry-user", default, id);
            var replay = await engine.Service.GuessAsync(slug, puzzle.Id, puzzle.AnswerPlayerId.Value, "retry-user", default, id);
            Require(won.Ok && replay.Ok && replay.Result!.Status == "won" && replay.Result.Guesses.Count == 1, "Retry must recover the saved win");
            var other = world.Players.Values.First(p => p.Id != puzzle.AnswerPlayerId);
            Require(!(await engine.Service.GuessAsync(slug, puzzle.Id, other.Id, "retry-user", default, id)).Ok, "A receipt cannot be reused for another player");
        }
    }

    [Fact]
    public async Task Concurrent_writes_preserve_both_guesses()
    {
        using var world = new GameWorld();
        foreach (var slug in new[] { "scouting-report", "career-path" })
        {
            using var initial = world.NewEngine();
            var state = (await initial.Service.GetDailyAsync(slug, "two-tabs", default)).Result!;
            var puzzle = await initial.Db.Puzzles.SingleAsync(p => p.Id == state.PuzzleId);
            var accepted = slug == "career-path" ? GameJson.Read<CareerSecret>(puzzle.SecretJson).Accepted.ToHashSet() : new HashSet<int> { puzzle.AnswerPlayerId!.Value };
            var wrong = world.Players.Values.Where(p => !accepted.Contains(p.Id) && p.IsActive).Take(3).ToArray();
            Require(wrong.Length == 3, "Fixture needs three eligible misses");
            await initial.Service.GuessAsync(slug, puzzle.Id, wrong[0].Id, "two-tabs", default);
            var pause = new PauseUpdate();
            using var db = world.Db.NewContext(pause);
            var games = new GameRegistry([new ScoutingReportGame(db), new CareerPathGame(db)]);
            var calendar = new PuzzleCalendar(world.Clock);
            var scheduler = new PuzzleScheduler(db, calendar, world.Clock, NullLogger<PuzzleScheduler>.Instance);
            var first = new GameService(db, games, scheduler, calendar, world.Clock)
                .GuessAsync(slug, puzzle.Id, wrong[1].Id, "two-tabs", default, Guid.NewGuid());
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                using var second = world.NewEngine();
                Require((await second.Service.GuessAsync(slug, puzzle.Id, wrong[2].Id, "two-tabs", default, Guid.NewGuid())).Ok, "Second guess failed");
            }
            finally { pause.Release.TrySetResult(); }
            Require((await first).Ok, "First guess must retry its stale update");
            using var read = world.NewEngine();
            var saved = (await read.Service.GetPuzzleAsync(slug, puzzle.Id, "two-tabs", default)).Result!;
            Require(saved.Guesses.Count == 3, "A concurrent guess was lost");
        }
    }

    private sealed class PauseUpdate : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool paused;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!paused && command.CommandText.StartsWith("UPDATE"))
            {
                paused = true; Reached.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }
}

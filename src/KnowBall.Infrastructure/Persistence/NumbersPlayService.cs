using System.Text.Json;
using System.Text.Json.Nodes;
using KnowBall.Application.Numbers;
using KnowBall.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.Infrastructure.Persistence;

public sealed record NumbersAnswer(string QuestionId, string OptionId, Guid RequestId);
public sealed record NumbersMove(string QuestionId, string? OptionId, Guid RequestId, int ExpectedRevision);
public sealed record NumbersStart(string[] Leagues, string[] Topics, int? FromYear, int? ThroughYear, Guid RequestId);
public sealed class NumbersFailure(int status, string message) : Exception(message) { public int Status => status; }

public sealed class NumbersPlayService(TriviaDbContext db, TimeProvider clock)
{
    private static readonly Lazy<string> Content = new(() => {
        using var stream = typeof(NumbersPlayService).Assembly.GetManifestResourceStream("KnowBall.Infrastructure.Data.numbers-beta.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);

    public async Task<object> Daily(string owner, CancellationToken ct)
    {
        var date = Today;
        var key = date.ToString("yyyy-MM-dd");
        var shared = await db.NumbersPlays.AsNoTracking().SingleOrDefaultAsync(x => x.Owner == "_daily" && x.StartKey == key, ct);
        if (shared is null)
        {
            shared = await Create(new NumbersPlay { Id = Guid.NewGuid(), Owner = "_daily", StartKey = key, Date = date,
                SecretJson = Select(new(QuizCatalogue.Leagues, QuizCatalogue.Topics), key) }, ct);
        }
        var play = await db.NumbersPlays.AsNoTracking().SingleOrDefaultAsync(x => x.Owner == owner && x.StartKey == key, ct)
            ?? await Create(new NumbersPlay { Id = Guid.NewGuid(), Owner = owner, StartKey = key, Date = date, SecretJson = shared.SecretJson }, ct);
        return View(play);
    }

    public async Task<object> Start(string owner, NumbersStart request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Leagues is null || request.Topics is null ||
            request.Leagues.Length > 4 || request.Topics.Length > 4 || request.Leagues.Length == 0 || request.Topics.Length == 0 ||
            request.Leagues.Any(l => !QuizCatalogue.Leagues.Contains(l)) || request.Topics.Any(t => !QuizCatalogue.Topics.Contains(t)) ||
            request.FromYear > request.ThroughYear) throw new NumbersFailure(400, "Select supported leagues, topics and an ordered year range.");
        var filters = new QuizFilters(request.Leagues.Distinct().Order().ToArray(), request.Topics.Distinct().Order().ToArray(), request.FromYear, request.ThroughYear);
        var signature = JsonSerializer.Serialize(filters);
        var key = request.RequestId.ToString();
        var existing = await db.NumbersPlays.AsNoTracking().SingleOrDefaultAsync(x => x.Owner == owner && x.StartKey == key, ct);
        if (existing is not null)
        {
            if (existing.Filters != signature) throw new NumbersFailure(409, "This start request was already used with other filters.");
            return View(existing);
        }
        string secret;
        try { secret = Select(filters, key); }
        catch (InvalidOperationException) { throw new NumbersFailure(503, "Not enough beta questions for those filters. Add topics or widen the years."); }
        var play = await Create(new NumbersPlay { Id = Guid.NewGuid(), Owner = owner, StartKey = key, Filters = signature,
            ExpiresAt = clock.GetUtcNow().AddDays(30), SecretJson = secret }, ct);
        if (play.Filters != signature) throw new NumbersFailure(409, "This start request was already used with other filters.");
        return View(play);
    }

    public async Task<object> Get(string owner, Guid id, CancellationToken ct) => View(await Find(owner, id, ct));

    public async Task<object> Move(string owner, Guid id, NumbersMove move, bool advance, CancellationToken ct)
    {
        if (move.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(move.QuestionId)) throw new NumbersFailure(400, "A question and request ID are required.");
        var play = await Find(owner, id, ct);
        var questions = JsonSerializer.Deserialize<JsonElement[]>(play.SecretJson)!;
        var answers = JsonSerializer.Deserialize<List<NumbersAnswer>>(play.AnswersJson)!;
        if (!advance)
        {
            var receipt = answers.SingleOrDefault(a => a.RequestId == move.RequestId);
            if (receipt is not null)
            {
                if (receipt.QuestionId != move.QuestionId || receipt.OptionId != move.OptionId) throw new NumbersFailure(409, "Request ID already used.");
                return View(play);
            }
        }
        // Continue is keyed by answered question, so repeated acknowledgement cannot skip a question.
        var questionIndex = Array.FindIndex(questions, q => Text(q, "id") == move.QuestionId);
        if (advance && questionIndex >= 0 && questionIndex < play.Position) return View(play);
        if (play.Revision != move.ExpectedRevision || play.Position >= 5 || questionIndex != play.Position)
            throw new NumbersFailure(409, "Progress changed. Reload the saved round.");
        if (advance)
        {
            if (answers.Count != play.Position + 1) throw new NumbersFailure(409, "Answer this question first.");
            play.Position++;
        }
        else
        {
            if (answers.Count != play.Position) throw new NumbersFailure(409, "This question was already answered.");
            if (!questions[play.Position].GetProperty("options").EnumerateArray().Any(o => Text(o, "id") == move.OptionId))
                throw new NumbersFailure(400, "Choose one of the offered answers.");
            answers.Add(new(move.QuestionId, move.OptionId!, move.RequestId));
            play.AnswersJson = JsonSerializer.Serialize(answers);
        }
        play.Revision++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new NumbersFailure(409, "Progress changed. Reload the saved round."); }
        return View(play);
    }

    private async Task<NumbersPlay> Find(string owner, Guid id, CancellationToken ct)
    {
        var play = await db.NumbersPlays.SingleOrDefaultAsync(x => x.Id == id && x.Owner == owner, ct);
        if (play is null || play.Date > Today || play.ExpiresAt <= clock.GetUtcNow())
            throw new NumbersFailure(404, "Round unavailable or expired.");
        return play;
    }
    private async Task<NumbersPlay> Create(NumbersPlay play, CancellationToken ct)
    {
        db.NumbersPlays.Add(play);
        try { await db.SaveChangesAsync(ct); return play; }
        catch (DbUpdateException)
        {
            db.Entry(play).State = EntityState.Detached;
            return await db.NumbersPlays.AsNoTracking().SingleOrDefaultAsync(x => x.Owner == play.Owner && x.StartKey == play.StartKey, ct)
                ?? throw new NumbersFailure(409, "Round creation conflicted. Retry.");
        }
    }
    private static string Select(QuizFilters filters, string seed)
    {
        using var catalogue = QuizCatalogue.Read(Content.Value);
        var set = QuizSelection.Select(catalogue.RootElement, filters, seed, requireApproved: false);
        var nodes = set.Select(q => JsonNode.Parse(q.GetRawText())!).ToArray();
        for (var i = 0; i < nodes.Length; i++)
        {
            var options = nodes[i]["options"]!.AsArray().Select(o => o!.DeepClone()).ToArray();
            // Opaque option IDs prevent the beta catalogue's original p0 convention leaking answers.
            var correct = nodes[i]["correctOptionId"]!.GetValue<string>();
            foreach (var option in options) {
                var old = option["id"]!.GetValue<string>();
                var opaque = Guid.NewGuid().ToString("N"); option["id"] = opaque;
                if (old == correct) nodes[i]["correctOptionId"] = opaque;
            }
            Random.Shared.Shuffle(options);
            nodes[i]["options"] = new JsonArray(options);
        }
        return JsonSerializer.Serialize(nodes);
    }
    private object View(NumbersPlay play)
    {
        if (play.ExpiresAt <= clock.GetUtcNow()) throw new NumbersFailure(404, "Practice expired. Start another round.");
        var qs = JsonSerializer.Deserialize<JsonElement[]>(play.SecretJson)!;
        var answers = JsonSerializer.Deserialize<List<NumbersAnswer>>(play.AnswersJson)!;
        var review = answers.Select((a, i) => new { question = qs[i], a.OptionId,
            correct = a.OptionId == Text(qs[i], "correctOptionId") }).ToArray();
        object? current = null;
        if (play.Position < 5) {
            var q = qs[play.Position];
            current = new { id = Text(q,"id"), prompt = Text(q,"prompt"), league = Text(q,"league"), topic = Text(q,"topic"),
                season = Text(q,"topic") == "seasons" ? null : Text(q,"season"),
                options = q.GetProperty("options").EnumerateArray().Select(o => new { id = Text(o,"id"), label = Text(o,"label") }).ToArray() };
        }
        return new { play.Id, play.Date, mode = play.Date.HasValue ? "daily" : "practice", play.Position, play.Revision,
            answeredCount = answers.Count, correctCount = review.Count(a => a.correct), current, review, complete = play.Position == 5 };
    }
    private static string Text(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}

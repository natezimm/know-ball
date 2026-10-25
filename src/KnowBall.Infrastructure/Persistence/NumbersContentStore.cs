using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowBall.Application.Numbers;
using KnowBall.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.Infrastructure.Persistence;

public sealed class NumbersContentStore(TriviaDbContext db, TimeProvider clock)
{
    public async Task<NumbersCatalogue> ImportAsync(string json, CancellationToken ct = default)
    {
        using var doc = QuizCatalogue.Read(json);
        var errors = QuizCatalogue.Validate(doc.RootElement, requireApproved: true);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var canonical = Canonical(doc.RootElement).ToJsonString();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var existing = await db.NumbersCatalogues.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (existing is not null) return existing;
        var entity = new NumbersCatalogue { Id = id, ContentJson = canonical, ImportedAt = clock.GetUtcNow() };
        db.NumbersCatalogues.Add(entity);
        try { await db.SaveChangesAsync(ct); return entity; }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;
            var winner = await db.NumbersCatalogues.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
            if (winner is null) throw;
            return winner;
        }
    }

    // Explicit staging primitive. Publication and queue scheduling are separate future operations.
    public async Task<NumbersQuiz> StageDailyAsync(string catalogueId, DateOnly date, CancellationToken ct = default)
    {
        var existing = await db.NumbersQuizzes.AsNoTracking().SingleOrDefaultAsync(q => q.Date == date, ct);
        if (existing is not null) return existing;
        var catalogue = await db.NumbersCatalogues.AsNoTracking().SingleAsync(c => c.Id == catalogueId, ct);
        using var doc = QuizCatalogue.Read(catalogue.ContentJson);
        var questions = QuizSelection.Select(doc.RootElement,
            new(QuizCatalogue.Leagues, QuizCatalogue.Topics), $"selection-v1:{date:yyyy-MM-dd}");
        var entity = new NumbersQuiz {
            Id = Guid.NewGuid(), Date = date, CatalogueId = catalogueId,
            QuestionsJson = JsonSerializer.Serialize(questions), CreatedAt = clock.GetUtcNow()
        };
        db.NumbersQuizzes.Add(entity);
        try { await db.SaveChangesAsync(ct); return entity; }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;
            var winner = await db.NumbersQuizzes.AsNoTracking().SingleOrDefaultAsync(q => q.Date == date, ct);
            if (winner is null) throw;
            return winner;
        }
    }

    private static JsonNode Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => new JsonObject(value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new KeyValuePair<string, JsonNode?>(p.Name, Canonical(p.Value)))),
        JsonValueKind.Array => new JsonArray(value.EnumerateArray().Select(v => (JsonNode?)Canonical(v)).ToArray()),
        _ => JsonNode.Parse(value.GetRawText())!
    };
}

using System.Data;
using System.Text.Json;
using KnowBall.Application.Numbers;
using KnowBall.Domain;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.Infrastructure.Persistence;

public sealed class NumbersQueue(TriviaDbContext db, TimeProvider clock)
{
    public async Task<int> FillAsync(string catalogueId, DateOnly start, DateOnly launch, int days = 30, CancellationToken ct = default)
    {
        if (days is < 1 or > 120 || start < launch) throw new ArgumentOutOfRangeException(nameof(days));
        // PostgreSQL serializable isolation also detects adjacent-date insert races.
        // A failed competing run retries on the next invocation; no partial queue commits.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var catalogue = await db.NumbersCatalogues.AsNoTracking().SingleAsync(c => c.Id == catalogueId, ct);
        using var doc = QuizCatalogue.Read(catalogue.ContentJson);
        var end = start.AddDays(days - 1);
        var rows = await db.NumbersQuizzes.AsNoTracking()
            .Where(q => q.Date >= start.AddDays(-29) && q.Date <= end.AddDays(29)).ToListAsync(ct);
        var history = rows.Select(q => new ScheduledQuiz(q.Date, JsonSerializer.Deserialize<JsonElement[]>(q.QuestionsJson)!)).ToList();
        var pending = new List<NumbersQuiz>();
        for (var i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            if (history.Any(q => q.Date == date)) continue;
            var planned = QuizSchedule.Daily(doc.RootElement, date, launch, history);
            history.Add(planned);
            pending.Add(new NumbersQuiz {
                Id = Guid.NewGuid(), Date = date, CatalogueId = catalogueId,
                AlgorithmVersion = QuizSchedule.Version,
                QuestionsJson = JsonSerializer.Serialize(planned.Questions), CreatedAt = clock.GetUtcNow()
            });
        }
        db.NumbersQuizzes.AddRange(pending);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return pending.Count;
        }
        catch
        {
            foreach (var row in pending) db.Entry(row).State = EntityState.Detached;
            throw;
        }
    }
}

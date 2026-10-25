using KnowBall.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowBall.UnitTests;

public class NumbersContentStoreTests
{
    [Fact]
    public async Task Import_is_idempotent_and_snapshot_survives_new_context_and_revision()
    {
        using var fixture = new TestDb();
        string hash, frozen;
        Guid quizId;
        var day = new DateOnly(2026, 10, 25);
        await using (var db = fixture.NewContext())
        {
            var store = new NumbersContentStore(db, TimeProvider.System);
            var json = QuizCatalogueTests.Catalogue().ToJsonString();
            hash = (await store.ImportAsync(json)).Id;
            Assert.Equal(hash, (await store.ImportAsync(" \n" + json)).Id);
            var quiz = await store.StageDailyAsync(hash, day);
            frozen = quiz.QuestionsJson;
            quizId = quiz.Id;
            Assert.Equal(1, await db.NumbersCatalogues.CountAsync());
            Assert.Equal(1, await db.NumbersQuizzes.CountAsync());
        }
        await using (var db = fixture.NewContext())
        {
            var store = new NumbersContentStore(db, TimeProvider.System);
            var changed = QuizCatalogueTests.Catalogue();
            changed["questions"]![0]!["revision"] = 2;
            changed["questions"]![0]!["explanation"] = "Corrected editorial explanation.";
            var revision = await store.ImportAsync(changed.ToJsonString());
            Assert.NotEqual(hash, revision.Id);
            var saved = await store.StageDailyAsync(revision.Id, day);
            Assert.Equal(quizId, saved.Id);
            Assert.Equal(frozen, saved.QuestionsJson);
            Assert.Equal(hash, saved.CatalogueId);
        }
    }

    [Fact]
    public async Task Unapproved_import_and_short_round_leave_no_partial_data()
    {
        using var fixture = new TestDb();
        await using var db = fixture.NewContext();
        var store = new NumbersContentStore(db, TimeProvider.System);
        var root = QuizCatalogueTests.Catalogue();
        root["questions"]![0]!["publicationStatus"] = "prototype-only";
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(root.ToJsonString()));
        Assert.Equal(0, await db.NumbersCatalogues.CountAsync());
        root["questions"]![0]!["publicationStatus"] = "approved";
        var questions = root["questions"]!.AsArray();
        while (questions.Count > 1) questions.RemoveAt(questions.Count - 1);
        var catalogue = await store.ImportAsync(root.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageDailyAsync(catalogue.Id, new(2026, 10, 25)));
        Assert.Equal(0, await db.NumbersQuizzes.CountAsync());
    }

    [Fact]
    public async Task Frozen_content_rejects_updates_and_database_enforces_one_quiz_per_date()
    {
        using var fixture = new TestDb();
        await using var db = fixture.NewContext();
        var store = new NumbersContentStore(db, TimeProvider.System);
        var catalogue = await store.ImportAsync(QuizCatalogueTests.Catalogue().ToJsonString());
        var quiz = await store.StageDailyAsync(catalogue.Id, new(2026, 10, 25));
        quiz.QuestionsJson = "[]";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.NumbersQuizzes.Add(new() {
            Id = Guid.NewGuid(), Date = quiz.Date, CatalogueId = catalogue.Id,
            QuestionsJson = "[]", CreatedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

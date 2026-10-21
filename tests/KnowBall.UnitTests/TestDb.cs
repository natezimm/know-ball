using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using KnowBall.Infrastructure.Persistence;

namespace KnowBall.UnitTests;

/// <summary>An in-memory SQLite database shared by several contexts for one test.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TriviaDbContext> _options;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<TriviaDbContext>().UseSqlite(_connection).Options;
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    /// <summary>A fresh context, like a new request or a new nightly run.</summary>
    public TriviaDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<TriviaDbContext>(_options).AddInterceptors(interceptors).Options);

    public void Dispose() => _connection.Dispose();
}

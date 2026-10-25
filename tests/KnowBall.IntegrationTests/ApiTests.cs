using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Application.Import;
using KnowBall.Infrastructure;

namespace KnowBall.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway database, runs the seed import the same way the
/// nightly importer does, and checks the endpoints the Angular app calls.
/// Uses SQLite by default; set TRIVIA_TEST_POSTGRES to a connection string to test against Postgres.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{

    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"trivia-test-{Guid.NewGuid():N}.db");
    private readonly SemaphoreSlim _historyLock = new(1, 1);
    private bool _historyLoaded;

    public ApiFactory()
    {
        // Environment variables sit above appsettings.json, and Program reads them while
        // registering services, so set them before the host is built.
        var pg = Environment.GetEnvironmentVariable("TRIVIA_TEST_POSTGRES");
        Environment.SetEnvironmentVariable("Database__Provider", pg is null ? "Sqlite" : "Postgres");
        Environment.SetEnvironmentVariable("ConnectionStrings__Trivia", pg ?? $"Data Source={_sqlitePath}");
        Environment.SetEnvironmentVariable("Sources__Bref__Directory", FindFixtures());
    }

    /// <summary>
    /// Loads the player history from tests/fixtures/bref (trimmed copies of the real files) and then
    /// the seed roster on top, the order the nightly job uses. Done once for the whole test run.
    /// </summary>
    public async Task EnsureHistoryAsync()
    {
        await _historyLock.WaitAsync();
        try
        {
            if (_historyLoaded) return;
            await using var scope = Services.CreateAsyncScope();
            var history = await scope.ServiceProvider.GetRequiredService<HistoryImportService>()
                .RunAsync(scope.ServiceProvider.GetRequiredService<IHistorySource>());
            Assert.Equal(KnowBall.Domain.ImportStatus.Succeeded, history.Status);

            var roster = await scope.ServiceProvider.GetRequiredService<PlayerImportService>()
                .RunAsync(scope.ServiceProvider.GetRequiredService<PlayerSourceResolver>().Get("seed"));
            Assert.Equal(KnowBall.Domain.ImportStatus.Succeeded, roster.Status);
            _historyLoaded = true;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "bref");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/fixtures/bref");
    }

    // Every test request comes from the same (empty) address, so the site-wide limit would trip
    // partway through the run; RateLimitTests turns it back down where it is the subject.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment("Testing")
        .UseSetting("RateLimits:RequestsPerMinute", "0");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_sqlitePath); } catch { /* best effort */ }
    }
}

/// <summary>
/// One API instance for every test class. The factory configures the app through process-wide
/// environment variables, so two factories alive at once would trample each other's database path.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

[Collection(ApiCollection.Name)]
public sealed class ApiTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<PlayerSourceResolver>().Get("seed");
        var run = await scope.ServiceProvider.GetRequiredService<PlayerImportService>().RunAsync(source);
        Assert.Equal(KnowBall.Domain.ImportStatus.Succeeded, run.Status);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Search_finds_player_without_accents()
    {
        var results = await _client.GetFromJsonAsync<JsonElement>("/api/players/search?q=jokic");
        var first = results.EnumerateArray().First();
        Assert.Equal("Nikola Jokić", first.GetProperty("fullName").GetString());
        Assert.Equal("DEN", first.GetProperty("teamAbbreviation").GetString());
    }

    [Fact]
    public async Task Search_short_query_returns_empty_list()
    {
        var results = await _client.GetFromJsonAsync<JsonElement>("/api/players/search?q=l");
        Assert.Equal(0, results.GetArrayLength());
    }

    [Fact]
    public async Task Search_ranks_by_popularity()
    {
        var results = await _client.GetFromJsonAsync<JsonElement>("/api/players/search?q=le&limit=5");
        Assert.Equal("LeBron James", results.EnumerateArray().First().GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Latest_import_run_is_reported()
    {
        var run = await _client.GetFromJsonAsync<JsonElement>("/api/import-runs/latest");
        Assert.Equal("Succeeded", run.GetProperty("status").GetString());
        Assert.Equal("seed", run.GetProperty("source").GetString());
        Assert.Equal(30, run.GetProperty("teamsUpserted").GetInt32());
    }

    [Fact]
    public async Task Summary_counts_teams_and_players()
    {
        var s = await _client.GetFromJsonAsync<JsonElement>("/api/stats/summary");
        // 30 from the seed alone; more once another test class has loaded the history and its former teams.
        Assert.True(s.GetProperty("teams").GetInt32() >= 30);
        Assert.True(s.GetProperty("activePlayers").GetInt32() >= 30);
    }

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cors_allows_the_angular_dev_server()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/players/search?q=cu");
        request.Headers.Add("Origin", "http://localhost:4200");
        var response = await _client.SendAsync(request);
        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}

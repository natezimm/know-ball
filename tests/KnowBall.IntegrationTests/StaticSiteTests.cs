using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;

namespace KnowBall.IntegrationTests;

/// <summary>
/// How the API serves the built Angular app: scripts and styles go out compressed, and the files the
/// build names by their content are kept by browsers for good, while the app shell is not.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StaticSiteTests(ApiFactory factory) : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("know-ball-site-").FullName;

    private HttpClient Site()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><app-root></app-root>" + new string(' ', 2000));
        File.WriteAllText(Path.Combine(_webRoot, "main-5F6GC5JD.js"), string.Concat(Enumerable.Repeat("console.log('know ball');", 200)));
        return factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot)).CreateClient();
    }

    private static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        return request;
    }

    [Fact]
    public async Task Hashed_scripts_are_compressed_and_kept_for_a_year()
    {
        var response = await Site().SendAsync(Get("/main-5F6GC5JD.js"));

        response.EnsureSuccessStatusCode();
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_app_shell_is_compressed_but_not_kept()
    {
        var response = await Site().SendAsync(Get("/some/game/route"));

        response.EnsureSuccessStatusCode();
        Assert.Single(response.Content.Headers.ContentEncoding);
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task Api_responses_are_not_compressed()
    {
        var response = await Site().SendAsync(Get("/api/stats/summary"));

        response.EnsureSuccessStatusCode();
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);
}

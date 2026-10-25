using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

/// <summary>
/// The site-wide per-address limit (RateLimits:RequestsPerMinute) that sits in front of every request,
/// and the 429 a limited visitor gets back.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitTests(ApiFactory factory)
{
    private const string ProxyNetwork = "172.31.250.0/24";

    /// <summary>A server allowing <paramref name="perMinute"/> requests a minute, reached through the trusted proxy.</summary>
    private HttpClient LimitedTo(int perMinute) =>
        factory.WithWebHostBuilder(builder => builder
                .UseSetting("RateLimits:RequestsPerMinute", perMinute.ToString())
                .UseSetting("ForwardedHeaders:KnownNetworks:0", ProxyNetwork)
                .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new FromProxy())))
            .CreateClient();

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string visitor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", visitor);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Every_request_counts_and_the_one_over_gets_429_with_Retry_After()
    {
        var client = LimitedTo(3);

        var statuses = new List<HttpStatusCode>();
        foreach (var path in new[] { "/api/stats/summary", "/api/players/search?q=le", "/api/import-runs/latest" })
            statuses.Add((await GetAsync(client, path, "198.51.100.1")).StatusCode);
        var rejected = await GetAsync(client, "/api/stats/summary", "198.51.100.1");

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = Assert.Single(rejected.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter), 1, 60);
        Assert.Equal("Too many requests, please slow down.", (await rejected.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }

    [Fact]
    public async Task Each_visitor_has_their_own_budget()
    {
        var client = LimitedTo(1);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/stats/summary", "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetAsync(client, "/api/stats/summary", "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/stats/summary", "198.51.100.2")).StatusCode);
    }

    [Fact]
    public async Task One_IPv6_network_shares_a_budget()
    {
        var client = LimitedTo(1);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/stats/summary", "2001:db8:1:2::1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetAsync(client, "/api/stats/summary", "2001:db8:1:2::ffff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/stats/summary", "2001:db8:1:3::1")).StatusCode);
    }

    [Fact]
    public async Task Health_is_never_limited()
    {
        var client = LimitedTo(1);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/health", "198.51.100.1")).StatusCode);
    }

    private sealed record ErrorBody(string Error);

    /// <summary>Makes every request arrive from the trusted proxy; the test server leaves the remote address empty.</summary>
    private sealed class FromProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext http, RequestDelegate nextMiddleware) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse("172.31.250.1");
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}

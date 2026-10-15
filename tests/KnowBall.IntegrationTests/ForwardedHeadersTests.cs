using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnowBall.IntegrationTests;

/// <summary>
/// The per-address rate limits behind a reverse proxy: X-Forwarded-For counts only on requests from
/// a network in ForwardedHeaders:KnownNetworks, so a visitor cannot dodge a limit by inventing addresses.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(ApiFactory factory)
{
    private const string ProxyNetwork = "172.31.250.0/24";

    /// <summary>A server whose requests all arrive from <paramref name="remote"/>, limited to two Spordle games a minute.</summary>
    private HttpClient ClientFrom(string remote) =>
        factory.WithWebHostBuilder(builder => builder
                .UseSetting("ForwardedHeaders:KnownNetworks:0", ProxyNetwork)
                .UseSetting("Spordle:GamesPerMinute", "2")
                .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress(remote))))
            .CreateClient();

    private static async Task<HttpStatusCode> StartGameAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/spordle/games")
        {
            Content = JsonContent.Create(new { wordLength = 5 }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Behind_the_trusted_proxy_each_visitor_has_their_own_limit()
    {
        var client = ClientFrom("172.31.250.1");

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 4; i++) statuses.Add(await StartGameAsync(client, $"198.51.100.{i}"));

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Created, s));
    }

    [Fact]
    public async Task A_forwarded_address_from_anyone_else_is_ignored()
    {
        var client = ClientFrom("203.0.113.7");

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 3; i++) statuses.Add(await StartGameAsync(client, $"198.51.100.{i}"));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }

    /// <summary>Sets the connection's remote address before the app's own middleware runs; the test server leaves it empty.</summary>
    private sealed class RemoteAddress(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext http, RequestDelegate nextMiddleware) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(address);
                return nextMiddleware(http);
            });
            next(app);
        };
    }
}

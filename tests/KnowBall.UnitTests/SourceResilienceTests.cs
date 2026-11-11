using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using KnowBall.Infrastructure;
using KnowBall.Infrastructure.Sources;

namespace KnowBall.UnitTests;

/// <summary>The data sources' HTTP clients as <see cref="DependencyInjection.AddInfrastructure"/> registers them, over a scripted server.</summary>
public class SourceResilienceTests
{
    private sealed class Scripted(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var answer = answers[Math.Min(Calls, answers.Length - 1)];
            Calls++;
            await Task.Yield();
            return answer();
        }
    }

    private sealed class Hangs : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }

    private static HttpClient Espn(HttpMessageHandler server, int timeoutSeconds = 30)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Trivia"] = "Data Source=:memory:",
            ["Sources:Retry:BaseDelaySeconds"] = "0",
            // Keeps the whole call's budget, and so the stalled-body test, to a few seconds.
            ["Sources:Retry:MaxDelaySeconds"] = "1",
            ["Sources:Espn:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(config);
        services.AddHttpClient<EspnRosterSource>().ConfigurePrimaryHttpMessageHandler(() => server);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(nameof(EspnRosterSource));
    }

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    [Fact]
    public async Task A_server_error_is_tried_again_and_the_next_answer_is_used()
    {
        var server = new Scripted(Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.OK));

        using var response = await Espn(server).GetAsync("https://example.test/teams");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task Throttling_is_tried_again()
    {
        var server = new Scripted(Status(HttpStatusCode.TooManyRequests), Status(HttpStatusCode.OK));

        using var response = await Espn(server).GetAsync("https://example.test/teams");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task It_gives_up_after_two_retries_with_the_last_answer()
    {
        var server = new Scripted(Status(HttpStatusCode.ServiceUnavailable));

        using var response = await Espn(server).GetAsync("https://example.test/teams");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, server.Calls);
    }

    [Fact]
    public async Task A_client_error_is_not_tried_again()
    {
        var server = new Scripted(Status(HttpStatusCode.NotFound), Status(HttpStatusCode.OK));

        using var response = await Espn(server).GetAsync("https://example.test/teams");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public void A_long_retry_after_is_held_to_the_longest_wait()
    {
        var asked = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        asked.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromSeconds(30), SourceResilience.RetryAfter(asked, TimeSpan.FromSeconds(30), DateTimeOffset.UnixEpoch));
        Assert.Null(SourceResilience.RetryAfter(new HttpResponseMessage(HttpStatusCode.BadGateway), TimeSpan.FromSeconds(30), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task A_body_that_stalls_is_given_up_on()
    {
        var server = new Scripted(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

        // Headers arrive at once; HttpClient then reads the body outside the handler, under its own timeout.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Espn(server, timeoutSeconds: 1).GetAsync("https://example.test/teams"));
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Each_attempt_has_the_sources_own_timeout()
    {
        var server = new Hangs();

        await Assert.ThrowsAnyAsync<Exception>(() => Espn(server, timeoutSeconds: 1).GetAsync("https://example.test/teams"));

        // Every attempt timed out on its own, rather than one HttpClient timeout ending all three.
        Assert.Equal(3, server.Calls);
    }
}

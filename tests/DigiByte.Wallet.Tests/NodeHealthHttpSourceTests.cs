using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>T006: the health request is one short, no-retry GET that never throws.</summary>
public class NodeHealthHttpSourceTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri}");
            return await respond(request, ct);
        }
    }

    private static NodeSourceOptions Options(string? url = "https://node.example.org", string timeoutSeconds = "3") =>
        NodeSourceOptions.FromConfiguration(key => key switch
        {
            "OwnNode:Enabled" => "true",
            "OwnNode:ApiUrl" => url,
            "OwnNode:HealthTimeoutSeconds" => timeoutSeconds,
            _ => null,
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private const string OkBody =
        "{\"status\":\"ok\",\"blocks\":1000,\"headers\":1000,\"lag\":0,\"peers\":8,\"txindex\":true,\"versionOk\":true,\"nodeVersion\":\"9.26.7\"}";

    [Fact]
    public async Task ValidBody_IsParsed()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, OkBody)));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options());

        var result = await sut.GetAsync();

        Assert.True(result.Reachable);
        Assert.False(result.Malformed);
        Assert.Equal("ok", result.Status);
        Assert.Equal(1000, result.Blocks);
    }

    [Fact]
    public async Task RequestGoesToApiHealth_ExactlyOnce()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, OkBody)));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options("https://node.example.org/"));

        await sut.GetAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET https://node.example.org/api/health", request);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task FailureStatus_IsUnreachable_AndNotRetried(HttpStatusCode status)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(status, "{\"status\":\"unhealthy\"}")));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options());

        var result = await sut.GetAsync();

        Assert.False(result.Reachable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HttpRequestException_IsUnreachable_NeverThrown()
    {
        var handler = new Handler((_, _) => throw new HttpRequestException("connection refused"));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options());

        var result = await sut.GetAsync();

        Assert.False(result.Reachable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AnyOtherException_IsUnreachable_NeverThrown()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("boom"));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options());

        var result = await sut.GetAsync();

        Assert.False(result.Reachable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("[1]")]
    public async Task UnreadableBody_IsMalformed(string body)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options());

        var result = await sut.GetAsync();

        Assert.True(result.Reachable);
        Assert.True(result.Malformed);
    }

    [Fact]
    public async Task SlowNode_IsGivenUpOnAtTheTimeout_AndReportedUnreachable()
    {
        var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, OkBody);
        });
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options(timeoutSeconds: "1"));

        var started = DateTime.UtcNow;
        var result = await sut.GetAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.False(result.Reachable);
        Assert.True(elapsed < TimeSpan.FromSeconds(4), $"took {elapsed}");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_ReturnsUnreachable_NotAnException()
    {
        var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, OkBody);
        });
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options(timeoutSeconds: "10"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await sut.GetAsync(cts.Token);

        Assert.False(result.Reachable);
    }

    [Fact]
    public async Task NoApiUrl_IsUnreachable_AndMakesNoRequest()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, OkBody)));
        var sut = new NodeHealthHttpSource(new HttpClient(handler), Options(url: "not a url"));

        var result = await sut.GetAsync();

        Assert.False(result.Reachable);
        Assert.Empty(handler.Requests);
    }
}

using System.Net;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Timing the routes to the gateway so a deployment can choose between them with numbers.</summary>
public class GatewayRouteTimerTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Urls) Urls.Add(request.RequestUri!.ToString());
            return respond(request);
        }
    }

    private static P2pGatewayOptions Options(string? url, string? alt) =>
        P2pGatewayOptions.FromConfiguration(k => k switch
        {
            "SpvGateway:ApiUrl" => url,
            "SpvGateway:ApiUrlAlt" => alt,
            _ => null,
        });

    [Fact]
    public void The_alternative_address_is_read_and_must_be_a_valid_address()
    {
        Assert.Equal("https://b.example.org", Options("https://a.example.org", "https://b.example.org/").AltApiUrl);
        Assert.Null(Options("https://a.example.org", "http://not-local.example.org").AltApiUrl);
        Assert.Null(Options("https://a.example.org", null).AltApiUrl);
    }

    [Fact]
    public void Routes_lists_each_distinct_address_once()
    {
        var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        Assert.Equal(["https://a.example.org", "https://b.example.org"], new GatewayRouteTimer(http, Options("https://a.example.org", "https://b.example.org")).Routes);
        Assert.Equal(["https://a.example.org"], new GatewayRouteTimer(http, Options("https://a.example.org", "https://a.example.org")).Routes);
        Assert.Empty(new GatewayRouteTimer(http, Options(null, null)).Routes);
    }

    [Fact]
    public async Task Both_routes_are_asked_in_turn_and_only_the_health_report_is_requested()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        var timer = new GatewayRouteTimer(new HttpClient(handler), Options("https://a.example.org", "https://b.example.org"));

        var result = await timer.MeasureAsync(3);

        Assert.Equal(2, result.Count);
        Assert.All(result, t => { Assert.Equal(3, t.Count); Assert.Equal(0, t.Failures); });
        Assert.All(handler.Urls, u => Assert.Contains("/v1/health?probe=", u));
        // One warm-up request per route, then the samples alternate: a b a b a b.
        Assert.Equal(8, handler.Urls.Count);
        Assert.Equal(["a.example.org", "b.example.org", "a.example.org", "b.example.org"], handler.Urls.Skip(2).Take(4).Select(u => new Uri(u).Host));
    }

    [Fact]
    public async Task A_route_that_fails_or_times_out_counts_as_a_failure_not_a_time()
    {
        var handler = new Handler(r => r.RequestUri!.Host == "b.example.org"
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        var timer = new GatewayRouteTimer(new HttpClient(handler), Options("https://a.example.org", "https://b.example.org"));

        var result = await timer.MeasureAsync(2);

        Assert.Equal(0, result[0].Failures);
        Assert.Equal(2, result[1].Failures);
        Assert.Empty(result[1].SamplesMs);
        Assert.Null(result[1].MedianMs);
    }

    [Fact]
    public void The_median_and_the_slowest_five_percent_come_from_the_samples()
    {
        var t = new GatewayRouteTiming("https://a.example.org", [50, 10, 30, 20, 40, 60, 70, 80, 90, 100], 1);

        Assert.Equal(10, t.MinMs);
        Assert.Equal(50, t.MedianMs);
        Assert.Equal(100, t.P95Ms);
        Assert.Equal(10, t.Count);
    }
}

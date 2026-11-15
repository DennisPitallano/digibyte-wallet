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
    public void The_median_comes_from_the_samples_and_the_tail_needs_enough_of_them()
    {
        var few = new GatewayRouteTiming("https://a.example.org", [50, 10, 30, 20, 40, 60, 70, 80, 90, 100], 1);

        Assert.Equal(10, few.MinMs);
        Assert.Equal(50, few.MedianMs);
        Assert.Equal(10, few.Count);
        Assert.Null(few.P95Ms); // ten answers: the "slowest 5%" would just be the single slowest one

        var many = new GatewayRouteTiming("https://a.example.org", Enumerable.Range(1, 40).Select(i => (double)i).ToList(), 0);
        Assert.Equal(38, many.P95Ms);
    }

    private static GatewayRouteTiming Timing(double center, double jitter, int count = 25) =>
        new("https://x.example.org", Enumerable.Range(0, count).Select(i => center + ((i % 5) - 2) * jitter).ToList(), 0);

    [Fact]
    public void A_gap_inside_the_run_to_run_variation_is_about_the_same()
    {
        // Medians 28 ms apart, but each route's answers vary by tens of milliseconds.
        var (verdict, gap) = GatewayRouteComparison.Compare(Timing(284, 30), Timing(256, 30));

        Assert.Equal(RouteVerdict.AboutTheSame, verdict);
        Assert.Equal(28, gap, 1);
    }

    [Fact]
    public void A_gap_much_bigger_than_the_variation_names_the_faster_route()
    {
        Assert.Equal(RouteVerdict.FirstFaster, GatewayRouteComparison.Compare(Timing(100, 3), Timing(160, 3)).Verdict);
        Assert.Equal(RouteVerdict.SecondFaster, GatewayRouteComparison.Compare(Timing(160, 3), Timing(100, 3)).Verdict);
    }

    [Fact]
    public void A_tiny_gap_never_counts_even_when_the_answers_are_steady()
    {
        Assert.Equal(RouteVerdict.AboutTheSame, GatewayRouteComparison.Compare(Timing(100, 0.1), Timing(103, 0.1)).Verdict);
    }

    [Fact]
    public void Too_few_answers_on_a_route_is_not_enough_data()
    {
        Assert.Equal(RouteVerdict.NotEnoughData, GatewayRouteComparison.Compare(Timing(100, 3, 4), Timing(160, 3)).Verdict);
        Assert.Equal(RouteVerdict.NotEnoughData, GatewayRouteComparison.Compare(new GatewayRouteTiming("a", [], 25), Timing(160, 3)).Verdict);
    }
}

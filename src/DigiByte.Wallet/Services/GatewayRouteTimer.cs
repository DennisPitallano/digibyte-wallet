using System.Diagnostics;

namespace DigiByte.Wallet.Services;

/// <summary>How long one route to the gateway took to answer <c>/v1/health</c>, over a number of samples.</summary>
public sealed record GatewayRouteTiming(string Url, IReadOnlyList<double> SamplesMs, int Failures)
{
    public int Count => SamplesMs.Count;
    public double? MinMs => Count == 0 ? null : SamplesMs.Min();
    public double? MedianMs => Percentile(0.5);
    public double? P95Ms => Percentile(0.95);

    private double? Percentile(double p)
    {
        if (Count == 0)
            return null;
        var sorted = SamplesMs.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

/// <summary>
/// Measures how fast the gateway answers by each of its routes, so a deployment can choose between them with numbers. It only reads
/// <c>/v1/health</c> (no wallet data is sent), alternates between the routes so network changes hit both alike, and runs only when asked.
/// Never throws: a route that does not answer counts as a failure.
/// </summary>
public sealed class GatewayRouteTimer(HttpClient http, P2pGatewayOptions options)
{
    /// <summary>The routes that can be measured: the main address and, when set, the alternative.</summary>
    public IReadOnlyList<string> Routes =>
        new[] { options.ApiUrl, options.AltApiUrl }.Where(u => u is not null).Select(u => u!).Distinct().ToList();

    public async Task<IReadOnlyList<GatewayRouteTiming>> MeasureAsync(int samples, CancellationToken cancellationToken = default)
    {
        samples = Math.Clamp(samples, 1, 50);
        var routes = Routes;
        var times = routes.ToDictionary(r => r, _ => new List<double>());
        var failures = routes.ToDictionary(r => r, _ => 0);

        // The first request to a route also pays for the connection and TLS set-up. It is made, but not counted, so the numbers show
        // the steady speed the wallet sees on its later requests.
        foreach (var route in routes)
            await ProbeAsync(route, 0, cancellationToken);

        for (var i = 1; i <= samples; i++)
        {
            foreach (var route in routes)
            {
                var ms = await ProbeAsync(route, i, cancellationToken);
                if (ms is null)
                    failures[route]++;
                else
                    times[route].Add(ms.Value);
            }
        }
        return routes.Select(r => new GatewayRouteTiming(r, times[r], failures[r])).ToList();
    }

    private async Task<double?> ProbeAsync(string baseUrl, int n, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.HealthTimeout);
            var started = Stopwatch.GetTimestamp();
            // A changing query value keeps a cache between the wallet and the gateway from answering for it.
            using var response = await http.GetAsync($"{baseUrl}/v1/health?probe={n}-{Environment.TickCount64}", timeout.Token);
            await response.Content.ReadAsStringAsync(timeout.Token);
            var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return response.IsSuccessStatusCode ? ms : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

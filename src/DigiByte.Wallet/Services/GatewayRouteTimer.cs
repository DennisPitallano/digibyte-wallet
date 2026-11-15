using System.Diagnostics;

namespace DigiByte.Wallet.Services;

/// <summary>How long one route to the gateway took to answer <c>/v1/health</c>, over a number of samples.</summary>
public sealed record GatewayRouteTiming(string Url, IReadOnlyList<double> SamplesMs, int Failures)
{
    public int Count => SamplesMs.Count;
    public double? MinMs => Count == 0 ? null : SamplesMs.Min();
    public double? MedianMs => Percentile(0.5);

    /// <summary>The slowest 5%: shown only with enough samples (at least <see cref="MinSamplesForTail"/>); with fewer it is just the single slowest answer.</summary>
    public double? P95Ms => Count >= MinSamplesForTail ? Percentile(0.95) : null;

    public const int MinSamplesForTail = 20;

    /// <summary>How much the answer times vary: the range of the middle half of the samples, so one slow answer does not distort it.</summary>
    public double? SpreadMs => Count < 4 ? null : Percentile(0.75) - Percentile(0.25);

    /// <summary>The uncertainty of the median (its standard error), estimated from the spread. Roughly 1.25 x sigma / sqrt(n), with sigma taken as spread / 1.35.</summary>
    public double? MedianUncertaintyMs => SpreadMs is { } s ? 1.2533 * (s / 1.349) / Math.Sqrt(Count) : null;

    private double? Percentile(double p)
    {
        if (Count == 0)
            return null;
        var sorted = SamplesMs.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

public enum RouteVerdict
{
    /// <summary>Too few answers on a route to say anything.</summary>
    NotEnoughData,
    /// <summary>The gap is within what the run-to-run variation can explain, so neither route is faster.</summary>
    AboutTheSame,
    FirstFaster,
    SecondFaster,
}

public static class GatewayRouteComparison
{
    /// <summary>A gap smaller than this does not matter to anyone, whatever the numbers say.</summary>
    public const double NegligibleMs = 5;

    public const int MinSamples = 8;

    /// <summary>
    /// Says a route is faster only when the gap between the two medians is clearly bigger than the variation in the measurements
    /// (more than twice the combined uncertainty of the two medians) and bigger than <see cref="NegligibleMs"/>.
    /// </summary>
    public static (RouteVerdict Verdict, double GapMs) Compare(GatewayRouteTiming first, GatewayRouteTiming second)
    {
        if (first.Count < MinSamples || second.Count < MinSamples || first.MedianMs is not { } a || second.MedianMs is not { } b)
            return (RouteVerdict.NotEnoughData, 0);
        var gap = Math.Abs(a - b);
        var noise = Math.Sqrt(Math.Pow(first.MedianUncertaintyMs ?? 0, 2) + Math.Pow(second.MedianUncertaintyMs ?? 0, 2));
        if (gap <= NegligibleMs || gap <= 2 * noise)
            return (RouteVerdict.AboutTheSame, gap);
        return (a < b ? RouteVerdict.FirstFaster : RouteVerdict.SecondFaster, gap);
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

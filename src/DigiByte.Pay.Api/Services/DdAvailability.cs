using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Whether DigiDollar can be confirmed right now on a network: the public explorer answers and DigiDollar is active there
/// (specs/035 research R6). Cached for <see cref="CacheFor"/> per network and cut off after <see cref="Timeout"/>, so no
/// caller waits on a slow explorer (Principle V). Never throws: any failure reads as "not available". DGB never asks it.
/// </summary>
public sealed class DdAvailability(IDdChainProvider chains, IMemoryCache cache, ILogger<DdAvailability> log)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>The reason payments and tip pages give while DD can't be confirmed (contract C2).</summary>
    public const string UnavailableReason = "dd_service_unavailable";

    /// <summary>The cache entry for a network's answer.</summary>
    public static string CacheKeyFor(string network) => "digipay.dd.available." + (network == "mainnet" ? "mainnet" : "testnet");

    public async Task<bool> IsAvailableAsync(string network, CancellationToken ct = default)
    {
        var net = network == "mainnet" ? "mainnet" : "testnet";
        var cacheKey = CacheKeyFor(net);
        if (cache.TryGetValue(cacheKey, out bool known)) return known;

        bool available;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var health = await chains.For(net).GetHealthAsync(cts.Token);
            available = health.Available && health.DeploymentActive;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogInformation(ex, "DigiDollar availability probe failed ({Network})", net);
            available = false;
        }

        cache.Set(cacheKey, available, CacheFor);
        return available;
    }
}

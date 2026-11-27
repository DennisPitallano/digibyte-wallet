using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Whether the DigiDollar node can be used right now, from <see cref="DigiDollarNodeClient.IsHealthyAsync"/>, cached
/// for <see cref="CacheFor"/> and cut off after <see cref="Timeout"/> so no caller waits on a slow node (Principle V,
/// specs/033 research R10). Never throws: any failure reads as "not available".
/// </summary>
public sealed class DdAvailability(IServiceProvider services, IMemoryCache cache, ILogger<DdAvailability> log)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const string CacheKey = "digipay.dd.available";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out bool known)) return known;

        bool available;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            // Typed HttpClient services are transient; resolve per probe so this singleton never pins a handler.
            var node = services.GetRequiredService<DigiDollarNodeClient>();
            available = await node.IsHealthyAsync(cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogInformation(ex, "DigiDollar availability probe failed");
            available = false;
        }

        cache.Set(CacheKey, available, CacheFor);
        return available;
    }
}

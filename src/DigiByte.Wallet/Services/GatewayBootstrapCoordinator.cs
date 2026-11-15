namespace DigiByte.Wallet.Services;

/// <summary>
/// Feeds explorer-served refreshes to <see cref="GatewayStateBootstrap"/> without ever touching them (spec FR-009a, T073). The wallet
/// offers the transactions it just got from an explorer; this class reads the unspent outputs in the background, at most once per
/// interval per wallet, and records the result. It runs only when the gateway feature is on, never when the gateway itself served the
/// refresh, and any failure is swallowed, so the explorer-served result is never delayed or changed. It never contacts the gateway.
/// </summary>
public sealed class GatewayBootstrapCoordinator(
    GatewayStateBootstrap bootstrap, IBlockchainService blockchain, Func<bool> isEnabled, TimeProvider? time = null, TimeSpan? minInterval = null)
{
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _minInterval = minInterval ?? DefaultMinInterval;
    private readonly Dictionary<string, DateTimeOffset> _lastOffer = [];
    private readonly object _gate = new();

    public bool IsEnabled => isEnabled();

    /// <summary>The explorer tip height, read before the snapshot is taken, or null when it cannot be read.</summary>
    public async Task<int?> ReadTipAsync()
    {
        if (!isEnabled())
            return null;
        try
        {
            return await blockchain.GetBlockHeightAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Starts recording in the background and returns at once. Does nothing when the offer is not wanted.</summary>
    public void Offer(string walletId, IReadOnlyList<string> addresses, int? tipBefore, IReadOnlyList<TransactionInfo> transactions, bool servedByGateway)
    {
        if (!ShouldRecord(walletId, tipBefore, servedByGateway))
            return;
        _ = Task.Run(() => RecordAsync(walletId, addresses, tipBefore!.Value, transactions));
    }

    /// <summary>The same as <see cref="Offer"/> but awaitable, for tests.</summary>
    public async Task<BootstrapOutcome?> OfferAndWaitAsync(string walletId, IReadOnlyList<string> addresses, int? tipBefore, IReadOnlyList<TransactionInfo> transactions, bool servedByGateway)
    {
        if (!ShouldRecord(walletId, tipBefore, servedByGateway))
            return null;
        return await RecordAsync(walletId, addresses, tipBefore!.Value, transactions);
    }

    private bool ShouldRecord(string walletId, int? tipBefore, bool servedByGateway)
    {
        if (!isEnabled() || servedByGateway || tipBefore is null)
            return false;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_lastOffer.TryGetValue(walletId, out var last) && now - last < _minInterval)
                return false;
            _lastOffer[walletId] = now;
            return true;
        }
    }

    private async Task<BootstrapOutcome> RecordAsync(string walletId, IReadOnlyList<string> addresses, int tipBefore, IReadOnlyList<TransactionInfo> transactions)
    {
        try
        {
            var unspent = await blockchain.GetUtxosAsync(addresses);
            return await bootstrap.TryRecordAsync(walletId, tipBefore, transactions, unspent);
        }
        catch (Exception)
        {
            return BootstrapOutcome.Failed;
        }
    }
}

using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Wraps the explorer-backed DigiDollar API and adds the oracle reading to its health (specs/014-digibyte-io-oracle): the explorer has no oracle
/// data, so without this the oracle price, its freshness, the protection flags and the emergency state show as unavailable.
///
/// The merge never waits for the oracle (FR-010): <see cref="GetHealthAsync"/> awaits only the explorer call, merges the reading the feed already
/// holds, and asks the feed to refresh in the background. Until the first reading arrives the health carries <c>OracleLoading</c>; the screens
/// re-read the health when the feed reports a change. The reading is merged only into an available health and only on mainnet; an unavailable
/// health stays unavailable and no fetch is started for it. Every other call passes straight through. Display only: nothing here feeds a
/// transaction.
/// </summary>
public sealed class OracleEnrichedDigiDollarApi(IDigiDollarNodeApi inner, IOracleFeed feed, Func<bool> isMainnet) : IDigiDollarNodeApi
{
    public async Task<NetworkHealth> GetHealthAsync(CancellationToken ct)
    {
        var health = await inner.GetHealthAsync(ct);
        var mainnet = isMainnet();
        if (!mainnet || !health.Available)
            return health;

        // Ask first, then read: a fetch started now is what makes IsLoading true on the very first call.
        feed.RequestRefresh();
        return Merge(health, feed, mainnet);
    }

    /// <summary>
    /// Adds the feed's current reading (or its loading flag) to a health answer. Returns the health unchanged unless it is available and the
    /// network is mainnet, so no caller can put oracle data on a test network or on an unavailable health (FR-012). Used by the wrapper and by the
    /// screens' view model when the feed reports a change.
    /// </summary>
    public static NetworkHealth Merge(NetworkHealth health, IOracleFeed feed, bool isMainnet)
    {
        if (!isMainnet || !health.Available)
            return health;

        var reading = feed.Current;
        if (reading is null)
            return health with { OracleLoading = feed.IsLoading };

        return health with
        {
            OraclePriceMicroUsd = reading.PriceMicroUsd,
            OracleHeight = (int)Math.Min(reading.Height, int.MaxValue),
            OracleUpdatedAt = reading.UpdatedAtUtc,
            OracleReportedStale = reading.ReportedStale,
            ErrActive = reading.ErrActive,
            VolatilityFreeze = reading.VolatilityFreeze,
            MintingRestricted = reading.MintingRestricted,
            AllOperationsRestricted = reading.AllOperationsRestricted,
            OracleLoading = false,
        };
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>> GetUtxosAsync(IReadOnlyCollection<string> addresses, int minConf, CancellationToken ct) =>
        inner.GetUtxosAsync(addresses, minConf, ct);

    public Task<DdHistoryResult> GetHistoryAsync(IReadOnlyCollection<string> addresses, int count, string? before, CancellationToken ct) =>
        inner.GetHistoryAsync(addresses, count, before, ct);

    public Task<DdTxStatusResult> GetTxStatusAsync(string txid, CancellationToken ct) => inner.GetTxStatusAsync(txid, ct);

    public Task<string> BroadcastAsync(string rawTxHex, CancellationToken ct) => inner.BroadcastAsync(rawTxHex, ct);
}

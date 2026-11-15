using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Where the wallet asks for proofs of transactions it already knows. The gateway client is the real one.</summary>
public interface IGatewayProofSource
{
    Task<GatewayProofsAnswer> GetProofsAsync(IReadOnlyCollection<(string Txid, int Height)> items, CancellationToken cancellationToken = default);
}

public enum UpgradeSkipReason
{
    None,

    /// <summary>The caller does not allow proof requests now (they reveal transaction ids, so they run only when the gateway is the answering source or background proving is on).</summary>
    NotAllowed,

    /// <summary>Run too recently for this wallet.</summary>
    TooSoon,

    /// <summary>The wallet has no record.</summary>
    NoState,
}

/// <summary>What one upgrade run did.</summary>
/// <param name="Upgraded">Explorer-sourced transactions that are now gateway-proven.</param>
/// <param name="Disputed">Transactions whose proof contradicts what the explorer reported (marked disputed, data replaced by the proven data).</param>
/// <param name="Unavailable">Transactions the gateway could not prove; they stay unchecked.</param>
/// <param name="Skipped">Why nothing was attempted, or <see cref="UpgradeSkipReason.None"/>.</param>
public sealed record UpgradeResult(int Upgraded, int Disputed, int Unavailable, UpgradeSkipReason Skipped);

/// <summary>
/// Turns explorer-sourced transactions into gateway-proven ones, one batch of at most 20 at a time (spec FR-012a, T074). Each proof is
/// checked against the wallet own headers; a transaction that checks out gets its raw bytes and block hash and loses the "unchecked"
/// label. Best effort: it never blocks a read, runs at most once per interval per wallet, and handles a bounded number of transactions
/// per run. A proof the wallet refuses makes the source faulty (the run throws); a block the wallet has not verified yet is left for
/// later. When a proven block differs from what the explorer reported, the transaction is marked disputed and kept out of the spendable
/// balance, with its data replaced by the proven data.
/// </summary>
public sealed class GatewayProofUpgrader(
    IGatewayProofSource source, GatewayProofChecker checker, WalletSyncStateStore states, Func<bool> isAllowed,
    TimeProvider? time = null, TimeSpan? minInterval = null, int maxPerRun = 100)
{
    public const int BatchSize = 20;
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _minInterval = minInterval ?? DefaultMinInterval;
    private readonly Dictionary<string, DateTimeOffset> _lastRun = [];
    private readonly object _gate = new();

    public async Task<UpgradeResult> UpgradeAsync(string walletId, CancellationToken cancellationToken = default)
    {
        if (!isAllowed())
            return new UpgradeResult(0, 0, 0, UpgradeSkipReason.NotAllowed);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_lastRun.TryGetValue(walletId, out var last) && now - last < _minInterval)
                return new UpgradeResult(0, 0, 0, UpgradeSkipReason.TooSoon);
            _lastRun[walletId] = now;
        }

        var state = await states.GetAsync(walletId);
        if (state is null)
            return new UpgradeResult(0, 0, 0, UpgradeSkipReason.NoState);

        var wanted = state.Transactions
            .Where(t => t.Origin == SyncOrigin.ExplorerSourced && t.Height > 0)
            .OrderByDescending(t => t.Height) // the newest matter most and are the most likely to still be served
            .Take(maxPerRun)
            .ToList();

        var updates = new Dictionary<string, SyncTransaction>();
        var upgraded = 0;
        var disputed = 0;
        var unavailable = 0;

        foreach (var batch in wanted.Chunk(BatchSize))
        {
            var answer = await source.GetProofsAsync(batch.Select(t => (t.Txid, t.Height)).ToList(), cancellationToken);
            unavailable += answer.Unavailable.Count;
            foreach (var proof in answer.Proofs)
            {
                var recorded = batch.FirstOrDefault(t => t.Txid.Equals(proof.Txid, StringComparison.OrdinalIgnoreCase));
                if (recorded is null)
                    continue; // a proof for something that was not asked for is ignored

                var check = checker.Check(proof);
                if (check == ProofCheckResult.HeaderNotVerified)
                {
                    unavailable++;
                    continue; // the wallet headers are behind: try again later
                }
                if (check != ProofCheckResult.Valid)
                    throw new GatewayFaultException($"The gateway sent a proof the wallet refused ({check}).");
                if (proof.Height != recorded.Height)
                {
                    unavailable++; // it proved the transaction in another block than the one asked for: not what was requested
                    continue;
                }

                var contradicts = recorded.BlockHash.Length == 64 && !recorded.BlockHash.Equals(proof.BlockHash, StringComparison.OrdinalIgnoreCase);
                if (contradicts)
                {
                    disputed++;
                    updates[recorded.Txid] = recorded with { Height = proof.Height, BlockHash = proof.BlockHash, Raw = proof.Raw, Disputed = true };
                }
                else
                {
                    upgraded++;
                    updates[recorded.Txid] = recorded with { BlockHash = proof.BlockHash, Raw = proof.Raw, Origin = SyncOrigin.GatewayProven, Disputed = false };
                }
            }
        }

        if (updates.Count > 0)
        {
            // Re-read so a refresh that ran meanwhile is not overwritten, then change only the entries this run proved.
            var latest = await states.GetAsync(walletId);
            if (latest is not null)
            {
                var merged = latest.Transactions.Select(t => updates.TryGetValue(t.Txid, out var u) && t.Origin == SyncOrigin.ExplorerSourced ? u : t).ToList();
                await states.SaveAsync(latest with { Transactions = merged, UpdatedAt = _time.GetUtcNow() });
            }
        }
        return new UpgradeResult(upgraded, disputed, unavailable, UpgradeSkipReason.None);
    }
}

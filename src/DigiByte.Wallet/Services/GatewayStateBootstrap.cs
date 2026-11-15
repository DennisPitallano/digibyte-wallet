using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

public enum BootstrapOutcome
{
    /// <summary>The wallet record was created or merged.</summary>
    Recorded,

    /// <summary>The wallet own headers do not reach the block the record would name, so nothing was recorded. The next refresh tries again.</summary>
    HeadersNotReady,

    /// <summary>Recording failed (storage error). Reported only by <see cref="GatewayStateBootstrap.TryRecordAsync"/>, which never throws.</summary>
    Failed,
}

/// <summary>
/// Creates and refreshes the wallet record from an explorer-served refresh (spec FR-009a, T073). The explorer transactions and unspent
/// outputs are recorded as explorer-sourced; the last-refreshed height is the explorer tip (read before the snapshot) minus 6 and its
/// hash comes from the wallet own headers; later runs merge into the record and only move the height forward. It never contacts the
/// gateway and never changes what the explorer returned to the caller.
/// </summary>
public sealed class GatewayStateBootstrap(WalletSyncStateStore states, GatewayHeaderStore headers, TimeProvider? time = null)
{
    public const int Margin = GatewayIncrementalSync.Margin;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>As <see cref="RecordAsync"/>, but any exception is swallowed: this runs beside an explorer read and must never affect it.</summary>
    public async Task<BootstrapOutcome> TryRecordAsync(string walletId, int explorerTipHeight, IReadOnlyList<TransactionInfo> transactions, IReadOnlyList<UtxoInfo> unspent)
    {
        try
        {
            return await RecordAsync(walletId, explorerTipHeight, transactions, unspent);
        }
        catch (Exception)
        {
            return BootstrapOutcome.Failed;
        }
    }

    public async Task<BootstrapOutcome> RecordAsync(string walletId, int explorerTipHeight, IReadOnlyList<TransactionInfo> transactions, IReadOnlyList<UtxoInfo> unspent)
    {
        var syncedHeight = Math.Max(1, explorerTipHeight - Margin);
        var syncedHeader = headers.HeaderAt(syncedHeight);
        if (syncedHeader is null)
            return BootstrapOutcome.HeadersNotReady;

        var existing = await states.GetAsync(walletId);

        var fromExplorer = transactions.Select(t => ToRecord(t, explorerTipHeight)).ToList();
        var explorerIds = fromExplorer.ToDictionary(t => t.Txid, t => t, StringComparer.OrdinalIgnoreCase);

        var merged = new List<SyncTransaction>();
        if (existing is not null)
        {
            foreach (var old in existing.Transactions)
            {
                if (!explorerIds.TryGetValue(old.Txid, out var theirs))
                {
                    merged.Add(old); // the explorer did not list it (its list is a window); nothing to compare
                    continue;
                }
                if (old.Origin == SyncOrigin.GatewayProven)
                {
                    // Kept while the two agree; marked disputed, with the proven version kept, when the explorer puts it elsewhere.
                    var agrees = old.Height == theirs.Height || old.Height == 0 || theirs.Height == 0;
                    merged.Add(agrees ? old : old with { Disputed = true });
                }
                else
                {
                    merged.Add(theirs);
                }
            }
        }
        var knownIds = merged.Select(t => t.Txid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        merged.AddRange(fromExplorer.Where(t => !knownIds.Contains(t.Txid)));

        // The explorer list is authoritative for explorer-derived outputs; outputs of proven transactions stay.
        var provenIds = merged.Where(t => t.Origin == SyncOrigin.GatewayProven).Select(t => t.Txid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outpoints = (existing?.UnspentOutpoints ?? []).Where(o => provenIds.Contains(o.Txid)).ToList();
        foreach (var utxo in unspent)
            if (outpoints.All(o => !(o.Txid.Equals(utxo.TxId, StringComparison.OrdinalIgnoreCase) && o.Vout == (int)utxo.OutputIndex)))
                outpoints.Add(new SyncOutpoint(utxo.TxId, (int)utxo.OutputIndex, utxo.AmountSatoshis, utxo.ScriptPubKey));

        var height = existing is not null && existing.LastSyncedHeight >= syncedHeight ? existing.LastSyncedHeight : syncedHeight;
        var hash = height == syncedHeight ? syncedHeader.Hash.ToString() : existing!.LastSyncedHash;

        await states.SaveAsync(new WalletSyncState(walletId, height, hash,
            merged.OrderBy(t => t.Height == 0 ? int.MaxValue : t.Height).ToList(), outpoints, _time.GetUtcNow(), existing?.SpentOutpoints));
        return BootstrapOutcome.Recorded;
    }

    /// <summary>
    /// An explorer transaction as a record. Its height is the explorer tip minus its confirmations plus one; the block hash comes from the
    /// wallet own headers when they reach that block, and is left empty when the block is older than they do.
    /// </summary>
    private SyncTransaction ToRecord(TransactionInfo tx, int explorerTip)
    {
        if (tx.Confirmations <= 0)
            return new SyncTransaction(tx.TxId, 0, string.Empty, null, SyncOrigin.ExplorerSourced);
        var height = Math.Max(1, explorerTip - tx.Confirmations + 1);
        var hash = headers.HeaderAt(height)?.Hash.ToString() ?? string.Empty;
        return new SyncTransaction(tx.TxId, height, hash, null, SyncOrigin.ExplorerSourced);
    }
}

using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Records a transaction the wallet itself broadcast, at once, in the wallet record (spec FR-009a, T087): it appears as sent and not yet
/// in a block, the outputs it spends are no longer offered, and its change is spendable straight away (the one exception to the 6-block
/// rule). The gateway does not report payments from others until they are in a block, so without this a wallet that has just sent would
/// show its old coins as still available. Once a block shows the transaction, the next refresh replaces the entry with the proven one.
/// Nothing here contacts any source, and a failure here never affects the send.
/// </summary>
public sealed class GatewayOwnSendRecorder(WalletSyncStateStore states, Network? network = null, TimeProvider? time = null)
{
    private readonly Network _network = network ?? DigiByteNetwork.Mainnet;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Records the transaction. Returns false when there was nothing to do: the wallet has no record yet (the bootstrap will create it
    /// and the explorer will show the transaction), or the transaction is already in the record.
    /// </summary>
    public async Task<bool> RecordAsync(GatewayWalletScope scope, byte[] rawTransaction)
    {
        var state = await states.GetAsync(scope.WalletId);
        if (state is null)
            return false;

        var tx = Transaction.Load(rawTransaction, _network);
        var txid = tx.GetHash().ToString();
        if (state.Transactions.Any(t => t.Txid == txid))
            return false;

        var scripts = scope.Addresses.Select(a => BitcoinAddress.Create(a, _network).ScriptPubKey.ToHex()).ToHashSet();
        var spentKeys = tx.Inputs.Select(i => $"{i.PrevOut.Hash}:{i.PrevOut.N}").ToHashSet();

        // What it spends leaves the unspent list and is remembered (amount and script) so a history row can name the inputs.
        var spentNow = state.UnspentOutpoints.Where(o => spentKeys.Contains(o.Key)).ToList();
        var unspent = state.UnspentOutpoints.Where(o => !spentKeys.Contains(o.Key)).ToList();

        // Every output it pays back to the wallet is spendable at once, flagged as change of an own send.
        for (var n = 0; n < tx.Outputs.Count; n++)
        {
            var output = tx.Outputs[n];
            if (scripts.Contains(output.ScriptPubKey.ToHex()))
                unspent.Add(new SyncOutpoint(txid, n, output.Value.Satoshi, output.ScriptPubKey.ToHex(), IsChangeToSelf: true));
        }

        var transactions = state.Transactions.Append(new SyncTransaction(txid, 0, string.Empty, Convert.ToHexString(rawTransaction).ToLowerInvariant(), SyncOrigin.GatewayProven, IsOwnSend: true)).ToList();
        var spent = (state.SpentOutpoints ?? []).Concat(spentNow).DistinctBy(o => o.Key).ToList();
        await states.SaveAsync(state with { Transactions = transactions, UnspentOutpoints = unspent, SpentOutpoints = spent, UpdatedAt = _time.GetUtcNow() });
        return true;
    }

    /// <summary>
    /// Takes a recorded own send back out (it was rejected): the transaction and the change it created go, and the outputs it spent are
    /// offered again. Returns false when no such own send is recorded.
    /// </summary>
    public async Task<bool> RemoveAsync(string walletId, string txid)
    {
        var state = await states.GetAsync(walletId);
        var own = state?.Transactions.FirstOrDefault(t => t.Txid == txid && t.IsOwnSend && t.Height == 0);
        if (state is null || own?.Raw is null)
            return false;

        var tx = Transaction.Parse(own.Raw, _network);
        var spentKeys = tx.Inputs.Select(i => $"{i.PrevOut.Hash}:{i.PrevOut.N}").ToHashSet();
        var restored = (state.SpentOutpoints ?? []).Where(o => spentKeys.Contains(o.Key)).ToList();
        var spent = (state.SpentOutpoints ?? []).Where(o => !spentKeys.Contains(o.Key)).ToList();
        var unspent = state.UnspentOutpoints.Where(o => o.Txid != txid).Concat(restored).DistinctBy(o => o.Key).ToList();

        await states.SaveAsync(state with
        {
            Transactions = state.Transactions.Where(t => t.Txid != txid).ToList(),
            UnspentOutpoints = unspent,
            SpentOutpoints = spent,
            UpdatedAt = _time.GetUtcNow(),
        });
        return true;
    }
}

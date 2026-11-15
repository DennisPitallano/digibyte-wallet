using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>Where the wallet asks for scans. The gateway client is the real one; tests use a scripted one.</summary>
public interface IGatewayScanner
{
    Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The gateway cannot answer this particular question correctly right now (no sync state, headers behind, a history row it cannot
/// complete). It says nothing against the gateway, so the caller asks the next source and does not count it as a failed read.
/// </summary>
public class GatewayDeclinedException(string message) : Exception(message);

public enum SyncUnavailableReason
{
    /// <summary>The wallet has no sync state yet: it must be created by an explorer-served refresh first.</summary>
    NoState,

    /// <summary>The state is older than the gateway keeps blocks for: the wallet uses its other sources until the state is refreshed.</summary>
    StateTooOld,

    /// <summary>The wallet own header chain has not reached the block it needs: sync headers first.</summary>
    HeadersBehind,

    /// <summary>The wallet record cannot fully answer the question (for example an input of a history row is unknown).</summary>
    CannotAnswer,
}

/// <summary>The gateway cannot be used for this request, for a reason that says nothing against it. The caller uses another source.</summary>
public sealed class GatewaySyncUnavailableException(SyncUnavailableReason reason) : GatewayDeclinedException($"The gateway cannot answer for this wallet: {reason}.")
{
    public SyncUnavailableReason Reason { get; } = reason;
}

/// <summary>The gateway sent data that fails the wallet own checks. The caller marks the source faulty and uses the next one.</summary>
public sealed class GatewayFaultException(string message) : Exception(message);

/// <summary>What a refresh did. The new state has already been saved.</summary>
public sealed record IncrementalSyncResult(WalletSyncState State, int NewTransactions, int RemovedTransactions, int DisputedTransactions, int Requests);

/// <summary>
/// A routine refresh through the gateway (spec FR-009a, T072). It asks only for blocks from the last synced height minus 6, sends the
/// wallet unspent outpoints so spends of old outputs are found, splits long address and outpoint lists into requests of at most 100,
/// checks every returned transaction against the wallet own headers, rebuilds the affected part of the wallet record from what it
/// checked, and only then saves the record with the new height and hash. A refresh that fails part way saves nothing.
/// </summary>
public sealed class GatewayIncrementalSync(
    IGatewayScanner scanner, GatewayHeaderStore headers, GatewayProofChecker checker, WalletSyncStateStore states,
    Network? network = null, TimeProvider? time = null)
{
    /// <summary>Blocks re-read before the last synced height, so a shallow reorganization is picked up.</summary>
    public const int Margin = 6;

    /// <summary>The longest height range one request may cover (the gateway default).</summary>
    public const int RangePerRequest = 50_000;

    private const int ReorgFallbackBlocks = 100;
    private const int MaxSpentOutpointsKept = 20_000;

    private readonly Network _network = network ?? DigiByteNetwork.Mainnet;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<IncrementalSyncResult> RefreshAsync(string walletId, IReadOnlyList<string> addresses, IReadOnlySet<string>? changeAddresses = null, CancellationToken cancellationToken = default)
    {
        var state = await states.GetAsync(walletId) ?? throw new GatewaySyncUnavailableException(SyncUnavailableReason.NoState);
        var tipHeight = headers.TipHeight;
        if (state.LastSyncedHeight > tipHeight)
            throw new GatewaySyncUnavailableException(SyncUnavailableReason.HeadersBehind);

        var start = Math.Max(1, state.LastSyncedHeight - Margin);
        if (headers.HeaderAt(state.LastSyncedHeight)?.Hash.ToString() != state.LastSyncedHash)
            start = Math.Min(start, ForkStart(state));

        var scripts = addresses.Select(a => BitcoinAddress.Create(a, _network).ScriptPubKey).ToHashSet();
        var changeScripts = (changeAddresses ?? new HashSet<string>()).Select(a => BitcoinAddress.Create(a, _network).ScriptPubKey).ToHashSet();

        var scanned = await ScanAllAsync(addresses, start, tipHeight, state.UnspentOutpoints.Select(o => o.Key).ToList(), cancellationToken);
        var requests = scanned.Requests;
        var syncedTo = Math.Min(tipHeight, scanned.ReachedHeight);

        // Check every transaction against the wallet own headers before any of it is believed.
        var found = new Dictionary<string, (GatewayTransactionProof Proof, Transaction Tx)>();
        foreach (var proof in scanned.Transactions)
        {
            var result = checker.Check(proof);
            if (result == ProofCheckResult.HeaderNotVerified)
                throw new GatewaySyncUnavailableException(SyncUnavailableReason.HeadersBehind);
            if (result != ProofCheckResult.Valid)
                throw new GatewayFaultException($"The gateway sent a transaction whose proof was refused ({result}).");
            found[proof.Txid] = (proof, Transaction.Parse(proof.Raw, _network));
        }

        // The part of the record at or after the start is rebuilt from the scan; the rest is kept.
        var retained = new List<SyncTransaction>();
        var replacedIds = new HashSet<string>();
        var removed = 0;
        var disputed = 0;
        foreach (var tx in state.Transactions)
        {
            // A transaction not yet in a block (the wallet own send) is rebuilt only once a block shows it.
            var inScannedRange = tx.Height >= start || (tx.Height == 0 && found.ContainsKey(tx.Txid));
            if (!inScannedRange)
            {
                retained.Add(tx);
                continue;
            }
            replacedIds.Add(tx.Txid);
            if (found.ContainsKey(tx.Txid))
                continue; // replaced by the proven version below
            if (tx.Origin == SyncOrigin.ExplorerSourced)
            {
                retained.Add(tx with { Disputed = true }); // the explorer says it is there and the gateway chain says it is not
                disputed++;
            }
            else
            {
                removed++; // a proven transaction that is no longer in the chain (reorganization)
            }
        }
        // Own sends that are not in a block yet stay until a block shows them.
        foreach (var own in state.Transactions.Where(t => t.Height == 0 && t.IsOwnSend && !found.ContainsKey(t.Txid)))
            if (!retained.Contains(own))
                retained.Add(own);

        var existingIds = state.Transactions.Select(t => t.Txid).ToHashSet();
        var added = 0;
        foreach (var (txid, (proof, tx)) in found)
        {
            var wasOwn = state.Transactions.Any(t => t.Txid == txid && t.IsOwnSend);
            retained.Add(new SyncTransaction(txid, proof.Height, proof.BlockHash, proof.Raw, SyncOrigin.GatewayProven, Disputed: false, IsOwnSend: wasOwn));
            if (!existingIds.Contains(txid))
                added++;
        }

        // Unspent outputs: keep the ones from transactions that were not rebuilt, add the wallet outputs of the proven ones, drop what they spend.
        var outpoints = state.UnspentOutpoints.Where(o => !replacedIds.Contains(o.Txid)).ToList();
        foreach (var (txid, (_, tx)) in found)
        {
            var ownSend = retained.Any(t => t.Txid == txid && t.IsOwnSend);
            for (var n = 0; n < tx.Outputs.Count; n++)
            {
                var output = tx.Outputs[n];
                if (scripts.Contains(output.ScriptPubKey))
                    outpoints.Add(new SyncOutpoint(txid, n, output.Value.Satoshi, output.ScriptPubKey.ToHex(), ownSend && changeScripts.Contains(output.ScriptPubKey)));
            }
        }
        var spent = found.Values.SelectMany(f => f.Tx.Inputs).Select(i => $"{i.PrevOut.Hash}:{i.PrevOut.N}").ToHashSet();
        outpoints = outpoints.DistinctBy(o => o.Key).ToList();
        // What was spent is remembered (with amount and script) so a history row can name what a spending transaction took in.
        var spentNow = outpoints.Where(o => spent.Contains(o.Key)).ToList();
        outpoints = outpoints.Where(o => !spent.Contains(o.Key)).ToList();
        var spentKept = (state.SpentOutpoints ?? []).Concat(spentNow).DistinctBy(o => o.Key).TakeLast(MaxSpentOutpointsKept).ToList();

        // The record moves to the block the scan really reached, which is below the wallet tip when the gateway is behind it.
        var tipHash = headers.HeaderAt(syncedTo)?.Hash.ToString() ?? state.LastSyncedHash;
        var next = new WalletSyncState(walletId, Math.Max(syncedTo, state.LastSyncedHeight), headers.HeaderAt(syncedTo) is null ? state.LastSyncedHash : tipHash,
            retained.OrderBy(t => t.Height == 0 ? int.MaxValue : t.Height).ToList(), outpoints, _time.GetUtcNow(), spentKept);
        await states.SaveAsync(next);
        return new IncrementalSyncResult(next, added, removed, disputed, requests);
    }

    /// <summary>
    /// The recorded block hash is no longer the wallet verified one: a reorganization went deeper than the margin. Start before the first
    /// recorded block that no longer matches, or a fixed distance back when none can be told apart.
    /// </summary>
    private int ForkStart(WalletSyncState state)
    {
        var broken = state.Transactions
            .Where(t => t.Height > 0 && headers.HeaderAt(t.Height) is { } header && header.Hash.ToString() != t.BlockHash)
            .Select(t => t.Height)
            .ToList();
        return broken.Count > 0 ? Math.Max(1, broken.Min() - 1) : Math.Max(1, state.LastSyncedHeight - ReorgFallbackBlocks);
    }

    private async Task<(List<GatewayTransactionProof> Transactions, int Requests, int ReachedHeight)> ScanAllAsync(
        IReadOnlyList<string> addresses, int from, int to, IReadOnlyList<string> outpoints, CancellationToken cancellationToken)
    {
        var all = new Dictionary<string, GatewayTransactionProof>();
        var requests = 0;
        var reached = to;
        var addressChunks = addresses.Chunk(GatewayClient.MaxPerRequest).ToList();
        var outpointChunks = outpoints.Chunk(GatewayClient.MaxPerRequest).ToList();
        var chunkCount = Math.Max(addressChunks.Count, outpointChunks.Count);
        if (addressChunks.Count == 0)
            return ([], 0, to);

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            // Outpoints ride along with an address chunk; extra outpoint chunks reuse the first one.
            var chunkAddresses = addressChunks[chunk < addressChunks.Count ? chunk : 0];
            var chunkOutpoints = chunk < outpointChunks.Count ? outpointChunks[chunk] : [];

            var cursor = from;
            while (cursor <= to)
            {
                var upper = (int)Math.Min((long)to, (long)cursor + RangePerRequest - 1);
                GatewayScan scan;
                try
                {
                    scan = await scanner.ScanAsync(chunkAddresses, cursor, upper, chunkOutpoints, cancellationToken);
                    requests++;
                }
                catch (GatewayRequestException e) when (e.IsRangeUnavailable)
                {
                    throw new GatewaySyncUnavailableException(SyncUnavailableReason.StateTooOld);
                }
                foreach (var tx in scan.Transactions)
                    all[tx.Txid] = tx;

                if (scan.Complete && scan.ScannedTo >= upper)
                {
                    cursor = upper + 1;
                    continue;
                }
                if (scan.ScannedTo < cursor)
                    throw new GatewayRequestException("unreadable", 200, "The gateway made no progress on a scan.");
                if (scan.Complete)
                {
                    reached = Math.Min(reached, scan.ScannedTo); // the gateway is behind the wallet headers: stop at what it has
                    break;
                }
                cursor = scan.ScannedTo + 1; // stopped early: continue where it stopped
            }
        }
        return (all.Values.ToList(), requests, reached);
    }
}

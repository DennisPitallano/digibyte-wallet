using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>Gives the restore scan the wallet addresses by index, receive and change. Public keys only: nothing secret is needed.</summary>
public interface IRestoreAddressSource
{
    string Receive(int index);

    string Change(int index);
}

public enum RestoreOutcome
{
    /// <summary>The gateway scan found every used address and the wallet record is written.</summary>
    Completed,

    /// <summary>The gateway does not keep blocks that far back: the caller restores through the existing explorer path instead.</summary>
    UseExplorer,
}

/// <summary>What a restore through the gateway did.</summary>
/// <param name="Outcome">Whether it finished or the explorer path must take over.</param>
/// <param name="ReceiveHighestUsed">The highest receive index with a payment, or -1.</param>
/// <param name="ChangeHighestUsed">The highest change index with a payment, or -1.</param>
/// <param name="TransactionsFound">How many distinct transactions were found.</param>
/// <param name="Message">An explanation to show the user when the explorer path takes over; null otherwise.</param>
public sealed record RestoreResult(RestoreOutcome Outcome, int ReceiveHighestUsed, int ChangeHighestUsed, int TransactionsFound, string? Message);

/// <summary>
/// Restores a wallet through the gateway (spec FR-008, T095): scans the blocks since the wallet birth height in chunks, extends the set of
/// addresses by the gap limit whenever a new one is used (rescanning the blocks already read for the new addresses, so nothing earlier is
/// missed), checks every returned transaction against the wallet own headers, and records progress after each whole chunk so an
/// interruption resumes there. When the gateway does not keep blocks that far back the restore hands over to the explorer path with an
/// explanation, and no funds are ever skipped silently: any other failure stops the restore with the progress kept. A finished scan builds
/// the wallet record through <see cref="GatewayIncrementalSync"/>.
/// </summary>
public sealed class GatewayRestoreScanner(
    IGatewayScanner scanner, GatewayHeaderStore headers, GatewayProofChecker checker, ScanProgressStore progressStore,
    GatewayIncrementalSync incremental, WalletSyncStateStore states, Network? network = null, int chunkBlocks = GatewayIncrementalSync.RangePerRequest)
{
    public const string ExplorerExplanation =
        "This wallet's history goes back further than the light-client gateway keeps blocks for, so the restore uses public block explorers instead. Nothing is skipped; it may take a while.";

    private readonly Network _network = network ?? DigiByteNetwork.Mainnet;

    public async Task<RestoreResult> RestoreAsync(string walletId, IRestoreAddressSource addresses, int startHeight, int gapLimit, ScanMode mode,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await headers.EnsureInitializedAsync();
        var tip = headers.TipHeight;
        var state = await progressStore.StartOrResumeAsync(walletId, startHeight, mode);
        var receiveUsed = state.ReceiveHighestUsed;
        var changeUsed = state.ChangeHighestUsed;
        var found = state.Transactions.ToDictionary(t => t.Txid, t => t);

        // The address set already read over [start, next): what the highest used indexes say it was.
        var receiveScanned = receiveUsed + 1 + gapLimit;
        var changeScanned = changeUsed + 1 + gapLimit;
        var scripts = new Dictionary<string, (bool Change, int Index)>();
        for (var i = 0; i < receiveScanned; i++)
            scripts[Script(addresses.Receive(i))] = (false, i);
        for (var i = 0; i < changeScanned; i++)
            scripts[Script(addresses.Change(i))] = (true, i);

        try
        {
            for (var chunkStart = state.NextHeight; chunkStart <= tip; chunkStart += chunkBlocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkEnd = (int)Math.Min((long)tip, (long)chunkStart + chunkBlocks - 1);

                // 1. Every address known so far, over this chunk.
                var everything = scripts.Keys.Select(s => AddressOf(s, addresses, scripts)).ToList();
                AddAll(found, await ScanAsync(everything, chunkStart, chunkEnd, cancellationToken), scripts, ref receiveUsed, ref changeUsed);

                // 2. A newly used address opens up more addresses; those are read over everything seen so far, this chunk included.
                while (receiveUsed + 1 + gapLimit > receiveScanned || changeUsed + 1 + gapLimit > changeScanned)
                {
                    var added = new List<string>();
                    for (var i = receiveScanned; i < receiveUsed + 1 + gapLimit; i++)
                    {
                        var address = addresses.Receive(i);
                        scripts[Script(address)] = (false, i);
                        added.Add(address);
                    }
                    for (var i = changeScanned; i < changeUsed + 1 + gapLimit; i++)
                    {
                        var address = addresses.Change(i);
                        scripts[Script(address)] = (true, i);
                        added.Add(address);
                    }
                    receiveScanned = Math.Max(receiveScanned, receiveUsed + 1 + gapLimit);
                    changeScanned = Math.Max(changeScanned, changeUsed + 1 + gapLimit);
                    AddAll(found, await ScanAsync(added, state.StartHeight, chunkEnd, cancellationToken), scripts, ref receiveUsed, ref changeUsed);
                }

                // The chunk is finished: only now does the resume point move.
                await progressStore.AdvanceAsync(walletId, chunkEnd + 1, receiveUsed, changeUsed, found.Values.ToList());
                progress?.Report(tip <= state.StartHeight ? 1 : Math.Min(1.0, (chunkEnd - state.StartHeight + 1) / (double)(tip - state.StartHeight + 1)));
            }
        }
        catch (GatewaySyncUnavailableException e) when (e.Reason == SyncUnavailableReason.StateTooOld)
        {
            await progressStore.SetSourceAsync(walletId, ScanSource.Explorer);
            return new RestoreResult(RestoreOutcome.UseExplorer, receiveUsed, changeUsed, found.Count, ExplorerExplanation);
        }

        await BuildRecordAsync(walletId, addresses, receiveUsed, changeUsed, gapLimit, state.StartHeight, cancellationToken);
        await progressStore.DeleteAsync(walletId);
        return new RestoreResult(RestoreOutcome.Completed, receiveUsed, changeUsed, found.Count, null);
    }

    /// <summary>Writes the wallet record for the addresses found: an empty record that starts at the birth height, then one incremental refresh.</summary>
    private async Task BuildRecordAsync(string walletId, IRestoreAddressSource addresses, int receiveUsed, int changeUsed, int gapLimit, int startHeight, CancellationToken cancellationToken)
    {
        var receive = Enumerable.Range(0, receiveUsed + 1 + gapLimit).Select(addresses.Receive).ToList();
        var change = Enumerable.Range(0, changeUsed + 1 + gapLimit).Select(addresses.Change).ToList();

        // The record starts just ahead of the birth height, so the first refresh reads from the birth height itself.
        var anchor = Math.Clamp(startHeight + GatewayIncrementalSync.Margin, headers.FirstHeight, headers.TipHeight);
        var header = headers.HeaderAt(anchor) ?? throw new GatewaySyncUnavailableException(SyncUnavailableReason.HeadersBehind);
        await states.SaveAsync(new WalletSyncState(walletId, anchor, header.Hash.ToString(), [], [], DateTimeOffset.UtcNow));

        await incremental.RefreshAsync(walletId, receive.Concat(change).ToList(), change.ToHashSet(), cancellationToken);
    }

    private string Script(string address) => BitcoinAddress.Create(address, _network).ScriptPubKey.ToHex();

    private static string AddressOf(string scriptHex, IRestoreAddressSource addresses, Dictionary<string, (bool Change, int Index)> scripts) =>
        scripts[scriptHex].Change ? addresses.Change(scripts[scriptHex].Index) : addresses.Receive(scripts[scriptHex].Index);

    /// <summary>Reads the addresses over a height range in groups of at most 100 and heights of at most one chunk, checking every proof.</summary>
    private async Task<List<(GatewayTransactionProof Proof, Transaction Tx)>> ScanAsync(IReadOnlyList<string> addressList, int from, int to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (GatewayTransactionProof, Transaction)>();
        if (from > to || addressList.Count == 0)
            return [];
        foreach (var group in addressList.Chunk(GatewayClient.MaxPerRequest))
        {
            var cursor = from;
            while (cursor <= to)
            {
                var upper = (int)Math.Min((long)to, (long)cursor + chunkBlocks - 1);
                GatewayScan scan;
                try
                {
                    scan = await scanner.ScanAsync(group, cursor, upper, [], cancellationToken);
                }
                catch (GatewayRequestException e) when (e.IsRangeUnavailable)
                {
                    throw new GatewaySyncUnavailableException(SyncUnavailableReason.StateTooOld);
                }
                foreach (var proof in scan.Transactions)
                {
                    var check = checker.Check(proof);
                    if (check == ProofCheckResult.HeaderNotVerified)
                        throw new GatewaySyncUnavailableException(SyncUnavailableReason.HeadersBehind);
                    if (check != ProofCheckResult.Valid)
                        throw new GatewayFaultException($"The gateway sent a transaction whose proof was refused ({check}).");
                    result[proof.Txid] = (proof, Transaction.Parse(proof.Raw, _network));
                }
                if (scan.Complete && scan.ScannedTo >= upper)
                {
                    cursor = upper + 1;
                    continue;
                }
                if (scan.ScannedTo < cursor)
                    throw new GatewayRequestException("unreadable", 200, "The gateway made no progress on a scan.");
                if (scan.Complete)
                    break; // the gateway is behind the wallet headers: it has nothing more in this range
                cursor = scan.ScannedTo + 1;
            }
        }
        return result.Values.ToList();
    }

    private void AddAll(Dictionary<string, SyncTransaction> found, List<(GatewayTransactionProof Proof, Transaction Tx)> txs,
        Dictionary<string, (bool Change, int Index)> scripts, ref int receiveUsed, ref int changeUsed)
    {
        foreach (var (proof, tx) in txs)
        {
            found[proof.Txid] = new SyncTransaction(proof.Txid, proof.Height, proof.BlockHash, proof.Raw, SyncOrigin.GatewayProven);
            foreach (var output in tx.Outputs)
            {
                if (!scripts.TryGetValue(output.ScriptPubKey.ToHex(), out var slot))
                    continue;
                if (slot.Change)
                    changeUsed = Math.Max(changeUsed, slot.Index);
                else
                    receiveUsed = Math.Max(receiveUsed, slot.Index);
            }
        }
    }
}

using System.Diagnostics;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Endpoints;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Sync;
using NBitcoin;

namespace DigiByte.SpvGateway.Matching;

public sealed record MerkleProofDto(int Index, IReadOnlyList<string> Branch);

public sealed record ScanTransactionDto(string Txid, int Height, string BlockHash, string Raw, MerkleProofDto Proof);

public sealed record ScanResponse(int TipHeight, int ScannedFrom, int ScannedTo, bool Complete, IReadOnlyList<ScanTransactionDto> Transactions);

public sealed record UtxoDto(string Txid, int Vout, long Amount, string ScriptPubKey, int Height, string BlockHash, MerkleProofDto Proof);

public sealed record UtxoResponse(int TipHeight, IReadOnlyList<UtxoDto> Utxos);

/// <summary>
/// Answers scans and unspent-output queries from the match index (spec FR-004, T064). It keeps nothing: the addresses, the outpoints and
/// the result exist only for the duration of the call, and nothing derived from them is logged or sent to a peer. The index tells which
/// blocks matter; each such block is then fetched and checked, and every returned transaction comes with its merkle branch.
/// </summary>
public sealed class ScanService(SpvGatewayOptions options, HeaderStore headers, BlockIndexStore index, IBlockMatcher matcher, BlockFetcher fetcher)
{
    public const int MaxOutpoints = 100;

    public async Task<ScanResponse> ScanAsync(IReadOnlyList<BitcoinAddress> addresses, int fromHeight, int toHeight, IReadOnlyList<OutPoint> knownOutpoints, CancellationToken cancellationToken)
    {
        var (tip, indexedFrom, indexedTo) = Coverage();
        var scripts = addresses.Select(a => ScriptKey.Of(a.ScriptPubKey)).ToList();
        var outpoints = knownOutpoints.Select(OutpointKey.Of).ToList();
        return await ScanCoreAsync(scripts, outpoints, fromHeight, toHeight, tip, indexedFrom, indexedTo, cancellationToken);
    }

    public async Task<UtxoResponse> UtxosAsync(IReadOnlyList<BitcoinAddress> addresses, int fromHeight, CancellationToken cancellationToken)
    {
        var (tip, indexedFrom, indexedTo) = Coverage();
        var wanted = addresses.Select(a => a.ScriptPubKey).ToList();
        var scripts = wanted.Select(ScriptKey.Of).ToList();

        var scan = await ScanCoreAsync(scripts, [], fromHeight, tip, tip, indexedFrom, indexedTo, cancellationToken);
        if (!scan.Complete)
            throw new GatewayException(GatewayError.NotReady, "The gateway has not finished indexing the latest blocks. Try again shortly.", 15);

        var candidates = new List<(UtxoDto Utxo, OutPoint Outpoint)>();
        var parsed = new List<(ScanTransactionDto Dto, Transaction Tx)>();
        foreach (var dto in scan.Transactions)
        {
            var tx = Transaction.Load(Convert.FromHexString(dto.Raw), DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet);
            parsed.Add((dto, tx));
            for (var n = 0; n < tx.Outputs.Count; n++)
            {
                var output = tx.Outputs[n];
                if (!wanted.Any(w => w == output.ScriptPubKey))
                    continue;
                candidates.Add((new UtxoDto(dto.Txid, n, output.Value.Satoshi, output.ScriptPubKey.ToHex(), dto.Height, dto.BlockHash, dto.Proof), new OutPoint(tx.GetHash(), n)));
            }
        }
        if (candidates.Count == 0)
            return new UtxoResponse(tip, []);

        // An output is spent if some later transaction in the window has it as an input. The short key finds candidates; the exact outpoint decides.
        var spent = new HashSet<OutPoint>();
        var spendScan = await ScanCoreAsync([], candidates.Select(c => OutpointKey.Of(c.Outpoint)).ToList(), fromHeight, tip, tip, indexedFrom, indexedTo, cancellationToken);
        if (!spendScan.Complete)
            throw new GatewayException(GatewayError.NotReady, "The gateway has not finished indexing the latest blocks. Try again shortly.", 15);
        foreach (var dto in spendScan.Transactions)
        {
            var tx = Transaction.Load(Convert.FromHexString(dto.Raw), DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet);
            foreach (var input in tx.Inputs)
                spent.Add(input.PrevOut);
        }

        return new UtxoResponse(tip, candidates.Where(c => !spent.Contains(c.Outpoint)).Select(c => c.Utxo).ToList());
    }

    private (int Tip, int IndexedFrom, int IndexedTo) Coverage()
    {
        var tip = headers.BestTip()?.Height;
        var from = index.IndexedFromHeight();
        var to = index.IndexedToHeight();
        if (tip is null || from is null || to is null)
            throw new GatewayException(GatewayError.NotReady, "The gateway has not indexed any blocks yet.", 30);
        return (tip.Value, from.Value, to.Value);
    }

    private async Task<ScanResponse> ScanCoreAsync(IReadOnlyCollection<byte[]> scripts, IReadOnlyCollection<byte[]> outpoints, int fromHeight, int toHeight,
        int tip, int indexedFrom, int indexedTo, CancellationToken cancellationToken)
    {
        if (fromHeight < 0 || toHeight < fromHeight)
            throw new GatewayException(GatewayError.BadRequest, "The height range is not valid.");
        if (toHeight - fromHeight + 1 > options.ScanRangeCapBlocks)
            throw new GatewayException(GatewayError.OverLimit, $"The range is too long (max {options.ScanRangeCapBlocks} blocks).");

        // Spends of outpoints may be older than the window (knownOutpoints); script matches may not be, because the index would hide them.
        var needsWindow = scripts.Count > 0 || outpoints.Count > 0;
        if (needsWindow && fromHeight < indexedFrom)
            throw new GatewayException(GatewayError.RangeUnavailable, $"Blocks below height {indexedFrom} are not available from this gateway. Use another source for that range.");

        var effectiveTo = Math.Min(Math.Min(toHeight, tip), indexedTo);
        var hits = effectiveTo < fromHeight ? [] : matcher.Match(scripts, outpoints, fromHeight, effectiveTo);

        var clock = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(options.ScanBudgetSeconds);
        var results = new List<ScanTransactionDto>();
        var scannedTo = effectiveTo;
        var complete = effectiveTo >= Math.Min(toHeight, tip);

        foreach (var group in hits.GroupBy(h => h.Height).OrderBy(g => g.Key))
        {
            if (clock.Elapsed > budget)
            {
                scannedTo = group.Key - 1;
                complete = false;
                break;
            }
            var block = await fetcher.FetchAsync(group.Key, cancellationToken);
            if (block is null)
            {
                scannedTo = group.Key - 1;
                complete = false;
                break;
            }

            var txids = block.Transactions.Select(t => t.GetHash()).ToList();
            foreach (var hit in group)
            {
                if (hit.TxIndex >= block.Transactions.Count)
                    continue;
                var tx = block.Transactions[hit.TxIndex];
                var branch = MerkleProof.BuildBranch(txids, hit.TxIndex);
                results.Add(new ScanTransactionDto(
                    txids[hit.TxIndex].ToString(), group.Key, block.GetHash().ToString(), tx.ToHex(),
                    new MerkleProofDto(hit.TxIndex, branch.Select(b => b.ToString()).ToList())));
            }
        }

        return new ScanResponse(tip, fromHeight, Math.Max(scannedTo, fromHeight - 1), complete, results);
    }
}

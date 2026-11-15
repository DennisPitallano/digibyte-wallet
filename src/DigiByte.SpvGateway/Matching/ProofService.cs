using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Sync;
using NBitcoin;

namespace DigiByte.SpvGateway.Matching;

public sealed record ProofItemDto(string Txid, int Height, string BlockHash, string Raw, MerkleProofDto Proof);

public sealed record UnavailableDto(string Txid, string Reason);

public sealed record ProofsResponse(IReadOnlyList<ProofItemDto> Proofs, IReadOnlyList<UnavailableDto> Unavailable);

public sealed record TransactionResponse(string Txid, string Raw, int? Height, string? BlockHash, MerkleProofDto? Proof);

/// <summary>
/// Proves specific transactions the wallet already knows about (spec FR-012a, T066) and looks transactions up by id (T065). A proof
/// needs only the single block at the given height, so it works for any age a full-history peer still serves; it never needs the index.
/// Nothing about a request is stored or logged.
/// </summary>
public sealed class ProofService(HeaderStore headers, BlockIndexStore index, BlockFetcher fetcher)
{
    public const int MaxItems = 20;

    public async Task<ProofsResponse> ProveAsync(IReadOnlyList<(uint256 Txid, int Height)> items, CancellationToken cancellationToken)
    {
        var proofs = new List<ProofItemDto>();
        var unavailable = new List<UnavailableDto>();
        var blocks = new Dictionary<int, Block?>();

        foreach (var (txid, height) in items)
        {
            if (headers.GetBest(height) is null)
            {
                unavailable.Add(new UnavailableDto(txid.ToString(), "header_unknown"));
                continue;
            }
            if (!blocks.TryGetValue(height, out var block))
                blocks[height] = block = await fetcher.FetchAsync(height, cancellationToken);
            if (block is null)
            {
                unavailable.Add(new UnavailableDto(txid.ToString(), "block_not_served"));
                continue;
            }
            var found = Build(block, height, txid);
            if (found is null)
                unavailable.Add(new UnavailableDto(txid.ToString(), "not_in_block"));
            else
                proofs.Add(found);
        }
        return new ProofsResponse(proofs, unavailable);
    }

    /// <summary>The transaction with its proof when it is in an indexed block, or null when the gateway does not know it.</summary>
    public async Task<TransactionResponse?> GetTransactionAsync(uint256 txid, CancellationToken cancellationToken)
    {
        foreach (var (height, _) in index.FindByTxid(TxidKey.Of(txid)))
        {
            var block = await fetcher.FetchAsync(height, cancellationToken);
            if (block is null)
                continue;
            if (Build(block, height, txid) is { } item)
                return new TransactionResponse(item.Txid, item.Raw, item.Height, item.BlockHash, item.Proof);
        }
        return null;
    }

    private static ProofItemDto? Build(Block block, int height, uint256 txid)
    {
        var ids = block.Transactions.Select(t => t.GetHash()).ToList();
        var position = ids.IndexOf(txid);
        if (position < 0)
            return null;
        var branch = MerkleProof.BuildBranch(ids, position);
        return new ProofItemDto(txid.ToString(), height, block.GetHash().ToString(), block.Transactions[position].ToHex(),
            new MerkleProofDto(position, branch.Select(b => b.ToString()).ToList()));
    }
}

using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>How many payments need the viewer's signature, per wallet and in total (specs/018-multisig-ux-polish, FR-012).</summary>
public sealed class AttentionSummary
{
    private readonly Dictionary<string, int> _perWallet;

    public AttentionSummary(Dictionary<string, int> perWallet)
    {
        _perWallet = perWallet;
        Total = perWallet.Values.Sum();
    }

    public int Total { get; }

    public int For(string walletId) => _perWallet.GetValueOrDefault(walletId);

    public IReadOnlyList<AttentionCount> All => _perWallet.Select(kv => new AttentionCount(kv.Key, kv.Value)).ToList();
}

public static class AttentionCounter
{
    /// <param name="pending">Saved proposals of any wallets.</param>
    /// <param name="localKeyForWallet">The viewer's co-signer key in that wallet, or null when watch-only.</param>
    public static AttentionSummary Count(IEnumerable<PendingMultisigTransaction> pending, Func<string, string?> localKeyForWallet)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in pending)
        {
            if (item.Status != PendingTxStatus.AwaitingSignatures) continue;
            var key = localKeyForWallet(item.WalletId);
            if (string.IsNullOrEmpty(key)) continue;
            if (PendingStateClassifier.HasSigned(item, key)) continue;
            counts[item.WalletId] = counts.GetValueOrDefault(item.WalletId) + 1;
        }
        return new AttentionSummary(counts);
    }
}

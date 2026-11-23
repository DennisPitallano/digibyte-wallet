using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// How many payments wait for the viewer, per wallet and in total. "Needs signature" is the figure from
/// specs/018-multisig-ux-polish (FR-012). "Needs you" adds payments that are ready to broadcast and payments whose
/// broadcast failed: the number on the Pending tab, the More tab and the home shortcut
/// (specs/026-multisig-wallet-experience, FR-004 and FR-010a).
/// </summary>
public sealed class AttentionSummary
{
    private static readonly Dictionary<string, int> Empty = [];

    private readonly Dictionary<string, int> _needsSignature;
    private readonly Dictionary<string, int> _readyToBroadcast;
    private readonly Dictionary<string, int> _failedBroadcast;

    public AttentionSummary(Dictionary<string, int> perWallet, Dictionary<string, int>? readyToBroadcast = null,
        Dictionary<string, int>? failedBroadcast = null)
    {
        _needsSignature = perWallet;
        _readyToBroadcast = readyToBroadcast ?? Empty;
        _failedBroadcast = failedBroadcast ?? Empty;
        Total = perWallet.Values.Sum();
        NeedsYouTotal = Total + _readyToBroadcast.Values.Sum() + _failedBroadcast.Values.Sum();
    }

    /// <summary>Payments that need the viewer's signature, across all wallets.</summary>
    public int Total { get; }

    /// <summary>Payments that need the viewer's signature in one wallet.</summary>
    public int For(string walletId) => _needsSignature.GetValueOrDefault(walletId);

    public IReadOnlyList<AttentionCount> All => _needsSignature.Select(kv => new AttentionCount(kv.Key, kv.Value)).ToList();

    /// <summary>Same as <see cref="For"/>, named to sit next to the other figures.</summary>
    public int NeedsSignatureFor(string walletId) => For(walletId);

    /// <summary>Payments with enough signatures that nobody has broadcast yet.</summary>
    public int ReadyToBroadcastFor(string walletId) => _readyToBroadcast.GetValueOrDefault(walletId);

    /// <summary>Payments whose last broadcast failed and can be retried.</summary>
    public int FailedBroadcastFor(string walletId) => _failedBroadcast.GetValueOrDefault(walletId);

    /// <summary>Everything in one wallet that waits for the viewer: their signature, a broadcast, or a retry.</summary>
    public int NeedsYouFor(string walletId) => For(walletId) + ReadyToBroadcastFor(walletId) + FailedBroadcastFor(walletId);

    /// <summary><see cref="NeedsYouFor"/> across all wallets.</summary>
    public int NeedsYouTotal { get; }
}

public static class AttentionCounter
{
    /// <param name="pending">Saved proposals of any wallets.</param>
    /// <param name="localKeyForWallet">The viewer's co-signer key in that wallet, or null when watch-only.</param>
    /// <param name="coinsSpent">Says, by payment id, whether its coins are known to be already spent. Such a payment
    /// cannot go through, so it needs nobody (the same rule as <see cref="PendingGrouping"/>). Null when nothing is known.</param>
    public static AttentionSummary Count(IEnumerable<PendingMultisigTransaction> pending, Func<string, string?> localKeyForWallet,
        Func<string, bool>? coinsSpent = null)
    {
        var needsSignature = new Dictionary<string, int>();
        var ready = new Dictionary<string, int>();
        var failed = new Dictionary<string, int>();
        foreach (var item in pending)
        {
            switch (item.Status)
            {
                case PendingTxStatus.Failed:
                    Add(failed, item.WalletId);
                    break;
                case PendingTxStatus.ReadyToFinalize or PendingTxStatus.AwaitingSignatures when coinsSpent?.Invoke(item.Id) == true:
                    break;
                // Anyone holding the payment can broadcast it, with or without a key of their own.
                case PendingTxStatus.ReadyToFinalize:
                    Add(ready, item.WalletId);
                    break;
                case PendingTxStatus.AwaitingSignatures:
                    var key = localKeyForWallet(item.WalletId);
                    if (string.IsNullOrEmpty(key)) break;
                    if (PendingStateClassifier.HasSigned(item, key)) break;
                    Add(needsSignature, item.WalletId);
                    break;
            }
        }
        return new AttentionSummary(needsSignature, ready, failed);
    }

    private static void Add(Dictionary<string, int> counts, string walletId) =>
        counts[walletId] = counts.GetValueOrDefault(walletId) + 1;
}

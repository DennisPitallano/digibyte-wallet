using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>"Needs your signature" counts per wallet and in total (spec 018, FR-012).</summary>
public class MultisigUxAttentionTests
{
    private static PendingMultisigTransaction P(string wallet, PendingTxStatus status, params string[] signedBy) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        WalletId = wallet,
        PsbtBase64 = "",
        DestinationAddress = "dgb1q",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        Status = status,
        SignedBy = signedBy.ToList(),
    };

    private static string? Keys(string wallet) => wallet switch { "w1" => "02aa", "w2" => "02bb", _ => null };

    [Fact]
    public void Counts_only_open_items_you_have_not_signed()
    {
        var summary = AttentionCounter.Count(
        [
            P("w1", PendingTxStatus.AwaitingSignatures),
            P("w1", PendingTxStatus.AwaitingSignatures, "02aa"),
            P("w1", PendingTxStatus.ReadyToFinalize, "02bb", "02cc"),
            P("w1", PendingTxStatus.Broadcast),
            P("w1", PendingTxStatus.Cancelled),
            P("w1", PendingTxStatus.Failed),
        ], Keys);

        Assert.Equal(1, summary.For("w1"));
        Assert.Equal(1, summary.Total);
    }

    [Fact]
    public void Per_wallet_counts_and_total_add_up()
    {
        var summary = AttentionCounter.Count(
        [
            P("w1", PendingTxStatus.AwaitingSignatures),
            P("w1", PendingTxStatus.AwaitingSignatures),
            P("w2", PendingTxStatus.AwaitingSignatures),
        ], Keys);

        Assert.Equal(2, summary.For("w1"));
        Assert.Equal(1, summary.For("w2"));
        Assert.Equal(3, summary.Total);
    }

    [Fact]
    public void Wallets_without_a_local_key_count_zero()
    {
        var summary = AttentionCounter.Count([P("watch", PendingTxStatus.AwaitingSignatures)], Keys);
        Assert.Equal(0, summary.For("watch"));
        Assert.Equal(0, summary.Total);
    }

    [Fact]
    public void Key_match_ignores_case()
    {
        var summary = AttentionCounter.Count([P("w1", PendingTxStatus.AwaitingSignatures, "02AA")], Keys);
        Assert.Equal(0, summary.Total);
    }

    [Fact]
    public void Empty_input_is_zero()
    {
        var summary = AttentionCounter.Count([], Keys);
        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.For("anything"));
    }
}

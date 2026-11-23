using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How the Pending list is split: what needs the viewer first, then what waits on others, then cancelled
/// (spec 026, FR-018). A payment that was sent is in no group (FR-018a).
/// </summary>
public class MultisigUxPendingGroupingTests
{
    private const string Me = "02aa";

    private static PendingMultisigTransaction P(string id, PendingTxStatus status, int minutesAgo = 0, params string[] signedBy) => new()
    {
        Id = id,
        WalletId = "ms",
        PsbtBase64 = "",
        DestinationAddress = "dgb1q",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        Status = status,
        SignedBy = signedBy.ToList(),
        CreatedAt = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo),
    };

    private static string[] Ids(IEnumerable<PendingMultisigTransaction> items) => items.Select(p => p.Id).ToArray();

    [Fact]
    public void Needs_you_holds_your_signature_ready_to_broadcast_and_failed_broadcasts()
    {
        var groups = PendingGrouping.Group(
        [
            P("sign", PendingTxStatus.AwaitingSignatures),
            P("ready", PendingTxStatus.ReadyToFinalize, 1, "02bb", "02cc"),
            P("failed", PendingTxStatus.Failed, 2, Me, "02bb"),
        ], Me);

        Assert.Equal(["sign", "ready", "failed"], Ids(groups.NeedsYou));
        Assert.Empty(groups.WaitingForOthers);
        Assert.Empty(groups.Cancelled);
    }

    [Fact]
    public void A_payment_you_already_signed_waits_for_others()
    {
        var groups = PendingGrouping.Group([P("mine", PendingTxStatus.AwaitingSignatures, 0, Me)], Me);

        Assert.Empty(groups.NeedsYou);
        Assert.Equal(["mine"], Ids(groups.WaitingForOthers));
    }

    [Fact]
    public void Your_key_matches_in_any_case()
    {
        var groups = PendingGrouping.Group([P("mine", PendingTxStatus.AwaitingSignatures, 0, "02AA")], Me);

        Assert.Equal(["mine"], Ids(groups.WaitingForOthers));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_view_only_copy_cannot_sign_so_open_payments_wait_for_others(string? noKey)
    {
        var groups = PendingGrouping.Group(
        [
            P("open", PendingTxStatus.AwaitingSignatures),
            P("ready", PendingTxStatus.ReadyToFinalize, 1, "02bb", "02cc"),
        ], noKey);

        // Anyone holding a complete payment can broadcast it, so that one still needs the viewer.
        Assert.Equal(["ready"], Ids(groups.NeedsYou));
        Assert.Equal(["open"], Ids(groups.WaitingForOthers));
    }

    [Fact]
    public void Cancelled_payments_have_their_own_group()
    {
        var groups = PendingGrouping.Group(
        [
            P("gone", PendingTxStatus.Cancelled),
            P("sign", PendingTxStatus.AwaitingSignatures),
        ], Me);

        Assert.Equal(["gone"], Ids(groups.Cancelled));
        Assert.Equal(["sign"], Ids(groups.NeedsYou));
    }

    [Fact]
    public void A_sent_payment_is_in_no_group()
    {
        var groups = PendingGrouping.Group([P("sent", PendingTxStatus.Broadcast, 0, Me, "02bb")], Me);

        Assert.Empty(groups.NeedsYou);
        Assert.Empty(groups.WaitingForOthers);
        Assert.Empty(groups.Cancelled);
    }

    [Fact]
    public void Each_group_is_newest_first()
    {
        var groups = PendingGrouping.Group(
        [
            P("old", PendingTxStatus.AwaitingSignatures, 60),
            P("new", PendingTxStatus.AwaitingSignatures, 1),
            P("mid", PendingTxStatus.ReadyToFinalize, 30, "02bb", "02cc"),
            P("w-old", PendingTxStatus.AwaitingSignatures, 50, Me),
            P("w-new", PendingTxStatus.AwaitingSignatures, 5, Me),
            P("c-old", PendingTxStatus.Cancelled, 40),
            P("c-new", PendingTxStatus.Cancelled, 2),
        ], Me);

        Assert.Equal(["new", "mid", "old"], Ids(groups.NeedsYou));
        Assert.Equal(["w-new", "w-old"], Ids(groups.WaitingForOthers));
        Assert.Equal(["c-new", "c-old"], Ids(groups.Cancelled));
    }

    [Fact]
    public void Payments_created_at_the_same_moment_keep_their_order()
    {
        var groups = PendingGrouping.Group(
        [
            P("a", PendingTxStatus.AwaitingSignatures, 10),
            P("b", PendingTxStatus.AwaitingSignatures, 10),
            P("c", PendingTxStatus.AwaitingSignatures, 10),
        ], Me);

        Assert.Equal(["a", "b", "c"], Ids(groups.NeedsYou));
    }

    [Fact]
    public void Nothing_in_gives_three_empty_groups()
    {
        var groups = PendingGrouping.Group([], Me);

        Assert.Empty(groups.NeedsYou);
        Assert.Empty(groups.WaitingForOthers);
        Assert.Empty(groups.Cancelled);
        Assert.Equal(0, groups.Count);
    }

    [Fact]
    public void Count_is_everything_shown()
    {
        var groups = PendingGrouping.Group(
        [
            P("sign", PendingTxStatus.AwaitingSignatures),
            P("mine", PendingTxStatus.AwaitingSignatures, 0, Me),
            P("gone", PendingTxStatus.Cancelled),
            P("sent", PendingTxStatus.Broadcast),
        ], Me);

        Assert.Equal(3, groups.Count);
    }
}

using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A shared wallet's card on the list: its one status line, most urgent first, and the order the cards come in
/// (spec 026, FR-012 and FR-015).
/// </summary>
public class MultisigUxWalletCardTests
{
    private const string Me = "02aa";
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static PendingMultisigTransaction P(PendingTxStatus status, int minutesAgo = 0, string unsignedTxId = "", int required = 2,
        params string[] signedBy) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        WalletId = "ms",
        PsbtBase64 = "",
        DestinationAddress = "dgb1q",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = required,
        Status = status,
        SignedBy = signedBy.ToList(),
        UnsignedTxId = unsignedTxId,
        CreatedAt = Now.AddMinutes(-minutesAgo),
        UpdatedAt = Now.AddMinutes(-minutesAgo),
    };

    // ─── The status line ───

    [Fact]
    public void Nothing_pending_says_nothing()
    {
        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.None, 0), WalletCardStatus.For([], Me, null));
    }

    [Fact]
    public void Needs_your_signature_comes_before_everything_else_and_counts()
    {
        var line = WalletCardStatus.For(
        [
            P(PendingTxStatus.AwaitingSignatures),
            P(PendingTxStatus.AwaitingSignatures),
            P(PendingTxStatus.ReadyToFinalize, signedBy: ["02bb", "02cc"]),
            P(PendingTxStatus.Failed),
            P(PendingTxStatus.AwaitingSignatures, signedBy: [Me]),
        ], Me, null);

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.NeedsSignature, 2), line);
    }

    [Fact]
    public void Then_ready_to_broadcast()
    {
        var line = WalletCardStatus.For(
        [
            P(PendingTxStatus.ReadyToFinalize, signedBy: [Me, "02bb"]),
            P(PendingTxStatus.ReadyToFinalize, signedBy: ["02bb", "02cc"]),
            P(PendingTxStatus.Failed),
            P(PendingTxStatus.AwaitingSignatures, signedBy: [Me]),
        ], Me, null);

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.ReadyToBroadcast, 2), line);
    }

    [Fact]
    public void Then_a_failed_broadcast_waiting_for_a_retry()
    {
        var line = WalletCardStatus.For(
        [
            P(PendingTxStatus.Failed),
            P(PendingTxStatus.AwaitingSignatures, unsignedTxId: "room-tx", signedBy: [Me]),
        ], Me, "room-tx");

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.FailedBroadcast, 1), line);
    }

    [Fact]
    public void Then_a_live_room_this_device_has_joined()
    {
        var line = WalletCardStatus.For(
        [
            P(PendingTxStatus.AwaitingSignatures, unsignedTxId: "room-tx", signedBy: [Me]),
            P(PendingTxStatus.AwaitingSignatures, signedBy: [Me]),
        ], Me, "ROOM-TX");

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.LiveRoom, 1), line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("another-tx")]
    public void A_room_this_device_is_not_in_is_unknown_to_it(string? liveRoomTxId)
    {
        var line = WalletCardStatus.For([P(PendingTxStatus.AwaitingSignatures, unsignedTxId: "room-tx", signedBy: [Me])], Me, liveRoomTxId);

        Assert.Equal(WalletCardStatusKind.WaitingForOthers, line.Kind);
    }

    [Fact]
    public void A_payment_without_an_id_never_matches_a_room()
    {
        var line = WalletCardStatus.For([P(PendingTxStatus.AwaitingSignatures, unsignedTxId: "", signedBy: [Me])], Me, "");

        Assert.Equal(WalletCardStatusKind.WaitingForOthers, line.Kind);
    }

    [Fact]
    public void Waiting_for_others_counts_the_signatures_still_missing_on_the_newest_payment()
    {
        var line = WalletCardStatus.For(
        [
            P(PendingTxStatus.AwaitingSignatures, minutesAgo: 60, required: 3, signedBy: [Me, "02bb"]),   // older: 1 missing
            P(PendingTxStatus.AwaitingSignatures, minutesAgo: 5, required: 3, signedBy: [Me]),            // newest: 2 missing
        ], Me, null);

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.WaitingForOthers, 2), line);
    }

    [Fact]
    public void Sent_and_cancelled_payments_say_nothing()
    {
        var line = WalletCardStatus.For([P(PendingTxStatus.Broadcast, signedBy: [Me, "02bb"]), P(PendingTxStatus.Cancelled)], Me, null);

        Assert.Equal(WalletCardStatusKind.None, line.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_view_only_copy_is_never_told_it_needs_to_sign(string? noKey)
    {
        var line = WalletCardStatus.For([P(PendingTxStatus.AwaitingSignatures), P(PendingTxStatus.AwaitingSignatures)], noKey, null);

        Assert.Equal(WalletCardStatusKind.WaitingForOthers, line.Kind);
    }

    [Fact]
    public void A_view_only_copy_is_still_told_about_a_payment_ready_to_broadcast()
    {
        var line = WalletCardStatus.For([P(PendingTxStatus.ReadyToFinalize, signedBy: ["02bb", "02cc"])], null, null);

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.ReadyToBroadcast, 1), line);
    }

    // ─── Last activity ───

    [Fact]
    public void Last_activity_is_the_newest_saved_payment()
    {
        var created = Now.AddDays(-30);
        var last = SharedWalletOrder.LastActivity(created, [P(PendingTxStatus.Cancelled, minutesAgo: 600), P(PendingTxStatus.Broadcast, minutesAgo: 20)]);

        Assert.Equal(Now.AddMinutes(-20), last);
    }

    [Fact]
    public void With_no_payments_last_activity_is_when_the_wallet_was_made()
    {
        var created = Now.AddDays(-3);

        Assert.Equal(created, SharedWalletOrder.LastActivity(created, []));
    }

    [Fact]
    public void A_wallet_made_after_its_last_payment_counts_from_its_own_date()
    {
        // Imported again later: the wallet's own date is the more recent thing that happened.
        var created = Now;

        Assert.Equal(created, SharedWalletOrder.LastActivity(created, [P(PendingTxStatus.Broadcast, minutesAgo: 90)]));
    }

    // ─── The order of the cards ───

    private static WalletCardModel Card(string id, int needsYou = 0, int minutesAgo = 0) =>
        new(id, id, 2, 3, false, false, new WalletCardStatusLine(WalletCardStatusKind.None, 0), needsYou, Now.AddMinutes(-minutesAgo));

    private static string[] Ids(IEnumerable<WalletCardModel> cards) => cards.Select(c => c.Id).ToArray();

    [Fact]
    public void Wallets_that_need_you_come_first()
    {
        var sorted = SharedWalletOrder.Sort([Card("quiet-new", minutesAgo: 1), Card("needs-old", needsYou: 1, minutesAgo: 500)]);

        Assert.Equal(["needs-old", "quiet-new"], Ids(sorted));
    }

    [Fact]
    public void Inside_each_part_the_most_recent_comes_first()
    {
        var sorted = SharedWalletOrder.Sort(
        [
            Card("quiet-old", minutesAgo: 900),
            Card("needs-old", needsYou: 3, minutesAgo: 300),
            Card("quiet-new", minutesAgo: 10),
            Card("needs-new", needsYou: 1, minutesAgo: 5),
        ]);

        Assert.Equal(["needs-new", "needs-old", "quiet-new", "quiet-old"], Ids(sorted));
    }

    [Fact]
    public void How_many_need_you_does_not_reorder_only_whether_any_do()
    {
        var sorted = SharedWalletOrder.Sort([Card("one-recent", needsYou: 1, minutesAgo: 1), Card("many-older", needsYou: 9, minutesAgo: 60)]);

        Assert.Equal(["one-recent", "many-older"], Ids(sorted));
    }

    [Fact]
    public void Ties_keep_the_order_given()
    {
        var sorted = SharedWalletOrder.Sort([Card("a", minutesAgo: 30), Card("b", minutesAgo: 30), Card("c", minutesAgo: 30)]);

        Assert.Equal(["a", "b", "c"], Ids(sorted));
    }

    [Fact]
    public void Nothing_in_nothing_out()
    {
        Assert.Empty(SharedWalletOrder.Sort([]));
    }
}

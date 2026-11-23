using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Web.Components.Multisig;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A payment whose coins were already spent cannot go through. Once this device knows that, the payment stops being
/// counted as something that needs the person, gets its own group on the Pending list, and no longer speaks for its
/// wallet on the list of shared wallets. The only thing left to do with it is cancel it.
/// </summary>
public class MultisigUxUnusablePaymentTests
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
    private static Func<string, bool> Spent(params string[] ids) => id => ids.Contains(id);

    // ─── Counts ───

    [Fact]
    public void A_payment_that_cannot_go_through_is_not_counted_as_needing_you()
    {
        var summary = AttentionCounter.Count(
        [
            P("dead-sign", PendingTxStatus.AwaitingSignatures),
            P("dead-ready", PendingTxStatus.ReadyToFinalize, 0, "02bb", "02cc"),
            P("live-sign", PendingTxStatus.AwaitingSignatures),
        ], _ => Me, Spent("dead-sign", "dead-ready"));

        Assert.Equal(1, summary.NeedsSignatureFor("ms"));
        Assert.Equal(0, summary.ReadyToBroadcastFor("ms"));
        Assert.Equal(1, summary.NeedsYouTotal);
    }

    [Fact]
    public void A_failed_broadcast_still_counts_whatever_is_known_about_its_coins()
    {
        // Retrying asks the network again; this device does not second-guess it.
        var summary = AttentionCounter.Count([P("failed", PendingTxStatus.Failed)], _ => Me, Spent("failed"));

        Assert.Equal(1, summary.FailedBroadcastFor("ms"));
    }

    [Fact]
    public void With_nothing_known_about_spent_coins_every_open_payment_counts_as_before()
    {
        PendingMultisigTransaction[] items = [P("a", PendingTxStatus.AwaitingSignatures), P("b", PendingTxStatus.ReadyToFinalize, 0, "02bb", "02cc")];

        Assert.Equal(AttentionCounter.Count(items, _ => Me).NeedsYouTotal, AttentionCounter.Count(items, _ => Me, null).NeedsYouTotal);
        Assert.Equal(2, AttentionCounter.Count(items, _ => Me, _ => false).NeedsYouTotal);
    }

    // ─── The Pending list ───

    [Fact]
    public void On_the_Pending_list_it_gets_its_own_group()
    {
        var groups = PendingGrouping.Group(
        [
            P("dead-sign", PendingTxStatus.AwaitingSignatures, 1),
            P("dead-signed", PendingTxStatus.AwaitingSignatures, 2, Me),
            P("dead-ready", PendingTxStatus.ReadyToFinalize, 3, "02bb", "02cc"),
            P("live-sign", PendingTxStatus.AwaitingSignatures, 4),
            P("live-signed", PendingTxStatus.AwaitingSignatures, 5, Me),
        ], Me, Spent("dead-sign", "dead-signed", "dead-ready"));

        Assert.Equal(["live-sign"], Ids(groups.NeedsYou));
        Assert.Equal(["live-signed"], Ids(groups.WaitingForOthers));
        Assert.Equal(["dead-sign", "dead-signed", "dead-ready"], Ids(groups.CantGoThrough));
        Assert.Equal(5, groups.Count);
    }

    [Fact]
    public void Cancelled_and_failed_payments_stay_where_they_were()
    {
        var groups = PendingGrouping.Group(
        [
            P("cancelled", PendingTxStatus.Cancelled),
            P("failed", PendingTxStatus.Failed),
        ], Me, Spent("cancelled", "failed"));

        Assert.Equal(["cancelled"], Ids(groups.Cancelled));
        Assert.Equal(["failed"], Ids(groups.NeedsYou));
        Assert.Empty(groups.CantGoThrough);
    }

    [Fact]
    public void The_needs_you_group_and_the_needs_you_count_are_the_same_payments()
    {
        PendingMultisigTransaction[] items =
        [
            P("dead-sign", PendingTxStatus.AwaitingSignatures),
            P("live-sign", PendingTxStatus.AwaitingSignatures),
            P("ready", PendingTxStatus.ReadyToFinalize, 0, "02bb", "02cc"),
            P("failed", PendingTxStatus.Failed),
            P("mine", PendingTxStatus.AwaitingSignatures, 0, Me),
            P("cancelled", PendingTxStatus.Cancelled),
            P("sent", PendingTxStatus.Broadcast),
        ];
        var spent = Spent("dead-sign");

        var groups = PendingGrouping.Group(items, Me, spent);
        var summary = AttentionCounter.Count(items, _ => Me, spent);

        Assert.Equal(groups.NeedsYou.Count, summary.NeedsYouTotal);
    }

    [Fact]
    public void Without_knowledge_of_spent_coins_the_group_is_empty()
    {
        var groups = PendingGrouping.Group([P("a", PendingTxStatus.AwaitingSignatures)], Me);

        Assert.Empty(groups.CantGoThrough);
        Assert.Equal(["a"], Ids(groups.NeedsYou));
    }

    // ─── The shared wallet's card ───

    [Fact]
    public void It_does_not_speak_for_its_wallet_on_the_list()
    {
        var line = WalletCardStatus.For([P("dead", PendingTxStatus.AwaitingSignatures)], Me, null, Spent("dead"));

        Assert.Equal(WalletCardStatusKind.None, line.Kind);
    }

    [Fact]
    public void A_live_payment_next_to_it_still_does()
    {
        var line = WalletCardStatus.For(
        [
            P("dead", PendingTxStatus.AwaitingSignatures),
            P("live", PendingTxStatus.AwaitingSignatures),
        ], Me, null, Spent("dead"));

        Assert.Equal(new WalletCardStatusLine(WalletCardStatusKind.NeedsSignature, 1), line);
    }
}

/// <summary>The six-character room code: what is kept from what a person types, pastes or scans (spec 026, FR-034).</summary>
public class RoomCodeInputTests
{
    [Theory]
    [InlineData("3wau9y", "3WAU9Y")]
    [InlineData("3WAU9Y", "3WAU9Y")]
    [InlineData(" 3wau 9y ", "3WAU9Y")]
    [InlineData("3wau-9y", "3WAU9Y")]
    [InlineData("3-w-a-u-9-y", "3WAU9Y")]
    public void Letters_and_digits_are_kept_in_capitals(string typed, string expected)
    {
        Assert.Equal(expected, RoomCodeInput.Clean(typed));
    }

    [Theory]
    [InlineData("3wau9-", "3WAU9")]
    [InlineData("3wau9 ", "3WAU9")]
    public void A_stray_character_does_not_take_the_place_of_a_real_one(string typed, string expected)
    {
        // The sixth keystroke was a dash: five real characters, and room for the sixth.
        Assert.Equal(expected, RoomCodeInput.Clean(typed));
    }

    [Theory]
    [InlineData("3wau9yzz")]
    [InlineData("3wau9y-extra")]
    public void Anything_past_six_characters_is_dropped(string typed)
    {
        Assert.Equal("3WAU9Y", RoomCodeInput.Clean(typed));
    }

    [Theory]
    [InlineData("https://dgbwallet.app/multisig/sign-room?code=3wau9y")]
    [InlineData("https://dgbwallet.app/multisig/sign-room?code=3WAU9Y&from=share")]
    [InlineData("Join my DigiByte signing room. Code: 3WAU9Y https://dgbwallet.app/multisig/sign-room?code=3WAU9Y")]
    [InlineData("http://localhost:5257/multisig/sign-room?CODE=3wau9y#top")]
    public void A_pasted_invite_link_gives_its_code_not_the_start_of_the_link(string pasted)
    {
        Assert.Equal("3WAU9Y", RoomCodeInput.Clean(pasted));
    }

    [Fact]
    public void A_link_whose_code_is_short_gives_what_it_has()
    {
        Assert.Equal("3WA", RoomCodeInput.Clean("https://dgbwallet.app/multisig/sign-room?code=3wa&x=123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Nothing_usable_gives_an_empty_code(string? typed)
    {
        Assert.Equal("", RoomCodeInput.Clean(typed));
    }
}

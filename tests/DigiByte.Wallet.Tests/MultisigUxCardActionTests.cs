using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The one button on a pending payment's card (spec 026, FR-019). None of them signs, broadcasts or retries from
/// the list: they open the payment's own screen or its live room (FR-019a).
/// </summary>
public class MultisigUxCardActionTests
{
    [Theory]
    [InlineData(MultisigState.NeedsYourSignature, PendingCardAction.ReviewAndSign, "Review & sign")]
    [InlineData(MultisigState.LiveRoom, PendingCardAction.OpenRoom, "Open room")]
    [InlineData(MultisigState.YouSigned, PendingCardAction.GetSignatures, "Get signatures")]
    [InlineData(MultisigState.WaitingForOthers, PendingCardAction.View, "View")]
    [InlineData(MultisigState.ReadyToBroadcast, PendingCardAction.ReviewAndBroadcast, "Review & broadcast")]
    [InlineData(MultisigState.Failed, PendingCardAction.ReviewAndRetry, "Review & retry")]
    public void Each_state_has_its_one_button(MultisigState state, PendingCardAction action, string label)
    {
        var button = PendingCardActions.For(state, coinsSpent: false, isViewOnly: false);

        Assert.Equal(action, button.Action);
        Assert.Equal(label, button.Label);
    }

    [Theory]
    [InlineData(MultisigState.CancelledOrExpired)]
    [InlineData(MultisigState.Sent)]
    public void Finished_payments_have_no_button(MultisigState state)
    {
        var button = PendingCardActions.For(state, coinsSpent: false, isViewOnly: false);

        Assert.Equal(PendingCardAction.None, button.Action);
        Assert.Null(button.Label);
    }

    [Theory]
    [InlineData(MultisigState.NeedsYourSignature)]
    [InlineData(MultisigState.YouSigned)]
    [InlineData(MultisigState.WaitingForOthers)]
    [InlineData(MultisigState.ReadyToBroadcast)]
    public void When_the_coins_are_already_spent_the_only_thing_left_is_to_cancel(MultisigState state)
    {
        var button = PendingCardActions.For(state, coinsSpent: true, isViewOnly: false);

        Assert.Equal(PendingCardAction.ReviewAndCancel, button.Action);
        Assert.Equal("Review & cancel", button.Label);
    }

    [Fact]
    public void A_live_room_can_still_be_opened_when_the_coins_are_spent()
    {
        // The room itself says the payment can no longer complete; closing or leaving it happens there.
        Assert.Equal(PendingCardAction.OpenRoom, PendingCardActions.For(MultisigState.LiveRoom, coinsSpent: true, isViewOnly: false).Action);
    }

    [Theory]
    [InlineData(MultisigState.CancelledOrExpired)]
    [InlineData(MultisigState.Sent)]
    [InlineData(MultisigState.Failed)]
    public void Spent_coins_change_nothing_for_payments_that_are_not_open(MultisigState state)
    {
        Assert.Equal(
            PendingCardActions.For(state, coinsSpent: false, isViewOnly: false),
            PendingCardActions.For(state, coinsSpent: true, isViewOnly: false));
    }

    [Theory]
    [InlineData(MultisigState.NeedsYourSignature)]
    [InlineData(MultisigState.YouSigned)]
    [InlineData(MultisigState.WaitingForOthers)]
    [InlineData(MultisigState.LiveRoom)]
    public void A_view_only_copy_is_never_offered_signing_collecting_or_a_room(MultisigState state)
    {
        var button = PendingCardActions.For(state, coinsSpent: false, isViewOnly: true);

        Assert.Equal(PendingCardAction.View, button.Action);
        Assert.Equal("View", button.Label);
    }

    [Theory]
    [InlineData(MultisigState.ReadyToBroadcast, PendingCardAction.ReviewAndBroadcast)]
    [InlineData(MultisigState.Failed, PendingCardAction.ReviewAndRetry)]
    public void A_view_only_copy_can_still_broadcast_a_complete_payment(MultisigState state, PendingCardAction action)
    {
        Assert.Equal(action, PendingCardActions.For(state, coinsSpent: false, isViewOnly: true).Action);
    }

    [Fact]
    public void Only_the_room_button_leaves_for_somewhere_other_than_the_payments_own_screen()
    {
        foreach (var action in Enum.GetValues<PendingCardAction>())
            Assert.Equal(action != PendingCardAction.OpenRoom && action != PendingCardAction.None, PendingCardActions.OpensPaymentScreen(action));
    }
}

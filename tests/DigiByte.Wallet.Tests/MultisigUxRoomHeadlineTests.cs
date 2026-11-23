using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>One viewer-centric headline and at most one primary action per room view (spec 018, FR-003/FR-004/FR-019).</summary>
public class MultisigUxRoomHeadlineTests
{
    private static RoomHeadline Head(RoomPhase phase = RoomPhase.Open, bool signed = false, bool declined = false,
        int count = 0, int required = 2, bool unreachable = false, bool watchOnly = false) =>
        SigningRoomHeadline.For(phase, signed, declined, count, required, unreachable, watchOnly);

    [Fact]
    public void Not_signed_and_below_threshold_asks_for_your_signature()
    {
        var h = Head(count: 1);
        Assert.Equal("Your signature is needed", h.Title);
        Assert.Equal(PrimaryAction.ReviewAndSign, h.Action);
        Assert.Equal("Review & sign", h.ActionLabel);
        Assert.Equal(UxTone.Action, h.Tone);
    }

    [Fact]
    public void Signed_and_below_threshold_waits_without_a_button()
    {
        var h = Head(signed: true, count: 1, required: 3);
        Assert.Equal("Waiting for 2 more signatures", h.Title);
        Assert.Equal(PrimaryAction.None, h.Action);
    }

    [Fact]
    public void One_signature_missing_is_singular()
    {
        Assert.Equal("Waiting for 1 more signature", Head(signed: true, count: 1, required: 2).Title);
    }

    [Fact]
    public void A_signed_viewer_is_never_offered_sign_again()
    {
        foreach (var count in new[] { 0, 1, 2 })
            Assert.NotEqual(PrimaryAction.ReviewAndSign, Head(signed: true, count: count, required: 3).Action);
    }

    [Fact]
    public void Threshold_met_is_ready_to_broadcast()
    {
        var h = Head(phase: RoomPhase.Complete, count: 2);
        Assert.Equal("Ready to broadcast", h.Title);
        Assert.Equal(PrimaryAction.Broadcast, h.Action);
        Assert.Equal(UxTone.Positive, h.Tone);
    }

    [Fact]
    public void Threshold_met_by_count_even_while_phase_is_open()
    {
        Assert.Equal(PrimaryAction.Broadcast, Head(phase: RoomPhase.Open, count: 2, required: 2).Action);
    }

    [Fact]
    public void Unreachable_points_to_the_payment()
    {
        var h = Head(unreachable: true, count: 1);
        Assert.Equal(PrimaryAction.GoToPending, h.Action);
        Assert.Equal(UxTone.Warning, h.Tone);
        Assert.Contains("reach", h.Title);
        // Cancelling lives on the payment's own screen now, not on the Pending list (spec 026).
        Assert.Equal("Open payment", h.ActionLabel);
        Assert.Equal("Too many co-signers declined. You can cancel it from the payment's screen.", h.Detail);
    }

    [Fact]
    public void No_headline_sends_people_to_the_pending_list()
    {
        RoomHeadline[] all =
        [
            Head(), Head(signed: true, count: 1), Head(declined: true), Head(unreachable: true),
            Head(count: 2), Head(phase: RoomPhase.Complete, count: 2), Head(phase: RoomPhase.Broadcast), Head(watchOnly: true),
        ];
        foreach (var h in all)
        {
            Assert.DoesNotContain("Pending list", h.Title + " " + h.Detail + " " + h.ActionLabel);
            Assert.DoesNotContain("Go to Pending", h.ActionLabel ?? "");
        }
    }

    [Fact]
    public void Broadcast_phase_shows_sent_with_view()
    {
        var h = Head(phase: RoomPhase.Broadcast, count: 2);
        Assert.Equal("Sent", h.Title);
        Assert.Equal(PrimaryAction.View, h.Action);
    }

    [Fact]
    public void Watch_only_viewer_sees_view_only_and_no_action()
    {
        var h = Head(count: 0, watchOnly: true);
        Assert.Equal("View only", h.Title);
        Assert.Equal(PrimaryAction.None, h.Action);
    }

    [Fact]
    public void Watch_only_viewer_can_still_see_it_is_sent()
    {
        Assert.Equal("Sent", Head(phase: RoomPhase.Broadcast, watchOnly: true).Title);
    }

    [Fact]
    public void A_declined_viewer_has_no_sign_button()
    {
        var h = Head(declined: true, count: 1, required: 3);
        Assert.Equal(PrimaryAction.None, h.Action);
        Assert.Equal("You declined", h.Title);
    }
}

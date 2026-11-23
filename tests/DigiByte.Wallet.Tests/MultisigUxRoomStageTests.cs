using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Where a signing room stands on its three-step tracker (Invite, Sign, Broadcast), and when the host is shown the
/// invite up front (spec 026, FR-027 and FR-029).
/// </summary>
public class MultisigUxRoomStageTests
{
    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 0, 3)]
    public void Too_few_co_signers_here_yet_means_Invite(int presentOrSigned, int signed, int required)
    {
        Assert.Equal(RoomStageKind.Invite, RoomStage.For(RoomPhase.Open, presentOrSigned, signed, required));
    }

    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 3)]
    [InlineData(5, 0, 3)]
    public void Enough_co_signers_here_means_Sign(int presentOrSigned, int signed, int required)
    {
        Assert.Equal(RoomStageKind.Sign, RoomStage.For(RoomPhase.Open, presentOrSigned, signed, required));
    }

    [Theory]
    [InlineData(RoomPhase.Open, 2, 2, 2)]
    [InlineData(RoomPhase.Open, 3, 3, 2)]
    [InlineData(RoomPhase.Complete, 2, 2, 2)]
    public void Enough_signatures_means_Broadcast(RoomPhase phase, int presentOrSigned, int signed, int required)
    {
        Assert.Equal(RoomStageKind.Broadcast, RoomStage.For(phase, presentOrSigned, signed, required));
    }

    [Fact]
    public void A_complete_room_is_at_Broadcast_whatever_the_counts_say()
    {
        // The room's own word wins over a count this device has not caught up with yet.
        Assert.Equal(RoomStageKind.Broadcast, RoomStage.For(RoomPhase.Complete, 1, 1, 2));
    }

    [Fact]
    public void Enough_signatures_count_even_when_nobody_else_is_online()
    {
        // Signatures brought in from offline copies: the room opens already at the threshold.
        Assert.Equal(RoomStageKind.Broadcast, RoomStage.For(RoomPhase.Open, 1, 2, 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    public void A_sent_payment_is_Done(int presentOrSigned, int signed)
    {
        Assert.Equal(RoomStageKind.Done, RoomStage.For(RoomPhase.Broadcast, presentOrSigned, signed, 2));
    }

    [Fact]
    public void A_room_with_no_threshold_never_claims_to_be_ready()
    {
        Assert.Equal(RoomStageKind.Sign, RoomStage.For(RoomPhase.Open, 0, 0, 0));
    }

    // ─── The invite: up front for the host while people are missing, folded otherwise ───

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_host_sees_the_invite_up_front_while_co_signers_are_missing(int presentOrSigned)
    {
        Assert.True(RoomStage.InviteProminent(isHost: true, presentOrSigned, required: 2, RoomPhase.Open));
    }

    [Fact]
    public void Once_enough_co_signers_are_here_the_invite_folds()
    {
        Assert.False(RoomStage.InviteProminent(isHost: true, presentOrSigned: 2, required: 2, RoomPhase.Open));
        Assert.False(RoomStage.InviteProminent(isHost: true, presentOrSigned: 3, required: 2, RoomPhase.Open));
    }

    [Fact]
    public void Someone_who_joined_never_gets_the_invite_up_front()
    {
        Assert.False(RoomStage.InviteProminent(isHost: false, presentOrSigned: 1, required: 2, RoomPhase.Open));
    }

    [Theory]
    [InlineData(RoomPhase.Complete)]
    [InlineData(RoomPhase.Broadcast)]
    public void Nobody_needs_inviting_once_the_signatures_are_in(RoomPhase phase)
    {
        Assert.False(RoomStage.InviteProminent(isHost: true, presentOrSigned: 1, required: 2, phase));
    }

    // ─── Counting who is here ───

    private static SignerCardModel Card(string key, SignerPresence? presence, SignerStatus status) =>
        new(key, key, false, "?", null, presence, status, false);

    [Fact]
    public void Present_or_signed_counts_each_co_signer_once()
    {
        var count = RoomStage.PresentOrSigned(
        [
            Card("a", SignerPresence.Online, SignerStatus.Waiting),     // here
            Card("b", SignerPresence.Offline, SignerStatus.Signed),     // signed earlier, now away
            Card("c", SignerPresence.Online, SignerStatus.Signed),      // here and signed: once
            Card("d", SignerPresence.Offline, SignerStatus.Waiting),    // not here
            Card("e", SignerPresence.Offline, SignerStatus.Declined),   // declined and away
            Card("f", null, SignerStatus.Waiting),                      // presence unknown
        ]);

        Assert.Equal(3, count);
    }

    [Fact]
    public void Text_for_assistive_technology_names_the_step()
    {
        Assert.Equal("Step 1 of 3: Invite", RoomStage.Describe(RoomStageKind.Invite));
        Assert.Equal("Step 2 of 3: Sign", RoomStage.Describe(RoomStageKind.Sign));
        Assert.Equal("Step 3 of 3: Broadcast", RoomStage.Describe(RoomStageKind.Broadcast));
        Assert.Equal("All 3 steps done: payment sent", RoomStage.Describe(RoomStageKind.Done));
    }
}

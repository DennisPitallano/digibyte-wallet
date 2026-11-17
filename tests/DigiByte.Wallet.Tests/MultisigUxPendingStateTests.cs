using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Pending item + viewer -> one shared state (spec 018, FR-010/FR-011).</summary>
public class MultisigUxPendingStateTests
{
    private const string Me = "02aa";
    private const string Other = "02bb";

    private static PendingMultisigTransaction Pending(PendingTxStatus status, params string[] signedBy) => new()
    {
        Id = "p1",
        WalletId = "w1",
        PsbtBase64 = "",
        DestinationAddress = "dgb1qdest",
        AmountSatoshis = 100,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        Status = status,
        SignedBy = signedBy.ToList(),
    };

    [Fact]
    public void Open_and_not_signed_needs_your_signature()
    {
        Assert.Equal(MultisigState.NeedsYourSignature,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.AwaitingSignatures, Other), Me, false));
    }

    [Fact]
    public void Open_and_signed_by_you_is_you_signed()
    {
        Assert.Equal(MultisigState.YouSigned,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.AwaitingSignatures, Me), Me, false));
    }

    [Fact]
    public void Key_match_ignores_case()
    {
        Assert.Equal(MultisigState.YouSigned,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.AwaitingSignatures, "02AA"), Me, false));
    }

    [Fact]
    public void Ready_to_finalize_is_ready_to_broadcast()
    {
        Assert.Equal(MultisigState.ReadyToBroadcast,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.ReadyToFinalize, Me, Other), Me, false));
    }

    [Fact]
    public void Terminal_statuses_map_directly()
    {
        Assert.Equal(MultisigState.Sent, PendingStateClassifier.Classify(Pending(PendingTxStatus.Broadcast), Me, false));
        Assert.Equal(MultisigState.CancelledOrExpired, PendingStateClassifier.Classify(Pending(PendingTxStatus.Cancelled), Me, false));
        Assert.Equal(MultisigState.Failed, PendingStateClassifier.Classify(Pending(PendingTxStatus.Failed), Me, false));
    }

    [Fact]
    public void A_live_room_wins_while_the_item_is_still_open()
    {
        Assert.Equal(MultisigState.LiveRoom,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.AwaitingSignatures, Other), Me, true));
    }

    [Fact]
    public void A_live_room_does_not_hide_a_finished_item()
    {
        Assert.Equal(MultisigState.Sent, PendingStateClassifier.Classify(Pending(PendingTxStatus.Broadcast), Me, true));
    }

    [Fact]
    public void Watch_only_viewer_never_needs_to_sign()
    {
        Assert.Equal(MultisigState.WaitingForOthers,
            PendingStateClassifier.Classify(Pending(PendingTxStatus.AwaitingSignatures, Other), null, false));
    }
}

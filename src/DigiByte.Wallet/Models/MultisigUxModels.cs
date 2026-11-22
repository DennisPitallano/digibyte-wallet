namespace DigiByte.Wallet.Models;

// Presentation-only types for the multisig creation and signing screens (specs/018-multisig-ux-polish).
// Computed on the fly from data the rooms and the local store already have; nothing here is stored or sent.

public enum SignerPresence
{
    Online,
    Offline,
}

public enum SignerStatus
{
    /// <summary>Signing room: has not signed or declined yet.</summary>
    Waiting,
    Signed,
    Declined,
    /// <summary>Creation room: has joined but is not ready.</summary>
    Joined,
    /// <summary>Creation room: has marked themselves ready.</summary>
    Ready,
    /// <summary>Creation room: a co-signer who has not arrived yet.</summary>
    NotJoined,
}

/// <summary>The one shared vocabulary of states, used by rooms, lists, wallet cards and the offline screens.</summary>
public enum MultisigState
{
    NeedsYourSignature,
    YouSigned,
    WaitingForOthers,
    ReadyToBroadcast,
    LiveRoom,
    Sent,
    CancelledOrExpired,
    Failed,
}

public enum PrimaryAction
{
    None,
    ReviewAndSign,
    Broadcast,
    CreateWallet,
    OpenRoom,
    Sign,
    View,
    GoToPending,
}

public enum UxTone
{
    Neutral,
    Action,
    Positive,
    Warning,
}

/// <summary>Label, icon and primary action of a state; the single source every screen reads.</summary>
public sealed record StateInfo(string Label, string Icon, UxTone Tone, PrimaryAction Action, string? ActionLabel);

/// <summary>Where a signing room stands, as far as the headline cares.</summary>
public enum RoomPhase
{
    Open,
    Complete,
    Broadcast,
}

/// <summary>What the room reports about one co-signer (or one slot), before any presence smoothing.</summary>
public sealed record SignerInput(
    string PublicKeyHex,
    string DisplayName,
    bool Online,
    SignerStatus Status,
    bool IsCreator = false);

public sealed record SignerCardModel(
    string PublicKeyHex,
    string DisplayName,
    bool IsYou,
    string Initials,
    string? Fingerprint,
    SignerPresence? Presence,
    SignerStatus Status,
    bool IsCreator);

public sealed record RoomHeadline(string Title, string? Detail, UxTone Tone, PrimaryAction Action, string? ActionLabel);

public sealed record PaymentSummaryModel(
    long AmountSat,
    string RecipientAddress,
    string RecipientGrouped,
    long FeeSat,
    long TotalOutSat,
    string? Memo,
    string? ChangeNote);

public sealed record AttentionCount(string WalletId, int NeedsSignature);

// Presentation-only types for the shared-wallet list, the Pending list and the signing room
// (specs/026-multisig-wallet-experience). Like the ones above: computed on the fly, never stored or sent.

/// <summary>The one button a pending payment's card shows. Every one except <see cref="OpenRoom"/> opens the payment's own screen.</summary>
public enum PendingCardAction
{
    None,
    ReviewAndSign,
    OpenRoom,
    GetSignatures,
    ReviewAndBroadcast,
    ReviewAndRetry,
    ReviewAndCancel,
    View,
}

/// <summary>A shared wallet's pending payments, split the way the Pending list shows them. Sent payments are in none.</summary>
public sealed record PendingGroups(
    IReadOnlyList<PendingMultisigTransaction> NeedsYou,
    IReadOnlyList<PendingMultisigTransaction> WaitingForOthers,
    IReadOnlyList<PendingMultisigTransaction> Cancelled)
{
    /// <summary>Everything the list shows.</summary>
    public int Count => NeedsYou.Count + WaitingForOthers.Count + Cancelled.Count;
}

/// <summary>A card's button: what it does and what it says. No label means no button.</summary>
public sealed record PendingCardButton(PendingCardAction Action, string? Label);

/// <summary>What the one status line on a shared wallet's card is about, most urgent first.</summary>
public enum WalletCardStatusKind
{
    None,
    NeedsSignature,
    ReadyToBroadcast,
    /// <summary>A signing room this device has joined. The wallet cannot know about rooms it is not in.</summary>
    LiveRoom,
    WaitingForOthers,
}

/// <summary>The status line of a shared wallet's card: what it is about and how many.</summary>
public sealed record WalletCardStatusLine(WalletCardStatusKind Kind, int Count);

/// <summary>Everything a shared wallet's card on the list shows, apart from the balance, which is filled in afterwards.</summary>
public sealed record WalletCardModel(
    string Id,
    string Name,
    int RequiredSignatures,
    int TotalSigners,
    bool IsViewOnly,
    bool NotBackedUp,
    WalletCardStatusLine Status,
    int NeedsYou,
    DateTime LastActivity);

/// <summary>Where a signing room stands, as the three-step tracker shows it.</summary>
public enum RoomStageKind
{
    Invite,
    Sign,
    Broadcast,
    Done,
}

/// <summary>The time left in a signing room: its text, and whether it is close enough to the end to stand out.</summary>
public sealed record RoomCountdownModel(string Text, bool IsUrgent);

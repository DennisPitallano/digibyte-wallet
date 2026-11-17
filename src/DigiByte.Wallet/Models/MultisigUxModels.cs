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

namespace DigiByte.P2P.Shared.Models;

// Wire models for online multisig signing rooms (specs/017-multisig-online-signing, contracts/signing-room-hub.md).
// Everything here is public data. Connection ids never appear in anything sent to clients.

public enum SigningRoomStatus
{
    Open,
    Complete,
    Broadcast,
}

public enum SigningParticipantAction
{
    None,
    Signed,
    Declined,
}

/// <summary>A participant as every room member sees it.</summary>
public sealed class SigningParticipantView
{
    public string PublicKeyHex { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Online { get; set; }
    public SigningParticipantAction Action { get; set; }
    public bool IsCreator { get; set; }
}

/// <summary>A signature copy a co-signer handed to the room.</summary>
public sealed class SignedPsbtView
{
    public string PublicKeyHex { get; set; } = "";
    public string PsbtBase64 { get; set; } = "";
}

/// <summary>Full room state. <see cref="IncludesPayload"/> is false when the PSBT fields were left out to keep updates small.</summary>
public sealed class SigningRoomView
{
    public string RoomId { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>"main" or "test".</summary>
    public string Network { get; set; } = "main";
    public string UnsignedTxId { get; set; } = "";
    public int RequiredSignatures { get; set; }
    public List<string> CoSignerKeys { get; set; } = [];
    public bool IncludesPayload { get; set; }
    public string? PsbtBase64 { get; set; }
    public List<SignedPsbtView> SignedPsbts { get; set; } = [];
    /// <summary>Co-signer keys the server found with a valid signature on every input (each wallet re-checks for itself).</summary>
    public List<string> SignedKeys { get; set; } = [];
    public string? Memo { get; set; }
    public List<SigningParticipantView> Participants { get; set; } = [];
    public SigningRoomStatus Status { get; set; }
    /// <summary>True when declines leave too few possible signers to reach the threshold.</summary>
    public bool Unreachable { get; set; }
    public string? BroadcastClaimHolder { get; set; }
    public string? BroadcastTxId { get; set; }
}

public sealed class SigningRoomOptionsView
{
    public List<int> AllowedLifetimesMinutes { get; set; } = [];
    public int DefaultLifetimeMinutes { get; set; }
}

public sealed class OpenSigningRoomRequest
{
    /// <summary>"main" or "test".</summary>
    public string Network { get; set; } = "main";
    public List<string> CoSignerKeys { get; set; } = [];
    public int RequiredSignatures { get; set; }
    public string PsbtBase64 { get; set; } = "";
    public string? Memo { get; set; }
    public int? LifetimeMinutes { get; set; }
    public string DisplayName { get; set; } = "";
    public string PublicKeyHex { get; set; } = "";
    public string Proof { get; set; } = "";
}

public sealed class JoinSigningRoomRequest
{
    public string InviteCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PublicKeyHex { get; set; } = "";
    public string Proof { get; set; } = "";
}

public sealed class RejoinSigningRoomRequest
{
    public string RoomId { get; set; } = "";
    public string PublicKeyHex { get; set; } = "";
    public string Proof { get; set; } = "";
}

/// <summary>Error codes sent with <c>Error(code, message)</c>.</summary>
public static class SigningRoomErrors
{
    public const string InvalidCode = "invalid_code";
    public const string RateLimited = "rate_limited";
    public const string BadProofOpen = "bad_proof_open";
    public const string BadPsbt = "bad_psbt";
    public const string BadSignature = "bad_signature";
    public const string BadLifetime = "bad_lifetime";
    public const string BadRequest = "bad_request";
    public const string NotAllowed = "not_allowed";
    public const string RoomEnded = "room_ended";

    public const string InvalidCodeMessage = "Invalid or expired code.";
    public const string RateLimitedMessage = "Too many attempts, try again shortly.";
}

/// <summary>Reasons sent with <c>RoomEnded(reason)</c>.</summary>
public static class SigningRoomEndReasons
{
    public const string Expired = "expired";
    public const string Closed = "closed";
    public const string Broadcast = "broadcast";
}

namespace DigiByte.P2P.Shared.Models;

/// <summary>
/// Represents a real-time multisig wallet creation room.
/// Ephemeral — stored in-memory only, expires after <see cref="ExpiresAt"/>.
/// </summary>
public class MultisigRoom
{
    public string RoomId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string InviteCode { get; set; } = "";
    public string WalletName { get; set; } = "";
    public int RequiredSignatures { get; set; }
    public int TotalSigners { get; set; }
    public List<MultisigParticipant> Participants { get; set; } = [];
    public MultisigRoomStatus Status { get; set; } = MultisigRoomStatus.Waiting;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);

    public bool IsFull => Participants.Count >= TotalSigners;
    public bool AllReady => Participants.Count == TotalSigners && Participants.TrueForAll(p => p.IsReady);
}

/// <summary>
/// A participant in a multisig room.
/// </summary>
public class MultisigParticipant
{
    public string Name { get; set; } = "";
    public string PublicKeyHex { get; set; } = "";
    public bool IsInitiator { get; set; }
    public bool IsReady { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    /// <summary>SignalR connection ID (server-side only, not serialized to clients).</summary>
    public string ConnectionId { get; set; } = "";
}

public enum MultisigRoomStatus
{
    Waiting,
    Full,
    Creating,
    Created,
    Expired,
    Cancelled
}

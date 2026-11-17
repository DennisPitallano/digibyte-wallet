using DigiByte.P2P.Shared.Models;

namespace DigiByte.P2P.Shared.Contracts;

/// <summary>
/// SignalR hub contract — methods clients call on the server for real-time multisig room coordination.
/// </summary>
public interface IMultisigRoomHub
{
    /// <summary>
    /// Returns a fresh random 32-byte nonce (lower-case hex), valid for 60 s, usable once and bound to this connection.
    /// Asking again replaces any outstanding nonce. Needed before every <see cref="CreateRoom"/> or <see cref="JoinRoom"/>.
    /// </summary>
    Task<string> RequestChallenge();

    /// <summary>
    /// Create a new room. Returns the room state via RoomUpdated callback.
    /// <paramref name="proof"/> is Base64 of <c>Key.SignCompact(SHA256(SHA256(UTF8(message))))</c> by the key behind
    /// <paramref name="publicKeyHex"/>, with message
    /// <c>"DigiByte multisig creation room\ncreate\n{walletName}\n{nonceHex}\n{connectionId}"</c>
    /// (see <c>DigiByte.Crypto.Multisig.CoSignerProof</c>).
    /// </summary>
    Task CreateRoom(string walletName, int requiredSignatures, int totalSigners, string initiatorName, string publicKeyHex, string proof);

    /// <summary>
    /// Join an existing room by invite code. <paramref name="proof"/> is as for <see cref="CreateRoom"/> with purpose
    /// <c>join</c> and the trimmed, upper-cased invite code as target. A bad proof gets the same error as an unknown code,
    /// and failed joins are rate-limited per connection.
    /// </summary>
    Task JoinRoom(string inviteCode, string name, string publicKeyHex, string proof);

    /// <summary>Leave the current room.</summary>
    Task LeaveRoom(string roomId);

    /// <summary>Mark yourself as ready to create the wallet.</summary>
    Task SetReady(string roomId);

    /// <summary>Unmark yourself as ready.</summary>
    Task SetNotReady(string roomId);
}

/// <summary>
/// SignalR client contract — methods the server calls on connected clients.
/// </summary>
public interface IMultisigRoomClient
{
    /// <summary>Full room state update (sent on join, when participants change, on ready state changes).</summary>
    Task RoomUpdated(MultisigRoom room);

    /// <summary>A participant joined the room.</summary>
    Task ParticipantJoined(MultisigParticipant participant);

    /// <summary>A participant left the room.</summary>
    Task ParticipantLeft(string publicKeyHex);

    /// <summary>All participants are ready — clients should create the wallet locally.</summary>
    Task AllReady(MultisigRoom room);

    /// <summary>The room was closed (expired, cancelled, or initiator left).</summary>
    Task RoomClosed(string reason);

    /// <summary>An error occurred (invalid code, room full, etc.).</summary>
    Task Error(string message);
}

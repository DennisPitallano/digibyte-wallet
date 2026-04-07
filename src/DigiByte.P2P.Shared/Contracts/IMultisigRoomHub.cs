using DigiByte.P2P.Shared.Models;

namespace DigiByte.P2P.Shared.Contracts;

/// <summary>
/// SignalR hub contract — methods clients call on the server for real-time multisig room coordination.
/// </summary>
public interface IMultisigRoomHub
{
    /// <summary>Create a new room. Returns the room state via RoomUpdated callback.</summary>
    Task CreateRoom(string walletName, int requiredSignatures, int totalSigners, string initiatorName, string publicKeyHex);

    /// <summary>Join an existing room by invite code.</summary>
    Task JoinRoom(string inviteCode, string name, string publicKeyHex);

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

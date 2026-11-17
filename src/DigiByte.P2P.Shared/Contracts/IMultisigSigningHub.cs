using DigiByte.P2P.Shared.Models;

namespace DigiByte.P2P.Shared.Contracts;

/// <summary>
/// Online multisig signing room hub (<c>/hubs/multisig-signing</c>), a co-signer coordination relay under constitution
/// 1.2.0. See specs/017-multisig-online-signing/contracts/signing-room-hub.md. A connection sees or changes a room only
/// after proving, with <c>CoSignerProof</c> and <c>SigningRoomDomain</c>, that it holds one of that room's co-signer keys.
/// </summary>
public interface IMultisigSigningHub
{
    /// <summary>Lifetimes the opener may choose (never above the operator maximum) and the default.</summary>
    Task<SigningRoomOptionsView> GetOptions();

    /// <summary>A fresh single-use nonce (hex) for this connection, valid 60 s. Replaces any outstanding one.</summary>
    Task<string> RequestChallenge();

    Task OpenRoom(OpenSigningRoomRequest request);
    Task JoinRoom(JoinSigningRoomRequest request);
    Task Rejoin(RejoinSigningRoomRequest request);
    Task SubmitSignature(string roomId, string psbtBase64);
    Task Decline(string roomId);
    Task ClaimBroadcast(string roomId);
    Task ReportBroadcast(string roomId, bool success, string txIdOrReason);
    Task CloseRoom(string roomId);
    Task LeaveRoom(string roomId);
}

/// <summary>Callbacks the signing hub sends to clients.</summary>
public interface IMultisigSigningClient
{
    Task RoomState(SigningRoomView room);
    Task ExistingRoom(string inviteCode);
    Task SignatureAdded(string publicKeyHex, string psbtBase64);
    Task BroadcastGranted();
    Task BroadcastClaimed(string publicKeyHex);
    Task Broadcasted(string txId);
    Task BroadcastFailed(string reason);
    Task RoomEnded(string reason);
    Task Superseded();
    Task Error(string code, string message);
}

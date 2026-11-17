using DigiByte.Api.Multisig;
using DigiByte.P2P.Shared.Contracts;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.SignalR;

namespace DigiByte.Api.Hubs;

/// <summary>
/// Online multisig signing rooms (specs/017-multisig-online-signing), a co-signer coordination relay under constitution
/// 1.2.0. A thin adapter: every rule lives in <see cref="SigningRoomRegistry"/>. Carries public data only, keeps it in
/// memory, logs only anonymous counts (<see cref="SigningRoomTelemetry"/>), and is never needed for signing.
/// </summary>
public sealed class MultisigSigningHub : Hub<IMultisigSigningClient>, IMultisigSigningHub
{
    private readonly SigningRoomRegistry _rooms;
    private readonly SigningRoomRateLimiter _limits;
    private readonly SigningRoomTelemetry _telemetry;

    public MultisigSigningHub(SigningRoomRegistry rooms, SigningRoomRateLimiter limits, SigningRoomTelemetry telemetry)
    {
        _rooms = rooms;
        _limits = limits;
        _telemetry = telemetry;
    }

    private string Conn => Context.ConnectionId;

    public Task<SigningRoomOptionsView> GetOptions() => Task.FromResult(_rooms.Options());

    public Task<string> RequestChallenge() => Task.FromResult(_rooms.IssueChallenge(Conn));

    public async Task OpenRoom(OpenSigningRoomRequest request)
    {
        if (!_limits.TryRecordOpen(Conn))
        {
            _telemetry.RateLimited("open");
            await Clients.Caller.Error(SigningRoomErrors.RateLimited, SigningRoomErrors.RateLimitedMessage);
            return;
        }

        var previous = _rooms.RoomOf(Conn);
        var result = _rooms.Open(Conn, request);
        if (result.Error is { } error)
        {
            await FailAsync(error);
            return;
        }
        if (result.ExistingCode is { } code)
        {
            await Clients.Caller.ExistingRoom(code);
            return;
        }

        await LeaveGroupAsync(previous);
        await Groups.AddToGroupAsync(Conn, result.RoomId!);
        _telemetry.Opened(request.LifetimeMinutes ?? _rooms.Options().DefaultLifetimeMinutes);
        await Clients.Caller.RoomState(_rooms.View(result.RoomId!, includePayload: true)!);
    }

    public async Task JoinRoom(JoinSigningRoomRequest request)
    {
        if (_limits.IsJoinLimited(Conn))
        {
            _telemetry.RateLimited("join");
            await Clients.Caller.Error(SigningRoomErrors.RateLimited, SigningRoomErrors.RateLimitedMessage);
            return;
        }

        var previous = _rooms.RoomOf(Conn);
        await EnterAsync(_rooms.Join(Conn, request), previous);
    }

    public async Task Rejoin(RejoinSigningRoomRequest request)
    {
        if (_limits.IsJoinLimited(Conn))
        {
            _telemetry.RateLimited("join");
            await Clients.Caller.Error(SigningRoomErrors.RateLimited, SigningRoomErrors.RateLimitedMessage);
            return;
        }

        var previous = _rooms.RoomOf(Conn);
        await EnterAsync(_rooms.Rejoin(Conn, request), previous);
    }

    public async Task SubmitSignature(string roomId, string psbtBase64)
    {
        var result = _rooms.Submit(Conn, roomId, psbtBase64);
        if (result.Error is { } error)
        {
            await FailAsync(error);
            return;
        }
        await Clients.Group(roomId).SignatureAdded(result.PublicKeyHex!, result.PsbtBase64!);
        await PushStateAsync(roomId);
    }

    public async Task Decline(string roomId)
    {
        if (_rooms.Decline(Conn, roomId) is { } error)
        {
            await FailAsync(error);
            return;
        }
        await PushStateAsync(roomId);
    }

    public async Task ClaimBroadcast(string roomId)
    {
        var result = _rooms.ClaimBroadcast(Conn, roomId);
        if (result.Error is { } error)
        {
            await FailAsync(error);
            return;
        }
        if (!result.Granted)
        {
            await Clients.Caller.BroadcastClaimed(result.HolderKey!);
            return;
        }
        await Clients.Caller.BroadcastGranted();
        await Clients.OthersInGroup(roomId).BroadcastClaimed(result.HolderKey!);
        await PushStateAsync(roomId);
    }

    public async Task ReportBroadcast(string roomId, bool success, string txIdOrReason)
    {
        var result = _rooms.ReportBroadcast(Conn, roomId, success, txIdOrReason);
        if (result.Error is { } error)
        {
            await FailAsync(error);
            return;
        }
        _telemetry.Broadcast(success);
        if (success) await Clients.Group(roomId).Broadcasted(result.TxId!);
        else await Clients.Group(roomId).BroadcastFailed(result.FailureReason!);
        await PushStateAsync(roomId);
    }

    public async Task CloseRoom(string roomId)
    {
        var (error, ended) = _rooms.Close(Conn, roomId);
        if (error is not null)
        {
            await FailAsync(error);
            return;
        }
        _telemetry.Ended(ended!.Reason, ended.Participants, ended.Signatures);
        await Clients.Clients(ended.Connections).RoomEnded(ended.Reason);
        foreach (var c in ended.Connections) await Groups.RemoveFromGroupAsync(c, roomId);
    }

    public async Task LeaveRoom(string roomId)
    {
        if (_rooms.RoomOf(Conn) != roomId) return;
        _rooms.Leave(Conn);
        await Groups.RemoveFromGroupAsync(Conn, roomId);
        await PushStateAsync(roomId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _limits.Forget(Conn);
        var roomId = _rooms.Disconnect(Conn);
        if (roomId is not null) await PushStateAsync(roomId);
        await base.OnDisconnectedAsync(exception);
    }

    // ─── Helpers ───

    private async Task EnterAsync(SigningRoomRegistry.JoinResult result, string? previous)
    {
        if (result.Error is { } error)
        {
            await FailAsync(error);
            return;
        }
        var roomId = result.RoomId!;
        if (previous != roomId) await LeaveGroupAsync(previous);
        if (result.SupersededConnection is { } old)
        {
            await Groups.RemoveFromGroupAsync(old, roomId);
            await Clients.Client(old).Superseded();
        }
        await Groups.AddToGroupAsync(Conn, roomId);
        await Clients.Caller.RoomState(_rooms.View(roomId, includePayload: true)!);
        await Clients.OthersInGroup(roomId).RoomState(_rooms.View(roomId, includePayload: false)!);
    }

    private async Task LeaveGroupAsync(string? roomId)
    {
        if (roomId is null) return;
        await Groups.RemoveFromGroupAsync(Conn, roomId);
        await PushStateAsync(roomId);
    }

    private async Task PushStateAsync(string roomId)
    {
        if (_rooms.View(roomId, includePayload: false) is { } view)
            await Clients.Group(roomId).RoomState(view);
    }

    private async Task FailAsync(SigningRoomRegistry.Failure error)
    {
        if (error.CountsAsFailedJoin) _limits.RecordJoinFailure(Conn);
        _telemetry.Error(error.Code);
        await Clients.Caller.Error(error.Code, error.Message);
    }
}

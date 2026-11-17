using DigiByte.Api.Hubs;
using DigiByte.P2P.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace DigiByte.Api.Multisig;

/// <summary>
/// Every 15 s ends expired signing rooms (and broadcast rooms after their grace period) and releases stale broadcast
/// claims (research R8). One sweep instead of a timer per room.
/// </summary>
public sealed class SigningRoomSweeper : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly SigningRoomRegistry _rooms;
    private readonly IHubContext<MultisigSigningHub, IMultisigSigningClient> _hub;
    private readonly SigningRoomTelemetry _telemetry;

    public SigningRoomSweeper(SigningRoomRegistry rooms, IHubContext<MultisigSigningHub, IMultisigSigningClient> hub,
        SigningRoomTelemetry telemetry)
    {
        _rooms = rooms;
        _hub = hub;
        _telemetry = telemetry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepOnceAsync(); }
            catch (Exception ex) { _telemetry.Unexpected(ex); }
        }
    }

    public async Task SweepOnceAsync()
    {
        var result = _rooms.Sweep();
        foreach (var ended in result.Ended)
        {
            _telemetry.Ended(ended.Reason, ended.Participants, ended.Signatures);
            if (ended.Connections.Count > 0)
                await _hub.Clients.Clients(ended.Connections).RoomEnded(ended.Reason);
            foreach (var c in ended.Connections)
                await _hub.Groups.RemoveFromGroupAsync(c, ended.RoomId);
        }
        foreach (var roomId in result.ClaimsReleased)
        {
            if (_rooms.View(roomId, includePayload: false) is { } view)
                await _hub.Clients.Group(roomId).RoomState(view);
        }
    }
}

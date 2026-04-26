using DigiByte.Pay.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Hubs;

/// <summary>
/// Real-time channel for the hosted DigiPay checkout page.
///
/// Security model: each session is a SignalR group keyed by session id.
/// Session ids are random ses_… strings (~80 bits of entropy), so cross-tenant
/// info leakage requires guessing an active session id — computationally
/// infeasible. Subscribers must present a real id; we reject unknown ids so
/// the hub can't be used as an oracle to probe for valid sessions.
/// </summary>
public class CheckoutHub : Hub
{
    public async Task SubscribeToSession(string sessionId, DigiPayDbContext db)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        // Cheap existence check: don't bind a connection to a group for an
        // id that doesn't exist. Prevents a misbehaving client from holding
        // open arbitrary group memberships and from probing the id space.
        var exists = await db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId);
        if (!exists) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }
}

public class CheckoutStatusUpdate
{
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public long ReceivedSatoshis { get; init; }
    public int Confirmations { get; init; }
    public string? Txid { get; init; }
}

public class CheckoutNotifier
{
    private readonly IHubContext<CheckoutHub> _hub;

    public CheckoutNotifier(IHubContext<CheckoutHub> hub) => _hub = hub;

    public Task PublishAsync(CheckoutStatusUpdate update, CancellationToken ct = default) =>
        _hub.Clients.Group(update.SessionId).SendAsync("StatusChanged", update, ct);
}

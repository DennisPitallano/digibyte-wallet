using Microsoft.AspNetCore.SignalR;

namespace DigiByte.Pay.Api.Hubs;

/// <summary>
/// Real-time channel for the hosted DigiPay checkout page.
/// Each session is a SignalR group keyed by the session id; the InvoiceMonitor
/// publishes state transitions via <see cref="CheckoutNotifier"/>.
/// </summary>
public class CheckoutHub : Hub
{
    public async Task SubscribeToSession(string sessionId)
    {
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

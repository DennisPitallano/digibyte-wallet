using DigiByte.P2P.Shared.Contracts;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.SignalR;

namespace DigiByte.Api.Hubs;

public class TradeChatHub : Hub<ITradeChatClient>, ITradeChatHub
{
    public async Task SendMessage(Guid tradeId, string content)
    {
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            TradeId = tradeId,
            SenderId = Guid.Empty, // TODO: resolve from auth context
            Content = content
        };

        await Clients.Group(tradeId.ToString()).ReceiveMessage(message);
    }

    public async Task MarkPaymentSent(Guid tradeId)
    {
        await Clients.Group(tradeId.ToString())
            .TradeStatusChanged(tradeId, TradeStatus.FiatSent);
    }

    public async Task ConfirmPaymentReceived(Guid tradeId)
    {
        await Clients.Group(tradeId.ToString())
            .TradeStatusChanged(tradeId, TradeStatus.Released);
    }

    public async Task RaiseDispute(Guid tradeId, string reason)
    {
        await Clients.Group(tradeId.ToString())
            .TradeStatusChanged(tradeId, TradeStatus.Disputed);
    }

    public async Task CancelTrade(Guid tradeId)
    {
        await Clients.Group(tradeId.ToString())
            .TradeCancelled(tradeId, "Cancelled by user");
    }

    public async Task JoinTrade(Guid tradeId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, tradeId.ToString());
    }

    public async Task LeaveTrade(Guid tradeId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, tradeId.ToString());
    }
}

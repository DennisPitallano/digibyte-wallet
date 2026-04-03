using DigiByte.P2P.Shared.Models;

namespace DigiByte.P2P.Shared.Contracts;

/// <summary>
/// SignalR hub contract for real-time trade chat and status updates.
/// </summary>
public interface ITradeChatHub
{
    Task SendMessage(Guid tradeId, string content);
    Task MarkPaymentSent(Guid tradeId);
    Task ConfirmPaymentReceived(Guid tradeId);
    Task RaiseDispute(Guid tradeId, string reason);
    Task CancelTrade(Guid tradeId);
}

/// <summary>
/// SignalR client contract — methods the server calls on the client.
/// </summary>
public interface ITradeChatClient
{
    Task ReceiveMessage(ChatMessage message);
    Task TradeStatusChanged(Guid tradeId, TradeStatus newStatus);
    Task TradeCreated(Trade trade);
    Task TradeCancelled(Guid tradeId, string reason);
}

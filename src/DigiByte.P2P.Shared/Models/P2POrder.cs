namespace DigiByte.P2P.Shared.Models;

public class P2POrder
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required string Username { get; set; }
    public required OrderType Type { get; set; }
    public required string FiatCurrency { get; set; }
    public required decimal PricePerDgb { get; set; }
    public required decimal MinAmount { get; set; }
    public required decimal MaxAmount { get; set; }
    public required List<string> PaymentMethods { get; set; }
    public string? Terms { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    // Denormalized reputation
    public int TradeCount { get; set; }
    public decimal CompletionRate { get; set; }
}

public enum OrderType
{
    Buy,
    Sell
}

public enum OrderStatus
{
    Active,
    Paused,
    Completed,
    Cancelled
}

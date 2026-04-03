namespace DigiByte.P2P.Shared.Models;

public class Trade
{
    public Guid Id { get; set; }
    public required Guid OrderId { get; set; }
    public required Guid BuyerId { get; set; }
    public required Guid SellerId { get; set; }
    public required decimal DgbAmount { get; set; }
    public required decimal FiatAmount { get; set; }
    public required string FiatCurrency { get; set; }
    public required string PaymentMethod { get; set; }
    public string? EscrowTxId { get; set; }
    public string? ReleaseTxId { get; set; }
    public TradeStatus Status { get; set; } = TradeStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int AutoCancelMinutes { get; set; } = 30;
}

public enum TradeStatus
{
    Pending,        // Trade initiated, waiting for escrow
    Escrowed,       // DGB locked in multisig escrow
    FiatSent,       // Buyer marked fiat as sent
    Released,       // Seller confirmed, DGB released to buyer
    Disputed,       // One party raised a dispute
    Cancelled,      // Trade cancelled
    Resolved        // Dispute resolved by arbitrator
}

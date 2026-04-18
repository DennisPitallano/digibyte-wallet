namespace DigiByte.Pay.Api.Data;

public enum PaySessionStatus
{
    Pending = 0,
    Seen = 1,
    Paid = 2,
    Confirmed = 3,
    Expired = 4,
    Underpaid = 5,
}

public class PaySession
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required int AddressIndex { get; init; }
    public required string Address { get; init; }
    public required long AmountSatoshis { get; init; }
    public string? FiatCurrency { get; init; }
    public decimal? FiatAmount { get; init; }
    public decimal? DgbPriceAtCreation { get; init; }
    public string? Label { get; init; }
    public string? Memo { get; init; }
    public PaySessionStatus Status { get; set; } = PaySessionStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public required DateTime ExpiresAt { get; init; }
    public DateTime? SeenAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaidTxid { get; set; }
    public long ReceivedSatoshis { get; set; }
    public int Confirmations { get; set; }
}

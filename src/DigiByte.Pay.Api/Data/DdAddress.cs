namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Every DD address DigiPay has created in its node wallet, with its owner (specs/034 data-model). All stores share one
/// node wallet, so this table is how incoming DD is attributed to a store or merchant.
/// </summary>
public class DdAddress
{
    public const string KindPayment = "payment";
    public const string KindDonation = "donation";

    public required string Address { get; init; }
    public required string MerchantId { get; init; }
    /// <summary>Null once the store is deleted (the DD then belongs to the merchant-level balance).</summary>
    public string? StoreId { get; set; }
    /// <summary><see cref="KindPayment"/> or <see cref="KindDonation"/>.</summary>
    public required string Kind { get; init; }
    /// <summary>The session the address was created for. Set for payment addresses.</summary>
    public string? SessionId { get; init; }
    public required string Network { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

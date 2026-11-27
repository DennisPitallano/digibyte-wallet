namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Every DigiDollar address DigiPay has given out (specs/035 data-model). Each is derived at <see cref="Index"/> from the
/// key identified by <see cref="DdKeyId"/>. The unique (key, index) and the address key make reuse impossible.
/// </summary>
public class DdAddress
{
    public const string KindPayment = "payment";
    public const string KindOption = "option";
    public const string KindDonation = "donation";

    public required string Address { get; init; }
    public required string DdKeyId { get; init; }
    /// <summary>Receive index <c>0/Index</c>.</summary>
    public required int Index { get; init; }
    /// <summary><see cref="KindPayment"/>, <see cref="KindOption"/> or <see cref="KindDonation"/>.</summary>
    public required string Kind { get; init; }
    public required string StoreId { get; init; }
    /// <summary>The session the address was given to (payments and buyer's options).</summary>
    public string? SessionId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>When DD first arrived, set by the monitor. Used for the long-unpaid-run warning (research R11).</summary>
    public DateTime? FirstPaidAt { get; set; }
}

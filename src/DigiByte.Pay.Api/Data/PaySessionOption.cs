namespace DigiByte.Pay.Api.Data;

/// <summary>
/// One currency a buyer-choice session can be paid in (specs/033 data-model). The DGB row is created with the
/// session; the DD row on the buyer's first switch to DD, or at creation for a store whose default is DD. Rows are
/// never updated, so a quoted DD amount stays fixed until the session expires.
/// </summary>
public class PaySessionOption
{
    public required string SessionId { get; init; }

    /// <summary>"DGB" or "DD". Unique per session.</summary>
    public required string Currency { get; init; }

    public required string Address { get; init; }

    /// <summary>DGB amount in satoshis; 0 for DD.</summary>
    public long AmountSatoshis { get; init; }

    /// <summary>DD only. 100 to 10,000,000 cents.</summary>
    public long? AmountCentsDd { get; init; }

    /// <summary>DD only. Units of the session's FiatCurrency per 1 USD at quote time; 1 for USD.</summary>
    public decimal? UsdRate { get; init; }

    /// <summary>DD only. When <see cref="UsdRate"/> was taken.</summary>
    public DateTime? RateAt { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

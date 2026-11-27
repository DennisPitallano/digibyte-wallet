namespace DigiByte.Wallet.Models;

public class PaymentRequest
{
    public required string Id { get; init; }
    public required string Address { get; set; }

    /// <summary>The receive index the request's own address came from; null for untracked requests (spec 036).</summary>
    public int? AddressIndex { get; set; }

    /// <summary>
    /// True when the request has an address of its own, so its payments can be told apart. False for requests made
    /// before spec 036 and in single-key wallets, which show "Not tracked" (FR-006). Missing in old JSON, so false.
    /// </summary>
    public bool Tracked { get; set; }

    /// <summary>The amount asked, in DGB. For a request made in the person's currency, the DGB amount fixed at creation.</summary>
    public decimal? AmountDgb { get; set; }

    /// <summary>The amount the person typed in their own currency (FR-017).</summary>
    public decimal? FiatAmount { get; set; }

    /// <summary>ISO code of <see cref="FiatAmount"/>, one <see cref="FiatCurrencies"/> knows.</summary>
    public string? FiatCurrency { get; set; }

    /// <summary>Price of one DGB in <see cref="FiatCurrency"/> used for the conversion.</summary>
    public decimal? FiatPrice { get; set; }

    public string? Label { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When the price lock ends (CreatedAt + 15 min) for a currency request; null for a request in DGB.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>When the person cancelled the request (FR-025).</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>The request this one renewed at a new price (FR-018).</summary>
    public string? RenewedFromId { get; set; }

    /// <summary>The payments last found at the request's address, kept for display while offline.</summary>
    public List<RequestPayment> Payments { get; set; } = [];

    /// <summary>The last computed status. Always recomputed by RequestStatusCalculator, never edited by hand.</summary>
    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Waiting;

    /// <summary>
    /// When the person was told the request was paid (overlay, toast, notification or summary). Set once, so the
    /// paid moment happens once per request (FR-010). The lock-screen notice and the locked notification don't set it.
    /// </summary>
    public DateTime? PaidAnnouncedAt { get; set; }

    /// <summary>When the system notification was sent while the wallet was locked and hidden (FR-014b); sent once.</summary>
    public DateTime? LockNotifiedAt { get; set; }

    /// <summary>False once paid until the person opens the request; drives the Request tab count (FR-014a).</summary>
    public bool OpenedSincePaid { get; set; }

    /// <summary>When the request's address was last checked successfully.</summary>
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>
    /// Generates a BIP21-style digibyte: URI
    /// </summary>
    public string ToUri()
    {
        var uri = $"digibyte:{Address}";
        var queryParams = new List<string>();
        // Other wallets read this link: the amount always uses a point, whatever language this wallet shows.
        if (AmountDgb.HasValue) queryParams.Add("amount=" + AmountDgb.Value.ToString("F8", System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(Label)) queryParams.Add($"label={Uri.EscapeDataString(Label)}");
        if (!string.IsNullOrEmpty(Message)) queryParams.Add($"message={Uri.EscapeDataString(Message)}");
        if (queryParams.Count > 0) uri += "?" + string.Join("&", queryParams);
        return uri;
    }

    /// <summary>
    /// Parses a digibyte: URI into a PaymentRequest
    /// </summary>
    public static PaymentRequest? FromUri(string uri)
    {
        if (!uri.StartsWith("digibyte:", StringComparison.OrdinalIgnoreCase))
            return null;

        var withoutScheme = uri["digibyte:".Length..];
        var addressEnd = withoutScheme.IndexOf('?');
        var address = addressEnd >= 0 ? withoutScheme[..addressEnd] : withoutScheme;
        var queryString = addressEnd >= 0 ? withoutScheme[(addressEnd + 1)..] : "";

        var request = new PaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Address = address
        };

        if (!string.IsNullOrEmpty(queryString))
        {
            foreach (var param in queryString.Split('&'))
            {
                var parts = param.Split('=', 2);
                if (parts.Length != 2) continue;
                var key = parts[0].ToLower();
                var value = Uri.UnescapeDataString(parts[1]);
                switch (key)
                {
                    case "amount":
                        // Only digits and one point: "1,5" is not a valid link amount and must never read as 15.
                        if (decimal.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint,
                            System.Globalization.CultureInfo.InvariantCulture, out var amt))
                            request.AmountDgb = amt;
                        break;
                    case "label": request.Label = value; break;
                    case "message": request.Message = value; break;
                }
            }
        }

        return request;
    }
}

/// <summary>
/// Where a payment request stands (spec 036, FR-004). Stored by name. Waiting is 0 so that requests saved before
/// spec 036, whose status was the number 0 ("Pending"), read as Waiting; they are untracked, so they show NotTracked.
/// </summary>
public enum PaymentRequestStatus
{
    Waiting = 0,
    PaymentArriving,
    PartlyPaid,
    Paid,
    Overpaid,
    Expired,
    PartlyPaidExpired,
    Cancelled,
    NotTracked,
}

/// <summary>One payment that arrived at a request's own address (spec 036, data-model "RequestPayment").</summary>
public sealed class RequestPayment
{
    public required string TxId { get; init; }

    /// <summary>The sum of this transaction's outputs to the request's address.</summary>
    public long AmountSatoshis { get; init; }

    /// <summary>At least one confirmation.</summary>
    public bool Confirmed { get; init; }

    /// <summary>First time this device saw the transaction; kept across recomputes by transaction id.</summary>
    public DateTime FirstSeenAt { get; init; }

    /// <summary>First seen after the request expired or was cancelled (FR-020).</summary>
    public bool Late { get; init; }
}

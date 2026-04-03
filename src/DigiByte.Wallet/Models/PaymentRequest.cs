namespace DigiByte.Wallet.Models;

public class PaymentRequest
{
    public required string Id { get; init; }
    public required string Address { get; set; }
    public decimal? AmountDgb { get; set; }
    public string? Label { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public PaymentRequestStatus Status { get; set; } = PaymentRequestStatus.Pending;

    /// <summary>
    /// Generates a BIP21-style digibyte: URI
    /// </summary>
    public string ToUri()
    {
        var uri = $"digibyte:{Address}";
        var queryParams = new List<string>();
        if (AmountDgb.HasValue) queryParams.Add($"amount={AmountDgb.Value:F8}");
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
                        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
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

public enum PaymentRequestStatus
{
    Pending,
    Paid,
    Expired,
    Cancelled
}

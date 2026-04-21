using System.Text.Json.Serialization;

namespace DigiPay;

/// <summary>
/// Session lifecycle. String literals on the wire so new states can be
/// added server-side without breaking <c>switch</c> statements in caller
/// code — callers should treat unknown values as an "unknown" catch-all.
/// </summary>
public static class SessionStatus
{
    public const string Pending = "pending";
    public const string Seen = "seen";
    public const string Paid = "paid";
    public const string Confirmed = "confirmed";
    public const string Expired = "expired";
    public const string Underpaid = "underpaid";
}

/// <summary>A DigiPay checkout session. The shape the REST API returns.</summary>
public sealed record Session(
    string Id,
    string StoreId,
    string MerchantId,
    string Status,
    string Address,
    long AmountSatoshis,
    decimal Amount,
    long ReceivedSatoshis,
    int Confirmations,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string Uri,
    string CheckoutUrl)
{
    public string? FiatCurrency { get; init; }
    public decimal? FiatAmount { get; init; }
    public string? Label { get; init; }
    public string? Memo { get; init; }
    public string? PaidTxid { get; init; }
    public DateTime? SeenAt { get; init; }
    public DateTime? PaidAt { get; init; }
}

public sealed record Store(
    string Id,
    string MerchantId,
    string Name,
    string Network,
    bool HasReceive,
    string Mode,
    int DefaultSessionExpiryMinutes,
    DateTime CreatedAt)
{
    public string? ReceiveAddress { get; init; }
    public string? WebhookUrl { get; init; }
}

public sealed record WebhookDelivery(
    string Id,
    string StoreId,
    string EventName,
    string Url,
    int Attempt,
    DateTime CreatedAt,
    bool Success)
{
    public string? SessionId { get; init; }
    public int? StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? DurationMs { get; init; }
    public string? ResponseSnippet { get; init; }
    public DateTime? DeliveredAt { get; init; }
    /// <summary>
    /// When set, the server will auto-redispatch at this UTC time. Null on
    /// succeeded / permanent-failure / dead-lettered rows.
    /// </summary>
    public DateTime? NextRetryAt { get; init; }
}

/// <summary>
/// Response from <see cref="DigiPayClient.RegisterAsync"/>. The
/// <see cref="ApiKey"/> is shown once — store it server-side immediately.
/// </summary>
public sealed record RegisterMerchantResponse(
    string Id,
    string DisplayName,
    string StoreId,
    string Network,
    string Mode,
    string ApiKey)
{
    public string? WebhookSecret { get; init; }
}

public sealed record SessionList(int Total, int Take, int Skip, IReadOnlyList<Session> Sessions);

/// <summary>
/// The payload POSTed to your configured webhookUrl on every session
/// state change, returned by
/// <see cref="WebhookVerifier.Verify(System.ReadOnlySpan{byte},string?,string)"/>
/// after the HMAC-SHA256 signature has been checked.
/// </summary>
public sealed record WebhookEvent(
    [property: JsonPropertyName("event")] string Event,
    DateTime Timestamp,
    Session Session);

namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A single webhook delivery attempt. One row per HTTP POST —
/// retries and manual replays produce new rows (bumping <see cref="Attempt"/>),
/// so the table doubles as an audit trail.
///
/// Kept intentionally simple for v0: no queue, no exponential backoff.
/// The delivery is fired inline from the invoice monitor or an explicit
/// replay; failures are logged here for the merchant to act on via the
/// dashboard until we wire proper retry machinery.
/// </summary>
public class WebhookDelivery
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    /// <summary>Null for synthetic events (e.g. webhook.test) that don't have a session.</summary>
    public string? SessionId { get; init; }
    public required string EventName { get; init; }
    public required string Url { get; init; }

    /// <summary>1 for the first POST; replays increment.</summary>
    public int Attempt { get; set; } = 1;

    /// <summary>HTTP status received. Null if the request threw before a response (DNS, timeout, TLS).</summary>
    public int? StatusCode { get; set; }
    /// <summary>Truncated response body (up to 2KB) so we can show merchants what the receiver said.</summary>
    public string? ResponseSnippet { get; set; }
    /// <summary>Exception message on network-layer failures; null on HTTP responses (even 5xx).</summary>
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }

    public bool Success => StatusCode is >= 200 and < 300;
}

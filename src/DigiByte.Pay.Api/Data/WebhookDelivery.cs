namespace DigiByte.Pay.Api.Data;

/// <summary>
/// A single webhook delivery attempt. One row per HTTP POST —
/// retries and manual replays produce new rows (bumping <see cref="Attempt"/>),
/// so the table doubles as an audit trail.
///
/// Automatic retries: when a delivery fails and the failure is retryable
/// (network error, 5xx, 408, 429), <see cref="NextRetryAt"/> is populated
/// on the failed row. A background <c>WebhookRetrier</c> polls for rows
/// whose <see cref="NextRetryAt"/> has come due, clears the field, and
/// re-fires via <c>WebhookDispatcher.ReplayAsync</c> — which writes a
/// fresh row with <see cref="Attempt"/> incremented. Deliveries that
/// succeed, hit a permanent 4xx, or exhaust the retry schedule leave
/// <see cref="NextRetryAt"/> null.
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

    /// <summary>
    /// UTC time at which the <c>WebhookRetrier</c> should re-dispatch this
    /// delivery. Null on successful deliveries, permanently-failed 4xx, and
    /// rows that have already been picked up for retry (the retrier clears
    /// this before it re-dispatches so the same row isn't retried twice).
    /// Also null when the retry schedule has been exhausted — that's the
    /// dead-letter state, visible in the dashboard as a final failed attempt.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    public bool Success => StatusCode is >= 200 and < 300;
}

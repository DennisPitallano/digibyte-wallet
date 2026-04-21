namespace DigiPay;

/// <summary>
/// Raised by every SDK call on failure, and by
/// <see cref="WebhookVerifier.Verify(System.ReadOnlySpan{byte},string?,string)"/>
/// on signature mismatch. The underlying HTTP status is preserved on
/// <see cref="Status"/> so callers can branch on the number rather than
/// string-matching the message.
/// <list type="table">
///   <item><term><c>0</c></term><description>Network / DNS / TLS / timeout</description></item>
///   <item><term><c>400</c></term><description>Validation — <see cref="Body"/> contains <c>{ error: "..." }</c></description></item>
///   <item><term><c>401</c></term><description>Missing or bad bearer token / webhook signature</description></item>
///   <item><term><c>404</c></term><description>Resource not found, or not owned by this merchant</description></item>
///   <item><term><c>429</c></term><description>Rate-limited (sandbox endpoints only)</description></item>
///   <item><term><c>&gt;= 500</c></term><description>Server-side; safe to retry with backoff</description></item>
/// </list>
/// </summary>
public sealed class DigiPayError : Exception
{
    /// <summary>HTTP status; 0 for network-layer failures before any response.</summary>
    public int Status { get; }

    /// <summary>Parsed error body from the server, if available.</summary>
    public object? Body { get; }

    public DigiPayError(string message, int status = 0, object? body = null) : base(message)
    {
        Status = status;
        Body = body;
    }

    public DigiPayError(string message, Exception inner, int status = 0, object? body = null)
        : base(message, inner)
    {
        Status = status;
        Body = body;
    }
}

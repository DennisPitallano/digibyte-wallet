using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigiPay;

/// <summary>
/// HMAC-SHA256 signature verification for incoming DigiPay webhooks.
/// Framework-agnostic: hand it the raw bytes + the header + the secret;
/// get back a parsed <see cref="WebhookEvent"/> or a <see cref="DigiPayError"/>
/// with <see cref="DigiPayError.Status"/> 401 on mismatch.
/// </summary>
public static class WebhookVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Verify an incoming DigiPay webhook and return the parsed event.
    /// </summary>
    /// <param name="rawBody">
    /// Raw request bytes exactly as received. <b>Must</b> be the un-parsed
    /// bytes — any reserialisation breaks the HMAC. In ASP.NET Core, read
    /// the body with <c>new StreamReader(Request.Body).ReadToEndAsync()</c>
    /// <em>before</em> any JSON binding.
    /// </param>
    /// <param name="signature">
    /// Value of the <c>X-DigiPay-Signature</c> header. Format is
    /// <c>sha256=&lt;hex&gt;</c>; the prefix is tolerated if stripped.
    /// </param>
    /// <param name="secret">The store's webhook secret. Treat like a password.</param>
    /// <exception cref="DigiPayError">
    /// Status 401 on missing / mismatched signature; 400 on malformed JSON
    /// after a valid signature.
    /// </exception>
    public static WebhookEvent Verify(ReadOnlySpan<byte> rawBody, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature))
            throw new DigiPayError("Missing X-DigiPay-Signature header", 401);
        if (string.IsNullOrEmpty(secret))
            throw new DigiPayError("Webhook secret is required", 400);

        // Header format: "sha256=<hex>". Tolerate a stripped prefix in case
        // a proxy rewrote the header — uncommon but harmless here.
        var provided = signature.StartsWith("sha256=", StringComparison.Ordinal)
            ? signature["sha256=".Length..]
            : signature;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(rawBody.ToArray());
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        // FixedTimeEquals on the utf-8 bytes of the two hex strings. The
        // length check up front keeps the timing-safe compare honest —
        // FixedTimeEquals itself throws if lengths differ.
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(provided);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
            throw new DigiPayError("Webhook signature mismatch", 401);

        try
        {
            var evt = JsonSerializer.Deserialize<WebhookEvent>(rawBody, JsonOptions);
            return evt ?? throw new DigiPayError("Webhook body parsed to null", 400);
        }
        catch (JsonException ex)
        {
            throw new DigiPayError("Webhook body is not valid JSON", ex, 400);
        }
    }

    /// <summary>Convenience overload for <c>string</c> bodies (UTF-8).</summary>
    public static WebhookEvent Verify(string rawBody, string? signature, string secret)
        => Verify(Encoding.UTF8.GetBytes(rawBody ?? ""), signature, secret);
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Api.Data;
using DigiByte.Server.Shared;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Sends state-change notifications to a store's configured <see cref="PayStore.WebhookUrl"/>.
///
/// Every attempt is persisted to <see cref="WebhookDelivery"/> so merchants can see what
/// happened (status code, response snippet, network errors) from the dashboard. Failed
/// deliveries that are retryable (network error, 5xx, 408, 429) get a
/// <see cref="WebhookDelivery.NextRetryAt"/> stamp — the <c>WebhookRetrier</c> background
/// service picks those up and re-fires on the schedule below. Permanent 4xx responses
/// and exhausted retry budgets leave <see cref="WebhookDelivery.NextRetryAt"/> null
/// (dead-letter) so the merchant can replay manually from the dashboard.
///
/// Receiver verification:
///     expected = HMAC_SHA256(secret, rawBody).hex()
///     provided = request.headers["X-DigiPay-Signature"].removePrefix("sha256=")
///     timingSafeEqual(expected, provided)
/// </summary>
public class WebhookDispatcher
{
    /// <summary>
    /// Backoff after attempt N fails. Indexed by (attempt - 1). Exhaustion
    /// (a <c>null</c> slot or past the end) = dead-letter state.
    ///
    /// Total retry window: ~8.5h. Enough to ride out most receiver outages
    /// without DOSing their service. Manual replay from the dashboard is
    /// still the escape hatch beyond that.
    /// </summary>
    private static readonly TimeSpan?[] RetrySchedule =
    {
        TimeSpan.FromMinutes(1),    // after attempt 1 failed — retry in 1 min
        TimeSpan.FromMinutes(5),    // after attempt 2
        TimeSpan.FromMinutes(30),   // after attempt 3
        TimeSpan.FromHours(2),      // after attempt 4
        TimeSpan.FromHours(6),      // after attempt 5
        null,                       // after attempt 6 — dead-letter
    };
    public static int MaxAttempts => RetrySchedule.Length + 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly DigiPayDbContext _db;
    private readonly OutboundUrlPolicy _policy;
    private readonly IOutboundResolver _resolver;

    /// <summary>Plain-text reason stored on a delivery whose stored URL fails the outbound policy. Details go to the log only.</summary>
    public const string DestinationNotAllowed = "destination not allowed";

    /// <summary>The most response text kept per delivery (plain text, markup stripped).</summary>
    public const int MaxSnippetChars = 512;

    public WebhookDispatcher(IHttpClientFactory httpFactory, ILogger<WebhookDispatcher> logger, DigiPayDbContext db, IOptions<OutboundOptions> outbound, IOutboundResolver resolver)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _db = db;
        _policy = outbound.Value.Webhook();
        _resolver = resolver;
    }

    public Task<WebhookDelivery?> DispatchAsync(
        PayStore store,
        PaySession session,
        string eventName,
        CancellationToken ct = default)
        => DispatchInternalAsync(store, session, eventName, attempt: 1, ct);

    /// <summary>
    /// Re-fires a previously persisted delivery using the same event + session. The
    /// new row carries an incremented attempt number so the audit trail shows the
    /// whole retry chain.
    /// </summary>
    public async Task<WebhookDelivery?> ReplayAsync(
        WebhookDelivery previous,
        PayStore store,
        PaySession? session,
        CancellationToken ct = default)
    {
        // For replays, we need a session to rebuild the payload. Test/synthetic
        // events don't have one — caller short-circuits those before calling us.
        if (session is null) return null;
        return await DispatchInternalAsync(store, session, previous.EventName, attempt: previous.Attempt + 1, ct);
    }

    private async Task<WebhookDelivery?> DispatchInternalAsync(
        PayStore store,
        PaySession session,
        string eventName,
        int attempt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(store.WebhookUrl) || string.IsNullOrWhiteSpace(store.WebhookSecret))
            return null;

        var payload = new
        {
            @event = eventName,
            timestamp = DateTime.UtcNow,
            session = new
            {
                id = session.Id,
                merchantId = session.MerchantId,
                storeId = session.StoreId,
                address = session.Address,
                amountSatoshis = session.AmountSatoshis,
                amount = session.AmountSatoshis / 100_000_000m,
                fiatCurrency = session.FiatCurrency,
                fiatAmount = session.FiatAmount,
                label = session.Label,
                memo = session.Memo,
                status = session.Status.ToString().ToLowerInvariant(),
                receivedSatoshis = session.ReceivedSatoshis,
                confirmations = session.Confirmations,
                paidTxid = session.PaidTxid,
                // Null for non-refunded/voided states; set by the merchant-initiated
                // refund/void endpoints. Subscribers use these to reconcile downstream.
                refundTxid = session.RefundTxid,
                refundedAt = session.RefundedAt,
                refundNote = session.RefundNote,
                // Echoed back to subscribers so a webhook handler can confirm
                // the customer landed on the expected merchant page (or kick
                // off a fallback if they didn't).
                returnUrl = session.ReturnUrl,
                createdAt = session.CreatedAt,
                expiresAt = session.ExpiresAt,
                // DigiDollar payload fields. DGB sessions carry currency="DGB"
                // and amountCentsDd=null; DD sessions carry currency="DD" and
                // amountCentsDd set. ReceivedSatoshis is repurposed for DD as
                // received cents — same field, unit implied by currency.
                currency = session.Currency,
                amountCentsDd = session.AmountCentsDd,
            },
        };

        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var signature = Sign(body, store.WebhookSecret);

        var delivery = new WebhookDelivery
        {
            // "wdel_" prefix keeps ids distinguishable in logs/URLs.
            Id = $"wdel_{RandomId(16)}",
            StoreId = store.Id,
            SessionId = session.Id,
            EventName = eventName,
            Url = store.WebhookUrl,
            Attempt = attempt,
        };
        _db.WebhookDeliveries.Add(delivery);

        // Re-validate at dispatch: the stored URL may predate the policy, or its DNS may have changed. A hostile URL is never called;
        // the delivery records why (visible in the dashboard), the URL and secret stay as they are, and nothing is retried.
        var refusal = await OutboundUrlGuard.ValidateAsync(store.WebhookUrl, _policy, _resolver, ct);
        if (refusal is not null)
        {
            delivery.ErrorMessage = DestinationNotAllowed;
            _logger.LogWarning("Webhook {Event} for store {StoreId} not sent: stored URL refused ({Reason}) session={SessionId}",
                eventName, store.Id, refusal.Value.ToText(), session.Id);
            await _db.SaveChangesAsync(ct);
            return delivery;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, store.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-DigiPay-Event", eventName);
        request.Headers.TryAddWithoutValidation("X-DigiPay-Signature", $"sha256={signature}");
        request.Headers.TryAddWithoutValidation("X-DigiPay-Delivery", delivery.Id);

        var http = _httpFactory.CreateClient("DigiPayWebhook");
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            delivery.StatusCode = (int)response.StatusCode;
            delivery.DurationMs = (int)sw.ElapsedMilliseconds;
            delivery.DeliveredAt = DateTime.UtcNow;

            // Response body snippet so the merchant can see what the receiver returned
            // on 4xx/5xx without having to wire their own logging first.
            try
            {
                var (respBody, _) = await response.Content.ReadCappedAsync(_policy.MaxResponseBytes, ct);
                delivery.ResponseSnippet = ToPlainSnippet(respBody);
            }
            catch { /* non-fatal */ }

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook {Event} → {Url} [{Status}] session={SessionId} attempt={Attempt}",
                    eventName, store.WebhookUrl, (int)response.StatusCode, session.Id, attempt);
            }
            else
            {
                _logger.LogWarning("Webhook {Event} → {Url} failed [{Status}] session={SessionId} attempt={Attempt}",
                    eventName, store.WebhookUrl, (int)response.StatusCode, session.Id, attempt);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            delivery.DurationMs = (int)sw.ElapsedMilliseconds;
            // A connect-time block (hostname resolved to a private address) gets the same generic reason and is not retried; anything else keeps
            // its message so merchants can debug their receiver.
            var blocked = ex is OutboundBlockedException || ex.InnerException is OutboundBlockedException;
            delivery.ErrorMessage = blocked ? DestinationNotAllowed : (ex.Message.Length > 500 ? ex.Message[..500] : ex.Message);
            _logger.LogWarning(ex, "Webhook {Event} → {Url} threw (session={SessionId}, attempt={Attempt})",
                eventName, store.WebhookUrl, session.Id, attempt);
        }

        // Schedule a retry if this attempt failed and the failure mode is one
        // that's likely to clear up on its own (network error, server error,
        // rate limit). Permanent 4xx stays dead-letter — if the receiver is
        // returning 404 it's a config issue and retries won't help.
        //
        // Synthetic webhook.test deliveries (session.Id starts with ses_test_)
        // are explicitly NOT retried — they're a one-shot diagnostic ping for
        // the merchant, and the dummy session isn't persisted so the retrier
        // wouldn't be able to rebuild the payload anyway.
        var isTestPing = session.Id.StartsWith("ses_test_", StringComparison.Ordinal);
        if (!delivery.Success && !isTestPing && delivery.ErrorMessage != DestinationNotAllowed && IsRetryable(delivery.StatusCode, delivery.ErrorMessage))
        {
            var backoffIndex = attempt - 1;
            if (backoffIndex < RetrySchedule.Length && RetrySchedule[backoffIndex] is TimeSpan wait)
            {
                delivery.NextRetryAt = DateTime.UtcNow + wait;
            }
        }

        await _db.SaveChangesAsync(ct);
        return delivery;
    }

    /// <summary>
    /// True if a failed delivery is worth retrying. Network / timeout / 5xx
    /// / 408 / 429 are transient enough to re-attempt; everything else (2xx
    /// success, 3xx redirect treated as non-2xx, other 4xx) is terminal.
    /// </summary>
    private static bool IsRetryable(int? statusCode, string? errorMessage)
    {
        // Network-layer failure (DNS, TLS, timeout, connection reset).
        if (errorMessage is not null) return true;
        // No response at all — treat as transient.
        if (statusCode is null) return true;
        // Server-side errors.
        if (statusCode >= 500) return true;
        // Explicit transient status codes.
        if (statusCode == 408 || statusCode == 429) return true;
        return false;
    }

    /// <summary>
    /// First <see cref="MaxSnippetChars"/> characters of the receiver's reply as plain text: tags removed and the rest HTML-escaped, so a
    /// hostile receiver cannot get markup into the dashboard through the snippet, whatever the status code.
    /// </summary>
    internal static string ToPlainSnippet(string body)
    {
        var head = body.Length > MaxSnippetChars ? body[..MaxSnippetChars] : body;
        var noTags = System.Text.RegularExpressions.Regex.Replace(head, "<[^>]*>?", string.Empty);
        return System.Net.WebUtility.HtmlEncode(noTags);
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RandomId(int lengthChars)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[lengthChars];
        for (int i = 0; i < lengthChars; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}

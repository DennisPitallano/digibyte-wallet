using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Api.Data;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Sends state-change notifications to a store's configured <see cref="PayStore.WebhookUrl"/>.
///
/// Every attempt is persisted to <see cref="WebhookDelivery"/> so merchants can see what
/// happened (status code, response snippet, network errors) from the dashboard and replay
/// failed deliveries manually. Retry-with-backoff is still deferred — failures stay failed
/// until the merchant hits replay.
///
/// Receiver verification:
///     expected = HMAC_SHA256(secret, rawBody).hex()
///     provided = request.headers["X-DigiPay-Signature"].removePrefix("sha256=")
///     timingSafeEqual(expected, provided)
/// </summary>
public class WebhookDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly DigiPayDbContext _db;

    public WebhookDispatcher(IHttpClientFactory httpFactory, ILogger<WebhookDispatcher> logger, DigiPayDbContext db)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _db = db;
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
                createdAt = session.CreatedAt,
                expiresAt = session.ExpiresAt,
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
            var response = await http.SendAsync(request, ct);
            sw.Stop();
            delivery.StatusCode = (int)response.StatusCode;
            delivery.DurationMs = (int)sw.ElapsedMilliseconds;
            delivery.DeliveredAt = DateTime.UtcNow;

            // Response body snippet so the merchant can see what the receiver returned
            // on 4xx/5xx without having to wire their own logging first.
            try
            {
                var respBody = await response.Content.ReadAsStringAsync(ct);
                delivery.ResponseSnippet = respBody.Length > 2048 ? respBody[..2048] : respBody;
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
            delivery.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            _logger.LogWarning(ex, "Webhook {Event} → {Url} threw (session={SessionId}, attempt={Attempt})",
                eventName, store.WebhookUrl, session.Id, attempt);
        }

        await _db.SaveChangesAsync(ct);
        return delivery;
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

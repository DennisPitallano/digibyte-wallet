using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Api.Data;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Sends state-change notifications to a merchant's configured <see cref="PayMerchant.WebhookUrl"/>.
///
/// v0 scope: single fire-and-forget POST with HMAC-SHA256 signature, 10-second timeout,
/// failures logged but not retried. A retry queue + delivery history lands once real
/// merchants need at-least-once semantics.
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

    public WebhookDispatcher(IHttpClientFactory httpFactory, ILogger<WebhookDispatcher> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(
        PayStore store,
        PaySession session,
        string eventName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(store.WebhookUrl) || string.IsNullOrWhiteSpace(store.WebhookSecret))
            return;

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
        var deliveryId = Guid.NewGuid().ToString("N");

        using var request = new HttpRequestMessage(HttpMethod.Post, store.WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-DigiPay-Event", eventName);
        request.Headers.TryAddWithoutValidation("X-DigiPay-Signature", $"sha256={signature}");
        request.Headers.TryAddWithoutValidation("X-DigiPay-Delivery", deliveryId);

        var http = _httpFactory.CreateClient("DigiPayWebhook");
        try
        {
            var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook {Event} → {Url} [{Status}] session={SessionId}",
                    eventName, store.WebhookUrl, (int)response.StatusCode, session.Id);
            }
            else
            {
                _logger.LogWarning("Webhook {Event} → {Url} failed [{Status}] session={SessionId}",
                    eventName, store.WebhookUrl, (int)response.StatusCode, session.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook {Event} → {Url} threw (session={SessionId})",
                eventName, store.WebhookUrl, session.Id);
        }
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

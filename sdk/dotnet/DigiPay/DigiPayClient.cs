using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigiPay;

/// <summary>
/// Main entry point. One instance per merchant API key; safe to reuse
/// across threads (wraps a thread-safe <see cref="HttpClient"/>).
///
/// <code>
/// var dp = new DigiPayClient(apiKey: Environment.GetEnvironmentVariable("DIGIPAY_KEY")!);
/// var session = await dp.Sessions.CreateAsync(new() { Amount = 5m, Label = "Order #1234" });
/// Console.WriteLine(session.CheckoutUrl);
/// </code>
///
/// For self-serve onboarding (creating a brand-new merchant + first
/// store + initial key in a single unauthenticated call) use the
/// static <see cref="RegisterAsync"/> — it doesn't need an existing key.
/// </summary>
public sealed class DigiPayClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.pay.dgbwallet.app";
    private const string SdkVersion = "0.1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _disposeHttp;

    public SessionsResource Sessions { get; }
    public StoresResource Stores { get; }

    /// <summary>Create a client with a default internal HttpClient.</summary>
    public DigiPayClient(string apiKey, string baseUrl = DefaultBaseUrl, TimeSpan? timeout = null)
        : this(CreateDefaultHttp(apiKey, baseUrl, timeout), disposeHttp: true)
    {
    }

    /// <summary>
    /// Advanced ctor: wrap a caller-owned <see cref="HttpClient"/> (e.g.
    /// one supplied by <c>IHttpClientFactory</c>). The caller is
    /// responsible for configuring BaseAddress, Authorization, User-Agent
    /// — this ctor won't mutate them.
    /// </summary>
    public DigiPayClient(HttpClient http) : this(http, disposeHttp: false)
    {
    }

    private DigiPayClient(HttpClient http, bool disposeHttp)
    {
        _http = http;
        _disposeHttp = disposeHttp;
        Sessions = new SessionsResource(this);
        Stores = new StoresResource(this);
    }

    private static HttpClient CreateDefaultHttp(string apiKey, string baseUrl, TimeSpan? timeout)
    {
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is required", nameof(apiKey));
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"digipay-dotnet/{SdkVersion}");
        return client;
    }

    public void Dispose()
    {
        if (_disposeHttp) _http.Dispose();
    }

    // ---- Shared request helpers -------------------------------------------

    internal async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
        => await SendJsonAsync<T>(HttpMethod.Get, path, body: null, idempotencyKey: null, ct).ConfigureAwait(false);

    internal async Task<T> PostJsonAsync<T>(string path, object? body, CancellationToken ct)
        => await SendJsonAsync<T>(HttpMethod.Post, path, body, idempotencyKey: null, ct).ConfigureAwait(false);

    internal async Task<T> PostJsonAsync<T>(string path, object? body, string? idempotencyKey, CancellationToken ct)
        => await SendJsonAsync<T>(HttpMethod.Post, path, body, idempotencyKey, ct).ConfigureAwait(false);

    internal async Task<T> PatchJsonAsync<T>(string path, object? body, CancellationToken ct)
        => await SendJsonAsync<T>(new HttpMethod("PATCH"), path, body, idempotencyKey: null, ct).ConfigureAwait(false);

    internal async Task<T> DeleteJsonAsync<T>(string path, CancellationToken ct)
        => await SendJsonAsync<T>(HttpMethod.Delete, path, body: null, idempotencyKey: null, ct).ConfigureAwait(false);

    internal async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        using var res = await SendAndCheckAsync(req, ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOptions);
        if (!string.IsNullOrEmpty(idempotencyKey))
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var res = await SendAndCheckAsync(req, ct).ConfigureAwait(false);
        var payload = await res.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return payload ?? throw new DigiPayError($"Empty response body from {path}", (int)res.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAndCheckAsync(HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout (not caller-cancelled). HttpClient surfaces these as
            // TaskCanceledException with no cancellation on the token.
            throw new DigiPayError($"Request to {req.RequestUri} timed out", ex, 0);
        }
        catch (HttpRequestException ex)
        {
            throw new DigiPayError($"Network error contacting {req.RequestUri}: {ex.Message}", ex, 0);
        }

        if (res.IsSuccessStatusCode) return res;

        // Surface the server's {"error": "..."} if we can — but don't let a
        // parse failure hide the status code the caller needs.
        object? parsedBody = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            parsedBody = doc.RootElement.Clone();
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                message = err.GetString();
        }
        catch { /* fall through to status-text message */ }

        res.Dispose();
        throw new DigiPayError(
            message ?? $"{req.Method} {req.RequestUri?.PathAndQuery} failed with HTTP {(int)res.StatusCode}",
            (int)res.StatusCode,
            parsedBody);
    }

    // ---- Static onboarding helper -----------------------------------------

    /// <summary>
    /// Self-serve merchant registration. Unauthenticated — returns the
    /// initial API key on the response. Shown <b>once</b>; store it in your
    /// secrets manager before dismissing the response.
    /// </summary>
    public static async Task<RegisterMerchantResponse> RegisterAsync(
        RegisterMerchantRequest request,
        string baseUrl = DefaultBaseUrl,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"digipay-dotnet/{SdkVersion}");

        using var res = await http.PostAsJsonAsync("v1/pay/merchants", request, JsonOptions, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            object? body = null;
            string? msg = null;
            try
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                body = doc.RootElement.Clone();
                if (doc.RootElement.TryGetProperty("error", out var err)) msg = err.GetString();
            }
            catch { }
            throw new DigiPayError(msg ?? $"Registration failed: HTTP {(int)res.StatusCode}", (int)res.StatusCode, body);
        }
        return (await res.Content.ReadFromJsonAsync<RegisterMerchantResponse>(JsonOptions, ct).ConfigureAwait(false))!;
    }
}

// ---- Request shapes ---------------------------------------------------

public sealed record RegisterMerchantRequest(string DisplayName, string AddressOrXpub)
{
    public string? Network { get; init; }
    public string? WebhookUrl { get; init; }
}

public sealed record CreateSessionRequest
{
    /// <summary>DGB (decimal). Must be &gt; 0.</summary>
    public required decimal Amount { get; init; }
    public string? StoreId { get; init; }
    public string? Label { get; init; }
    public string? Memo { get; init; }
    public string? FiatCurrency { get; init; }
    public decimal? FiatAmount { get; init; }
    public int? ExpiryMinutes { get; init; }
}

public sealed record ListSessionsOptions
{
    public string? StoreId { get; init; }
    public string? Status { get; init; }
    public int Take { get; init; } = 25;
    public int Skip { get; init; }
}

public sealed record CreateStoreRequest(string Name, string Network = "mainnet");

public sealed record UpdateStoreRequest
{
    public string? Name { get; init; }
    public string? Network { get; init; }
    public string? AddressOrXpub { get; init; }
    public string? WebhookUrl { get; init; }
    public int? DefaultSessionExpiryMinutes { get; init; }
}

public sealed record TestWebhookResult(bool Ok, string WebhookUrl, string DeliveryId)
{
    public int? StatusCode { get; init; }
    public string? Error { get; init; }
}

// ---- Resources ---------------------------------------------------------

public sealed class SessionsResource
{
    private readonly DigiPayClient _client;
    internal SessionsResource(DigiPayClient client) => _client = client;

    public Task<Session> CreateAsync(CreateSessionRequest request, CancellationToken ct = default)
        => _client.PostJsonAsync<Session>("v1/pay/sessions", request, idempotencyKey: null, ct);

    /// <summary>
    /// Create a session with a Stripe-style idempotency key. The server stores
    /// (merchant + key) → sessionId for 24h and replays the original session
    /// on retry, so a network blip can't mint two invoices for the same order.
    /// Pass an order id, request id, or any opaque ≤255-char string.
    /// </summary>
    public Task<Session> CreateAsync(CreateSessionRequest request, string idempotencyKey, CancellationToken ct = default)
        => _client.PostJsonAsync<Session>("v1/pay/sessions", request, idempotencyKey, ct);

    public async Task<Session> GetAsync(string sessionId, CancellationToken ct = default)
    {
        // Public endpoint wraps the session in { session, merchantName }.
        var wrapper = await _client.GetJsonAsync<PublicSessionResponse>($"v1/pay/sessions/{Uri.EscapeDataString(sessionId)}", ct).ConfigureAwait(false);
        return wrapper.Session;
    }

    public Task<SessionList> ListAsync(ListSessionsOptions? options = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(new Dictionary<string, object?>
        {
            ["storeId"] = options?.StoreId,
            ["status"] = options?.Status,
            ["take"] = options?.Take ?? 25,
            ["skip"] = options?.Skip ?? 0,
        });
        return _client.GetJsonAsync<SessionList>($"v1/pay/sessions{qs}", ct);
    }

    /// <summary>
    /// CSV bookkeeping export — returns the raw CSV as a string. Row cap
    /// is 10 000 server-side; pass a tighter filter if you're near it.
    /// </summary>
    public Task<string> ExportCsvAsync(ListSessionsOptions? options = null, CancellationToken ct = default)
    {
        var qs = BuildQuery(new Dictionary<string, object?>
        {
            ["format"] = "csv",
            ["storeId"] = options?.StoreId,
            ["status"] = options?.Status,
            ["take"] = options?.Take ?? 10_000,
        });
        return _client.GetStringAsync($"v1/pay/sessions{qs}", ct);
    }

    // The /sessions/{id} endpoint wraps its payload. Internal record, not
    // part of the public API — CreateAsync returns the Session directly.
    private sealed record PublicSessionResponse(Session Session)
    {
        public string? MerchantName { get; init; }
    }

    internal static string BuildQuery(IDictionary<string, object?> parts)
    {
        var pairs = parts
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!.ToString()!)}");
        var joined = string.Join("&", pairs);
        return joined.Length == 0 ? string.Empty : "?" + joined;
    }
}

public sealed class StoresResource
{
    private readonly DigiPayClient _client;
    internal StoresResource(DigiPayClient client) => _client = client;

    public Task<IReadOnlyList<Store>> ListAsync(CancellationToken ct = default)
        => _client.GetJsonAsync<IReadOnlyList<Store>>("v1/pay/stores", ct);

    public Task<Store> GetAsync(string storeId, CancellationToken ct = default)
        => _client.GetJsonAsync<Store>($"v1/pay/stores/{Uri.EscapeDataString(storeId)}", ct);

    public Task<Store> CreateAsync(CreateStoreRequest request, CancellationToken ct = default)
        => _client.PostJsonAsync<Store>("v1/pay/stores", request, ct);

    public Task<Store> UpdateAsync(string storeId, UpdateStoreRequest request, CancellationToken ct = default)
        => _client.PatchJsonAsync<Store>($"v1/pay/stores/{Uri.EscapeDataString(storeId)}", request, ct);

    public Task<object> DeleteAsync(string storeId, CancellationToken ct = default)
        => _client.DeleteJsonAsync<object>($"v1/pay/stores/{Uri.EscapeDataString(storeId)}", ct);

    /// <summary>Fire a synthetic webhook so the receiver can confirm signature handling.</summary>
    public Task<TestWebhookResult> SendTestWebhookAsync(string storeId, CancellationToken ct = default)
        => _client.PostJsonAsync<TestWebhookResult>(
            $"v1/pay/stores/{Uri.EscapeDataString(storeId)}/webhook/test", body: null, ct);

    public Task<IReadOnlyList<WebhookDelivery>> ListDeliveriesAsync(
        string storeId, int take = 25, string? sessionId = null, CancellationToken ct = default)
    {
        var qs = SessionsResource.BuildQuery(new Dictionary<string, object?>
        {
            ["take"] = take,
            ["sessionId"] = sessionId,
        });
        return _client.GetJsonAsync<IReadOnlyList<WebhookDelivery>>(
            $"v1/pay/stores/{Uri.EscapeDataString(storeId)}/webhook-deliveries{qs}", ct);
    }

    /// <summary>Re-fire a previous delivery. Server writes a new row with Attempt + 1.</summary>
    public Task<WebhookDelivery> ReplayDeliveryAsync(string storeId, string deliveryId, CancellationToken ct = default)
        => _client.PostJsonAsync<WebhookDelivery>(
            $"v1/pay/stores/{Uri.EscapeDataString(storeId)}/webhook-deliveries/{Uri.EscapeDataString(deliveryId)}/replay",
            body: null, ct);

    public Task<string> ExportDeliveriesCsvAsync(
        string storeId, int take = 10_000, string? sessionId = null, CancellationToken ct = default)
    {
        var qs = SessionsResource.BuildQuery(new Dictionary<string, object?>
        {
            ["format"] = "csv",
            ["take"] = take,
            ["sessionId"] = sessionId,
        });
        return _client.GetStringAsync(
            $"v1/pay/stores/{Uri.EscapeDataString(storeId)}/webhook-deliveries{qs}", ct);
    }
}

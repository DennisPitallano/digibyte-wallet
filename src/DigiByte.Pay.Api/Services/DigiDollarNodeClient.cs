using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Thin HTTP client that talks to <c>DigiByte.NodeApi</c>'s <c>/api/digidollar/*</c>
/// surface. Owns nothing DD-specific in Pay.Api's own domain — every call is
/// pass-through except for parsing the JSON shapes returned by the RPC wrapper.
///
/// Write endpoints on NodeApi require <c>X-NodeApi-Key</c> when configured.
/// Configure via <see cref="DdNodeOptions.WriteKey"/> (env var
/// <c>NODEAPI_WRITE_KEY</c>).
/// </summary>
public class DigiDollarNodeClient
{
    private readonly HttpClient _http;
    private readonly DdNodeOptions _opts;
    private readonly ILogger<DigiDollarNodeClient> _logger;

    public DigiDollarNodeClient(HttpClient http, IOptions<DdNodeOptions> opts, ILogger<DigiDollarNodeClient> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;

        if (!string.IsNullOrEmpty(_opts.WriteKey))
        {
            _http.DefaultRequestHeaders.Remove("X-NodeApi-Key");
            _http.DefaultRequestHeaders.Add("X-NodeApi-Key", _opts.WriteKey);
        }
    }

    /// <summary>
    /// Ask the node for a fresh DD receive address. One RPC per call — do not
    /// cache; each session must get its own address to preserve buyer privacy
    /// and per-session accounting.
    /// </summary>
    public async Task<string> GenerateAddressAsync(CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/api/digidollar/address/new";
        // POST body is optional (label). Send empty object so servers that
        // require a JSON body don't 415.
        var resp = await _http.PostAsJsonAsync(url, new { }, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.String)
        {
            var value = addr.GetString();
            if (!string.IsNullOrEmpty(value)) return value;
        }
        throw new InvalidOperationException("NodeApi returned no address in /address/new response.");
    }

    /// <summary>
    /// Fetch DD balance for a specific address, in USD cents.
    /// Includes mempool (<c>minconf=0</c>) so mempool payments can transition
    /// pending → seen.
    /// </summary>
    public async Task<DdBalance> GetBalanceAsync(string address, CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/api/digidollar/balance/{Uri.EscapeDataString(address)}?minConfirmations=0";
        var json = await _http.GetFromJsonAsync<JsonElement>(url, ct);
        // Underlying digibyted returns { "confirmed": n, "unconfirmed": n, "total": n }
        long confirmed = 0, unconfirmed = 0;
        if (json.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.Number)
            confirmed = c.GetInt64();
        if (json.TryGetProperty("unconfirmed", out var u) && u.ValueKind == JsonValueKind.Number)
            unconfirmed = u.GetInt64();
        return new DdBalance(confirmed, unconfirmed);
    }

    /// <summary>
    /// Look up the first receive txid landing on <paramref name="address"/>.
    /// Used by the invoice monitor to stamp <c>PaidTxid</c> once a DD payment
    /// is detected. Returns null when no receive is found yet.
    /// </summary>
    public async Task<string?> GetFirstReceiveTxidAsync(string address, CancellationToken ct = default)
    {
        var url = $"{_opts.BaseUrl.TrimEnd('/')}/api/digidollar/transactions"
            + $"?address={Uri.EscapeDataString(address)}&category=receive&count=1";
        var json = await _http.GetFromJsonAsync<JsonElement>(url, ct);

        // digibyted returns an array of tx objects, each with a "txid" property.
        // NodeApi passes them through; be defensive about shape.
        if (json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0)
        {
            var first = json[0];
            if (first.TryGetProperty("txid", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        else if (json.ValueKind == JsonValueKind.Object)
        {
            // Some node builds wrap in { "transactions": [...] }
            if (json.TryGetProperty("transactions", out var arr) && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                if (first.TryGetProperty("txid", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString();
            }
        }
        return null;
    }

    /// <summary>
    /// Cheap deployment probe — used by health checks and to short-circuit
    /// DD session creation when the node isn't ready.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_opts.BaseUrl.TrimEnd('/')}/api/digidollar/health";
            var json = await _http.GetFromJsonAsync<JsonElement>(url, ct);
            return json.TryGetProperty("enabled", out var e) && e.GetBoolean()
                && json.TryGetProperty("deployment_active", out var d) && d.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DigiDollarNodeClient health probe failed");
            return false;
        }
    }
}

public record DdBalance(long ConfirmedCents, long UnconfirmedCents)
{
    public long TotalCents => ConfirmedCents + UnconfirmedCents;
}

public class DdNodeOptions
{
    /// <summary>
    /// Base URL of the <c>DigiByte.NodeApi</c> deployment (e.g.
    /// <c>http://localhost:5260</c> or <c>https://node.dgbwallet.app</c>).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5260";

    /// <summary>
    /// Shared secret sent as <c>X-NodeApi-Key</c> on write endpoints. Optional
    /// when NodeApi is running without a write key (dev / behind a firewall).
    /// </summary>
    public string? WriteKey { get; set; }
}

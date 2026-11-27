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
    // ==================== Payouts (specs/034 contract C4) ====================

    private string Url(string path) => $"{_opts.BaseUrl.TrimEnd('/')}/api/digidollar/{path}";

    /// <summary>
    /// Node health for the payout worker. Never throws: an unreachable node is <c>Reachable = false</c>. Reserve fields
    /// missing from older NodeApi builds count as healthy.
    /// </summary>
    public async Task<DdNodeHealth> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetFromJsonAsync<JsonElement>(Url("health"), ct);
            bool Flag(string name, bool fallback) =>
                json.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
            var enabled = Flag("enabled", false) && Flag("deployment_active", false);
            decimal? reserve = json.TryGetProperty("dgb_fee_reserve_dgb", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDecimal() : null;
            return new DdNodeHealth(true, enabled, reserve, Flag("reserve_healthy", true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "DigiDollar node health check failed");
            return new DdNodeHealth(false, false, null, false);
        }
    }

    /// <summary>The node wallet's whole DD balance in cents, unconfirmed included (research R4 coverage rule).</summary>
    public async Task<long> GetWalletBalanceAsync(int minConfirmations = 0, CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>(Url($"balance?minConfirmations={minConfirmations}"), ct);
        if (json.ValueKind == JsonValueKind.Number) return json.GetInt64();
        long Get(string name) => json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        return json.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : Get("confirmed") + Get("unconfirmed");
    }

    /// <summary>One page of the wallet's DD receives, newest last (as <c>listdigidollartxs</c> returns them).</summary>
    public async Task<IReadOnlyList<DdReceive>> ListReceivesAsync(int count, int skip, CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>(Url($"transactions?category=receive&count={count}&skip={skip}"), ct);
        var list = new List<DdReceive>();
        foreach (var tx in Items(json))
        {
            var txid = Str(tx, "txid");
            var address = Str(tx, "address");
            if (txid is null || address is null) continue;
            var cents = Math.Abs(Long(tx, "amount_cents") ?? Long(tx, "amountCents") ?? Long(tx, "amount") ?? 0);
            if (cents <= 0) continue;
            list.Add(new DdReceive(txid, address, cents, (int)(Long(tx, "confirmations") ?? 0), Long(tx, "time") ?? Long(tx, "blocktime") ?? 0));
        }
        return list;
    }

    /// <summary>
    /// Sends DD from the node wallet. Never throws for node or network trouble: the result says whether the send
    /// happened (<see cref="DdSendOutcome.Sent"/>), clearly did not (<see cref="DdSendOutcome.Rejected"/>), or is
    /// unknown (<see cref="DdSendOutcome.Ambiguous"/>; research R3).
    /// </summary>
    public async Task<DdSendResult> SendAsync(string address, long amountCents, string comment, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(Url("send"), new { address, amountCents, comment }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DdSendResult.Ambiguous(ex.GetType().Name);
        }
        using (response)
        {
            string body = "";
            try { body = await response.Content.ReadAsStringAsync(ct); } catch { /* keep empty */ }
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("txid", out var t) && t.GetString() is { Length: 64 } txid)
                        return DdSendResult.Sent(txid);
                }
                catch { /* fall through */ }
                return DdSendResult.Ambiguous("unparsed_success");
            }
            if ((int)response.StatusCode == 400)
            {
                var message = ErrorText(body);
                return DdSendResult.Rejected(message, IsInsufficientFunds(message));
            }
            return DdSendResult.Ambiguous($"http_{(int)response.StatusCode}");
        }
    }

    /// <summary>
    /// True when a send was refused only because the wallet's DD can't be spent yet (unconfirmed change or not enough
    /// confirmed DD). Such payouts wait instead of failing (specs/034 tasks T033). The node's exact wording is
    /// recorded by T001; these markers cover digibyted's known messages.
    /// </summary>
    public static bool IsInsufficientFunds(string message) =>
        message.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
        || message.Contains("dd-input-amounts-unknown", StringComparison.OrdinalIgnoreCase)
        || message.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Recent DD sends from the wallet with their comment (contract C4 <c>GET /sends</c>).</summary>
    public async Task<IReadOnlyList<DdSend>> ListSendsAsync(long sinceUnix, int count = 200, CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>(Url($"sends?since={sinceUnix}&count={count}"), ct);
        var list = new List<DdSend>();
        foreach (var tx in Items(json))
        {
            var txid = Str(tx, "txid");
            if (txid is null) continue;
            list.Add(new DdSend(txid, Str(tx, "address"), Math.Abs(Long(tx, "amountCents") ?? 0), Str(tx, "comment"),
                Long(tx, "time") ?? 0, (int)(Long(tx, "confirmations") ?? 0)));
        }
        return list;
    }

    /// <summary>A transaction's status from the public DD index: <c>mempool</c>, <c>confirmed</c> or <c>unknown</c>.</summary>
    public async Task<DdTxStatus> GetTxStatusAsync(string txid, CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>(Url($"tx/{Uri.EscapeDataString(txid)}/status"), ct);
        var status = json.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "unknown";
        var confirmations = json.TryGetProperty("confirmations", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        return new DdTxStatus(status, confirmations);
    }

    private static IEnumerable<JsonElement> Items(JsonElement json)
    {
        var array = json.ValueKind == JsonValueKind.Array ? json
            : json.ValueKind == JsonValueKind.Object && (json.TryGetProperty("items", out var i) || json.TryGetProperty("transactions", out i)) && i.ValueKind == JsonValueKind.Array ? i
            : default;
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList() : [];
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static string ErrorText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var name in new[] { "error", "message", "detail" })
                if (doc.RootElement.TryGetProperty(name, out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();
        }
        catch { /* not JSON */ }
        return body.Length > 300 ? body[..300] : body;
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

public record DdNodeHealth(bool Reachable, bool DdEnabled, decimal? FeeReserveDgb, bool ReserveHealthy)
{
    public bool Usable => Reachable && DdEnabled;
}

public record DdReceive(string Txid, string Address, long AmountCents, int Confirmations, long Time);

public record DdSend(string Txid, string? Address, long AmountCents, string? Comment, long Time, int Confirmations);

public record DdTxStatus(string Status, int Confirmations);

public enum DdSendOutcome { Sent, Rejected, Ambiguous }

public record DdSendResult(DdSendOutcome Outcome, string? Txid, string? Message, bool InsufficientFunds)
{
    public static DdSendResult Sent(string txid) => new(DdSendOutcome.Sent, txid, null, false);
    public static DdSendResult Rejected(string message, bool insufficientFunds) => new(DdSendOutcome.Rejected, null, message, insufficientFunds);
    public static DdSendResult Ambiguous(string reason) => new(DdSendOutcome.Ambiguous, null, reason, false);
}

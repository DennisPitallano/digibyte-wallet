using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary><see cref="IDigiDollarNodeApi"/> over HTTP. Isolated from the DGB services so a DD failure never affects DGB (Principle V).</summary>
public sealed class DigiDollarNodeApiClient : IDigiDollarNodeApi
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public DigiDollarNodeApiClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    private string Url(string path) => $"{_baseUrl}{path}";

    // ------------------------------------------------------------------ health

    public async Task<NetworkHealth> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(Url("/api/digidollar/health"), ct);
            if (!resp.IsSuccessStatusCode) return Unavailable;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unavailable;
            if (!(root.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)) return Unavailable;

            long? price = null;
            int? oracleHeight = null;
            if (root.TryGetProperty("oracle", out var oracle) && oracle.ValueKind == JsonValueKind.Object)
            {
                price = ReadLong(oracle, "priceMicroUsd");
                oracleHeight = (int?)ReadLong(oracle, "height");
            }

            var err = false;
            var freeze = false;
            if (root.TryGetProperty("protection", out var protection) && protection.ValueKind == JsonValueKind.Object)
            {
                err = ReadBool(protection, "errActive");
                freeze = ReadBool(protection, "volatilityFreeze");
            }

            return new NetworkHealth
            {
                Available = true,
                DeploymentActive = !root.TryGetProperty("deploymentActive", out var dep) || dep.ValueKind != JsonValueKind.False,
                NodeHeight = (int)(ReadLong(root, "nodeHeight") ?? 0),
                NetworkHeight = (int?)ReadLong(root, "networkHeight"),
                OraclePriceMicroUsd = price,
                OracleHeight = oracleHeight,
                ErrActive = err,
                VolatilityFreeze = freeze,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Unavailable;
        }
    }

    private static NetworkHealth Unavailable => new() { Available = false };

    // ------------------------------------------------------------------ utxos

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>> GetUtxosAsync(
        IReadOnlyCollection<string> addresses, int minConf, CancellationToken ct)
    {
        if (addresses.Count == 0) return new Dictionary<string, IReadOnlyList<DigiDollarUtxo>>();

        using var doc = await PostAsync("/api/digidollar/addresses/utxos", new { addresses, minConf }, ct);
        var result = new Dictionary<string, IReadOnlyList<DigiDollarUtxo>>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var list = new List<DigiDollarUtxo>();
            foreach (var u in prop.Value.EnumerateArray())
            {
                list.Add(new DigiDollarUtxo(
                    u.GetProperty("txid").GetString()!,
                    u.GetProperty("vout").GetInt32(),
                    prop.Name,
                    u.GetProperty("amountCents").GetInt64(),
                    u.GetProperty("scriptPubKey").GetString()!,
                    u.GetProperty("valueSats").GetInt64(),
                    u.GetProperty("confirmations").GetInt32(),
                    ReadBool(u, "spentByMempool")));
            }
            result[prop.Name] = list;
        }
        return result;
    }

    // ------------------------------------------------------------------ history

    public async Task<DdHistoryResult> GetHistoryAsync(
        IReadOnlyCollection<string> addresses, int count, string? before, CancellationToken ct)
    {
        using var doc = await PostAsync("/api/digidollar/addresses/history", new { addresses, count, before }, ct);
        var root = doc.RootElement;

        var items = new List<DdHistoryEntry>();
        foreach (var i in root.GetProperty("items").EnumerateArray())
        {
            items.Add(new DdHistoryEntry(
                i.GetProperty("txid").GetString()!,
                (int?)ReadLong(i, "blockHeight"),
                i.TryGetProperty("blockTime", out var bt) && bt.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(bt.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : null,
                (int)(ReadLong(i, "confirmations") ?? 0),
                ReadLong(i, "feeSats"),
                ReadAmounts(i, "ddInputs"),
                ReadAmounts(i, "ddOutputs")));
        }
        var next = root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new DdHistoryResult(items, next);
    }

    private static List<DdAmountAt> ReadAmounts(JsonElement item, string name) =>
        item.GetProperty(name).EnumerateArray()
            .Select(a => new DdAmountAt(a.GetProperty("address").GetString()!, a.GetProperty("amountCents").GetInt64()))
            .ToList();

    // ------------------------------------------------------------------ tx status

    public async Task<DdTxStatusResult> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        using var doc = await GetAsync($"/api/digidollar/tx/{Uri.EscapeDataString(txid)}/status", ct);
        var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        var state = status switch
        {
            "mempool" => DdTxState.Mempool,
            "confirmed" => DdTxState.Confirmed,
            _ => DdTxState.Unknown,
        };
        return new DdTxStatusResult(state, (int)(ReadLong(doc.RootElement, "confirmations") ?? 0));
    }

    // ------------------------------------------------------------------ broadcast

    public async Task<string> BroadcastAsync(string rawTxHex, CancellationToken ct)
    {
        using var doc = await PostAsync("/api/tx/broadcast", new { hex = rawTxHex }, ct);
        return doc.RootElement.GetProperty("txid").GetString()
               ?? throw new DdBackendUnavailableException("Broadcast returned no txid.");
    }

    // ------------------------------------------------------------------ plumbing

    private Task<JsonDocument> GetAsync(string path, CancellationToken ct) =>
        SendAsync(() => _http.GetAsync(Url(path), ct), ct);

    private Task<JsonDocument> PostAsync<T>(string path, T body, CancellationToken ct) =>
        SendAsync(() => _http.PostAsJsonAsync(Url(path), body, ct), ct);

    /// <summary>
    /// Run a request and return the parsed 200 body. 5xx, network failures, timeouts and unusable bodies become
    /// <see cref="DdBackendUnavailableException"/>; 4xx become <see cref="DdApiException"/>. Caller cancellation passes through.
    /// </summary>
    private static async Task<JsonDocument> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await send();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DdBackendUnavailableException("DigiDollar backend is unreachable.", ex);
        }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);

            if ((int)resp.StatusCode >= 500)
                throw new DdBackendUnavailableException($"DigiDollar backend returned {(int)resp.StatusCode}.");

            if (!resp.IsSuccessStatusCode)
            {
                string code = $"http_{(int)resp.StatusCode}";
                string? detail = null;
                try
                {
                    using var err = JsonDocument.Parse(text);
                    if (err.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (err.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                            code = e.GetString()!;
                        if (err.RootElement.TryGetProperty("detail", out var d)) detail = d.ToString();
                    }
                }
                catch (JsonException) { /* keep the generic code */ }
                throw new DdApiException(code, (int)resp.StatusCode, detail);
            }

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new DdBackendUnavailableException("DigiDollar backend returned an unreadable response.", ex);
            }
        }
    }

    private static long? ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static bool ReadBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

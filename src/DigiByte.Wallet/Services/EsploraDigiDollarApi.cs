using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>
/// <see cref="IDigiDollarNodeApi"/> over a public Esplora explorer (digiexplorer.info) instead of our own NodeApi.
/// A DigiDollar address is just a Taproot output key, so the explorer already tracks it as an ordinary P2TR address
/// (value 0); the DD amounts are read from the transaction's DD <c>OP_RETURN</c> exactly as the NodeApi indexer does.
///
/// What it cannot provide, and reports honestly: the oracle price, oracle height and the ERR / volatility flags
/// (they live in the node's RPC, not in explorer data), so the oracle panel shows "unavailable" in this mode.
/// Privacy trade-off: the wallet's DD addresses go to the explorer, one address per request.
/// </summary>
public sealed class EsploraDigiDollarApi : IDigiDollarNodeApi
{
    /// <summary>Mainnet block where DigiDollar activated (research R6); before it the network reports "not active".</summary>
    public const int MainnetActivationHeight = 23_869_440;

    private const int MaxTxsPerAddress = 100;
    private const int PageSize = 25;
    private static readonly TimeSpan TipCacheTtl = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly string _mainnetBase;
    private readonly string _testnetBase;
    private readonly Func<bool> _isTestnet;
    private readonly SemaphoreSlim _gate = new(6, 6);
    private readonly ConcurrentDictionary<string, DdTx?> _txCache = new();
    private readonly ConcurrentDictionary<string, (int Height, DateTime At)> _tips = new();

    public EsploraDigiDollarApi(HttpClient http, string mainnetBaseUrl, string testnetBaseUrl, Func<bool> isTestnet)
    {
        _http = http;
        _mainnetBase = mainnetBaseUrl.TrimEnd('/');
        _testnetBase = testnetBaseUrl.TrimEnd('/');
        _isTestnet = isTestnet;
    }

    private string CurrentBase => _isTestnet() ? _testnetBase : _mainnetBase;

    private string BaseFor(string network) => network == "mainnet" ? _mainnetBase : _testnetBase;

    // ------------------------------------------------------------------ health

    public async Task<NetworkHealth> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            var baseUrl = CurrentBase;
            var height = await GetTipAsync(baseUrl, ct);
            var active = _isTestnet() || height >= MainnetActivationHeight;
            // No oracle data without a node: the price fields stay null and the UI says so plainly (FR-019).
            return new NetworkHealth { Available = true, DeploymentActive = active, NodeHeight = height };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is DdBackendUnavailableException or DdApiException or HttpRequestException or TaskCanceledException)
        {
            return new NetworkHealth { Available = false };
        }
    }

    // ------------------------------------------------------------------ utxos

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>> GetUtxosAsync(
        IReadOnlyCollection<string> addresses, int minConf, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<DigiDollarUtxo>>(StringComparer.Ordinal);
        var work = addresses.Distinct(StringComparer.Ordinal).Select(a => UtxosForAsync(a, minConf, ct)).ToList();
        foreach (var (address, list) in await Task.WhenAll(work)) result[address] = list;
        return result;
    }

    private async Task<(string Address, IReadOnlyList<DigiDollarUtxo> Utxos)> UtxosForAsync(string address, int minConf, CancellationToken ct)
    {
        if (!TryResolve(address, out var network, out var key, out var explorerAddress)) return (address, []);
        var baseUrl = BaseFor(network);
        var tip = await GetTipAsync(baseUrl, ct);

        using var doc = await GetJsonAsync(baseUrl, $"/address/{explorerAddress}/utxo", ct);
        var list = new List<DigiDollarUtxo>();
        foreach (var u in doc!.RootElement.EnumerateArray())
        {
            if (u.GetProperty("value").GetInt64() != 0) continue; // DD outputs carry no DGB value
            var txid = u.GetProperty("txid").GetString()!;
            var vout = u.GetProperty("vout").GetInt32();

            var tx = await GetDdTxAsync(baseUrl, txid, ct);
            var dd = tx?.Outputs.FirstOrDefault(o => o.Vout == vout);
            if (dd is null || dd.AmountCents <= 0) continue; // a zero-value P2TR that is not a DD output

            var confirmations = ConfirmationsOf(u.GetProperty("status"), tip);
            if (confirmations < minConf) continue;
            list.Add(new DigiDollarUtxo(txid, vout, address, dd.AmountCents, "5120" + Convert.ToHexString(key).ToLowerInvariant(), 0, confirmations));
        }
        return (address, list);
    }

    // ------------------------------------------------------------------ history

    public async Task<DdHistoryResult> GetHistoryAsync(
        IReadOnlyCollection<string> addresses, int count, string? before, CancellationToken ct)
    {
        // The explorer has no cross-address cursor; DigiDollar addresses carry few transactions, so one bounded pass is enough.
        if (before is not null) return new DdHistoryResult([], null);

        var perAddress = await Task.WhenAll(addresses.Distinct(StringComparer.Ordinal).Select(a => TxsForAsync(a, ct)));
        var entries = new Dictionary<string, DdHistoryEntry>(StringComparer.Ordinal);
        foreach (var e in perAddress.SelectMany(x => x)) entries.TryAdd(e.Txid, e);

        var ordered = entries.Values
            .OrderBy(e => e.BlockHeight is null ? 0 : 1)                // mempool first
            .ThenByDescending(e => e.BlockHeight ?? int.MaxValue)
            .Take(Math.Max(1, count))
            .ToList();
        return new DdHistoryResult(ordered, null);
    }

    private async Task<List<DdHistoryEntry>> TxsForAsync(string address, CancellationToken ct)
    {
        var entries = new List<DdHistoryEntry>();
        if (!TryResolve(address, out var network, out _, out var explorerAddress)) return entries;
        var baseUrl = BaseFor(network);
        var tip = await GetTipAsync(baseUrl, ct);

        string? lastSeen = null;
        var seen = 0;
        while (seen < MaxTxsPerAddress)
        {
            var path = lastSeen is null ? $"/address/{explorerAddress}/txs" : $"/address/{explorerAddress}/txs/chain/{lastSeen}";
            using var doc = await GetJsonAsync(baseUrl, path, ct);
            var page = doc!.RootElement.EnumerateArray().Select(t => t.Clone()).ToList();

            foreach (var raw in page)
            {
                var txid = raw.GetProperty("txid").GetString()!;
                var dd = await GetDdTxAsync(baseUrl, txid, ct, raw);
                if (dd is null) continue;
                entries.Add(await BuildEntryAsync(baseUrl, network, raw, dd, tip, ct));
            }

            seen += page.Count;
            if (page.Count < PageSize) break;
            lastSeen = page[^1].GetProperty("txid").GetString();
        }
        return entries;
    }

    private async Task<DdHistoryEntry> BuildEntryAsync(string baseUrl, string network, JsonElement raw, DdTx dd, int tip, CancellationToken ct)
    {
        var outputs = dd.Outputs.Select(o => new DdAmountAt(DigiDollarAddressValidator.Encode(o.Key, network), o.AmountCents)).ToList();

        var inputs = new List<DdAmountAt>();
        foreach (var vin in raw.GetProperty("vin").EnumerateArray())
        {
            if (vin.TryGetProperty("is_coinbase", out var cb) && cb.ValueKind == JsonValueKind.True) continue;
            if (!vin.TryGetProperty("prevout", out var prev) || prev.ValueKind != JsonValueKind.Object) continue;
            if (prev.GetProperty("value").GetInt64() != 0) continue; // only value-0 P2TR inputs can be DD

            var parent = await GetDdTxAsync(baseUrl, vin.GetProperty("txid").GetString()!, ct);
            var spent = parent?.Outputs.FirstOrDefault(o => o.Vout == vin.GetProperty("vout").GetInt32());
            if (spent is not null) inputs.Add(new DdAmountAt(DigiDollarAddressValidator.Encode(spent.Key, network), spent.AmountCents));
        }

        var status = raw.GetProperty("status");
        var confirmed = status.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;
        int? height = confirmed && status.TryGetProperty("block_height", out var h) ? h.GetInt32() : null;
        DateTimeOffset? time = confirmed && status.TryGetProperty("block_time", out var bt) && bt.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(bt.GetInt64())
            : null;
        long? fee = raw.TryGetProperty("fee", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt64() : null;

        return new DdHistoryEntry(dd.Txid, height, time, ConfirmationsOf(status, tip), fee, inputs, outputs);
    }

    // ------------------------------------------------------------------ tx status

    public async Task<DdTxStatusResult> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        var baseUrl = CurrentBase;
        using var doc = await GetJsonAsync(baseUrl, $"/tx/{Uri.EscapeDataString(txid)}/status", ct, notFoundIsNull: true);
        if (doc is null) return new DdTxStatusResult(DdTxState.Unknown, 0);

        var status = doc.RootElement;
        if (status.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True)
            return new DdTxStatusResult(DdTxState.Confirmed, ConfirmationsOf(status, await GetTipAsync(baseUrl, ct)));
        return new DdTxStatusResult(DdTxState.Mempool, 0);
    }

    // ------------------------------------------------------------------ broadcast

    public async Task<string> BroadcastAsync(string rawTxHex, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            HttpResponseMessage resp;
            try
            {
                using var content = new StringContent(rawTxHex, Encoding.ASCII, "text/plain");
                resp = await _http.PostAsync(CurrentBase + "/tx", content, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new DdBackendUnavailableException("The explorer is unreachable.", ex);
            }

            using (resp)
            {
                var body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
                if ((int)resp.StatusCode >= 500) throw new DdBackendUnavailableException($"The explorer returned {(int)resp.StatusCode}.");
                if (!resp.IsSuccessStatusCode) throw new DdApiException("broadcast_rejected", (int)resp.StatusCode, body);
                if (body.Length != 64) throw new DdBackendUnavailableException("The explorer returned an unexpected broadcast response.");
                return body;
            }
        }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ transactions

    /// <summary>One DD output of a transaction: its position, Taproot output key and amount.</summary>
    internal sealed record DdOutput(int Vout, byte[] Key, long AmountCents);

    internal sealed record DdTx(string Txid, DigiDollarTxType Type, IReadOnlyList<DdOutput> Outputs);

    /// <summary>The parsed DD view of a transaction (cached), or null when it is not a well-formed DigiDollar transaction.</summary>
    private async Task<DdTx?> GetDdTxAsync(string baseUrl, string txid, CancellationToken ct, JsonElement? known = null)
    {
        var cacheKey = baseUrl + "|" + txid;
        if (_txCache.TryGetValue(cacheKey, out var cached)) return cached;

        DdTx? parsed;
        if (known is { } tx) parsed = ParseDdTx(tx);
        else
        {
            using var doc = await GetJsonAsync(baseUrl, $"/tx/{txid}", ct, notFoundIsNull: true);
            parsed = doc is null ? null : ParseDdTx(doc.RootElement);
        }

        if (_txCache.Count > 4000) _txCache.Clear(); // simple bound; entries are cheap to rebuild
        _txCache[cacheKey] = parsed;
        return parsed;
    }

    /// <summary>
    /// The same rules as the NodeApi indexer: a DD version marker, exactly one DD <c>OP_RETURN</c> whose type matches the
    /// version, value-0 canonical P2TR outputs as the DD outputs. A transfer has one amount per DD output; a mint has one
    /// DD output and carries its amount first.
    /// </summary>
    internal static DdTx? ParseDdTx(JsonElement tx)
    {
        if (!tx.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (!DigiDollarScripts.IsDigiDollarVersion((uint)v.GetInt64())) return null;

        List<long>? amounts = null;
        var type = DigiDollarTxType.None;
        var vouts = tx.GetProperty("vout");
        foreach (var o in vouts.EnumerateArray())
        {
            if (!TryScript(o, out var script)) continue;
            List<long> a;
            DigiDollarTxType t;
            if (DigiDollarScripts.TryParseOpReturn(script, out t, out a)) { }
            else if (DigiDollarScripts.TryParseMintOpReturn(script, out var mintCents)) { t = DigiDollarTxType.Mint; a = [mintCents]; }
            else continue;
            if (amounts is not null) return null; // more than one DD OP_RETURN
            amounts = a;
            type = t;
        }
        if (amounts is null || type != DigiDollarScripts.GetTxType((uint)v.GetInt64())) return null;

        var outputs = new List<(int Vout, byte[] Key)>();
        var index = 0;
        foreach (var o in vouts.EnumerateArray())
        {
            if (o.GetProperty("value").GetInt64() == 0 && TryScript(o, out var script) && DigiDollarScripts.TryGetOutputKey(script, out var key))
                outputs.Add((index, key));
            index++;
        }

        List<DdOutput> result;
        if (type == DigiDollarTxType.Mint)
        {
            if (outputs.Count != 1 || amounts.Count < 1) return null;
            result = [new DdOutput(outputs[0].Vout, outputs[0].Key, amounts[0])];
        }
        else
        {
            if (outputs.Count != amounts.Count) return null;
            result = outputs.Select((o, i) => new DdOutput(o.Vout, o.Key, amounts[i])).ToList();
        }

        return new DdTx(tx.GetProperty("txid").GetString()!, type, result);
    }

    private static bool TryScript(JsonElement output, out Script script)
    {
        script = Script.Empty;
        if (!output.TryGetProperty("scriptpubkey", out var s) || s.ValueKind != JsonValueKind.String) return false;
        try { script = Script.FromHex(s.GetString()!); return true; }
        catch (FormatException) { return false; }
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Maps a DD address to its network, output key and the equivalent explorer (bech32m P2TR) address.</summary>
    private static bool TryResolve(string address, out string network, out byte[] key, out string explorerAddress)
    {
        explorerAddress = "";
        if (!DigiDollarAddressValidator.TryDecode(address, null, out network, out key) || network == "regtest") return false;
        // The explorer shows a DD output as a plain witness-v1 (bech32m) address; the network definition sets no Taproot
        // prefix, so encode it directly with the human-readable part ("dgb" mainnet, "dgbt" testnet).
        explorerAddress = Bech32m.EncodeWitnessV1(network == "mainnet" ? "dgb" : "dgbt", key);
        return true;
    }

    private static int ConfirmationsOf(JsonElement status, int tip)
    {
        if (!(status.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True)) return 0;
        return status.TryGetProperty("block_height", out var h) && h.ValueKind == JsonValueKind.Number
            ? Math.Max(1, tip - h.GetInt32() + 1)
            : 1;
    }

    private async Task<int> GetTipAsync(string baseUrl, CancellationToken ct)
    {
        if (_tips.TryGetValue(baseUrl, out var t) && DateTime.UtcNow - t.At < TipCacheTtl) return t.Height;

        await _gate.WaitAsync(ct);
        try
        {
            string text;
            try { text = (await _http.GetStringAsync(baseUrl + "/blocks/tip/height", ct)).Trim(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new DdBackendUnavailableException("The explorer is unreachable.", ex);
            }
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var height))
                throw new DdBackendUnavailableException("The explorer returned an unexpected height.");
            _tips[baseUrl] = (height, DateTime.UtcNow);
            return height;
        }
        finally { _gate.Release(); }
    }

    /// <summary>GET and parse a 200 JSON body. 5xx and network failures become <see cref="DdBackendUnavailableException"/>; 4xx become <see cref="DdApiException"/> (404 is null when allowed).</summary>
    private async Task<JsonDocument?> GetJsonAsync(string baseUrl, string path, CancellationToken ct, bool notFoundIsNull = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            HttpResponseMessage resp;
            try { resp = await _http.GetAsync(baseUrl + path, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new DdBackendUnavailableException("The explorer is unreachable.", ex);
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound && notFoundIsNull) return null;
                if ((int)resp.StatusCode >= 500) throw new DdBackendUnavailableException($"The explorer returned {(int)resp.StatusCode}.");
                if (!resp.IsSuccessStatusCode)
                    throw new DdApiException($"http_{(int)resp.StatusCode}", (int)resp.StatusCode, (await resp.Content.ReadAsStringAsync(ct)).Trim());
                try { return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)); }
                catch (JsonException ex) { throw new DdBackendUnavailableException("The explorer returned an unreadable response.", ex); }
            }
        }
        finally { _gate.Release(); }
    }
}

/// <summary>Minimal BIP350 bech32m encoder for witness-v1 (Taproot) addresses, with a DigiByte human-readable part.</summary>
internal static class Bech32m
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
    private const uint Constant = 0x2bc830a3;

    public static string EncodeWitnessV1(string hrp, byte[] program)
    {
        var data = new List<byte> { 1 };
        data.AddRange(ConvertBits(program));

        var checksum = CreateChecksum(hrp, data);
        var sb = new StringBuilder(hrp).Append('1');
        foreach (var d in data.Concat(checksum)) sb.Append(Charset[d]);
        return sb.ToString();
    }

    private static List<byte> ConvertBits(byte[] input)
    {
        var result = new List<byte>();
        var acc = 0;
        var bits = 0;
        foreach (var b in input)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Add((byte)((acc >> bits) & 31));
            }
        }
        if (bits > 0) result.Add((byte)((acc << (5 - bits)) & 31));
        return result;
    }

    private static uint Polymod(IEnumerable<byte> values)
    {
        uint chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) != 0) chk ^= Generator[i];
        }
        return chk;
    }

    private static byte[] CreateChecksum(string hrp, List<byte> data)
    {
        var values = hrp.Select(c => (byte)(c >> 5)).Concat([(byte)0]).Concat(hrp.Select(c => (byte)(c & 31))).Concat(data).Concat(new byte[6]);
        var mod = Polymod(values) ^ Constant;
        return Enumerable.Range(0, 6).Select(i => (byte)((mod >> (5 * (5 - i))) & 31)).ToArray();
    }
}

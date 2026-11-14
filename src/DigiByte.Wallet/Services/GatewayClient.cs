using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.Headers;

namespace DigiByte.Wallet.Services;

/// <summary>The gateway answered with its error shape, or with something the wallet cannot use. Never carries addresses or transaction data.</summary>
public sealed class GatewayRequestException : Exception
{
    public GatewayRequestException(string code, int statusCode, string message) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>The gateway error code (<c>over_limit</c>, <c>range_unavailable</c> and so on), or <c>unreadable</c> or <c>unreachable</c>.</summary>
    public string Code { get; }

    public int StatusCode { get; }

    /// <summary>The range asked for is older than the gateway keeps: the caller falls back to its explorer path for that range.</summary>
    public bool IsRangeUnavailable => Code == "range_unavailable";
}

public sealed record GatewayHeaders(IReadOnlyList<BlockHeader80> Headers, int TipHeight, string TipHash);

public sealed record GatewayScan(int TipHeight, int ScannedFrom, int ScannedTo, bool Complete, IReadOnlyList<GatewayTransactionProof> Transactions);

public sealed record GatewayUtxo(string Txid, int Vout, long Amount, string ScriptPubKey, GatewayTransactionProof Proof);

public sealed record GatewayUtxos(int TipHeight, IReadOnlyList<GatewayUtxo> Utxos);

public sealed record GatewayBroadcast(string Txid, int RelayedTo, IReadOnlyList<string> Rejections, int SeenBackFrom, string Outcome);

public sealed record GatewayProofsAnswer(IReadOnlyList<GatewayTransactionProof> Proofs, IReadOnlyList<(string Txid, string Reason)> Unavailable);

/// <summary>
/// The wallet side of the gateway HTTP contract. Every method either returns a usable answer or throws
/// <see cref="GatewayRequestException"/>: it never turns a failure into an empty result, so an outage cannot read as "no funds". Request
/// bodies hold only addresses, heights, outpoints and signed transactions, never a key, seed word or extended public key.
/// </summary>
public sealed class GatewayClient(HttpClient http, P2pGatewayOptions options) : IGatewayHeaderFeed
{
    public const int MaxPerRequest = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string BaseUrl => options.ApiUrl ?? throw new GatewayRequestException("unreachable", 0, "No gateway address is configured.");

    public async Task<GatewayHeaders> GetHeadersAsync(int from, int count, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => http.GetAsync($"{BaseUrl}/v1/headers?from={from}&count={count}", cancellationToken));
        var bytes = await ReadBytesAsync(response);
        if (bytes.Length % BlockHeader80.Size != 0)
            throw Unreadable();
        try
        {
            var headers = new List<BlockHeader80>(bytes.Length / BlockHeader80.Size);
            for (var i = 0; i < bytes.Length; i += BlockHeader80.Size)
                headers.Add(BlockHeader80.Parse(bytes.AsSpan(i, BlockHeader80.Size)));
            if (!response.Headers.TryGetValues("X-Tip-Height", out var heights) || !int.TryParse(heights.FirstOrDefault(), out var tip)
                || !response.Headers.TryGetValues("X-Tip-Hash", out var hashes))
                throw Unreadable();
            return new GatewayHeaders(headers, tip, hashes.First());
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw Unreadable();
        }
    }

    public async Task<GatewayScan> ScanAsync(IReadOnlyCollection<string> addresses, int fromHeight, int toHeight, IReadOnlyCollection<string>? knownOutpoints = null, CancellationToken cancellationToken = default)
    {
        Require(addresses.Count, nameof(addresses));
        Require(knownOutpoints?.Count ?? 0, nameof(knownOutpoints));
        var body = new { addresses, fromHeight, toHeight, knownOutpoints = knownOutpoints ?? [] };
        var root = await PostAsync("/v1/scan", body, cancellationToken);
        return Read(() => new GatewayScan(
            root.GetProperty("tipHeight").GetInt32(),
            root.GetProperty("scannedFrom").GetInt32(),
            root.GetProperty("scannedTo").GetInt32(),
            root.GetProperty("complete").GetBoolean(),
            root.GetProperty("transactions").EnumerateArray().Select(ReadProof).ToList()));
    }

    public async Task<GatewayUtxos> GetUtxosAsync(IReadOnlyCollection<string> addresses, int fromHeight, CancellationToken cancellationToken = default)
    {
        Require(addresses.Count, nameof(addresses));
        var root = await PostAsync("/v1/utxos", new { addresses, fromHeight }, cancellationToken);
        return Read(() => new GatewayUtxos(
            root.GetProperty("tipHeight").GetInt32(),
            root.GetProperty("utxos").EnumerateArray().Select(u => new GatewayUtxo(
                u.GetProperty("txid").GetString()!, u.GetProperty("vout").GetInt32(), u.GetProperty("amount").GetInt64(),
                u.GetProperty("scriptPubKey").GetString()!,
                new GatewayTransactionProof(u.GetProperty("txid").GetString()!, u.GetProperty("height").GetInt32(), u.GetProperty("blockHash").GetString()!,
                    string.Empty, u.GetProperty("proof").GetProperty("index").GetInt32(), ReadBranch(u.GetProperty("proof"))))).ToList()));
    }

    public async Task<GatewayBroadcast> BroadcastAsync(byte[] rawTransaction, CancellationToken cancellationToken = default)
    {
        var root = await PostAsync("/v1/broadcast", new { raw = Convert.ToHexString(rawTransaction).ToLowerInvariant() }, cancellationToken);
        return Read(() => new GatewayBroadcast(
            root.GetProperty("txid").GetString()!, root.GetProperty("relayedTo").GetInt32(),
            root.GetProperty("rejections").EnumerateArray().Select(r => r.GetString()!).ToList(),
            root.GetProperty("seenBackFrom").GetInt32(), root.GetProperty("outcome").GetString()!));
    }

    public async Task<GatewayProofsAnswer> GetProofsAsync(IReadOnlyCollection<(string Txid, int Height)> items, CancellationToken cancellationToken = default)
    {
        if (items.Count is 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(items), "A proof request holds 1 to 20 items.");
        var root = await PostAsync("/v1/proofs", new { items = items.Select(i => new { txid = i.Txid, height = i.Height }) }, cancellationToken);
        return Read(() => new GatewayProofsAnswer(
            root.GetProperty("proofs").EnumerateArray().Select(ReadProof).ToList(),
            root.GetProperty("unavailable").EnumerateArray().Select(u => (u.GetProperty("txid").GetString()!, u.GetProperty("reason").GetString()!)).ToList()));
    }

    // ---- plumbing ----

    private static void Require(int count, string name)
    {
        if (count > MaxPerRequest)
            throw new ArgumentOutOfRangeException(name, $"At most {MaxPerRequest} per request; the caller splits longer lists.");
    }

    private static GatewayTransactionProof ReadProof(JsonElement e)
    {
        var proof = e.GetProperty("proof");
        return new GatewayTransactionProof(
            e.GetProperty("txid").GetString()!, e.GetProperty("height").GetInt32(), e.GetProperty("blockHash").GetString()!,
            e.GetProperty("raw").GetString()!, proof.GetProperty("index").GetInt32(), ReadBranch(proof));
    }

    private static List<string> ReadBranch(JsonElement proof) => proof.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToList();

    private static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or NullReferenceException)
        {
            throw Unreadable();
        }
    }

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => http.PostAsJsonAsync($"{BaseUrl}{path}", body, Json, cancellationToken));
        var bytes = await ReadBytesAsync(response);
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Unreadable();
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            throw new GatewayRequestException("unreachable", 0, "The gateway could not be reached.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        // The gateway error shape, when it is one; anything else is just a failure.
        var code = "unreadable";
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } found)
                code = found;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or HttpRequestException)
        {
            // keep "unreadable"
        }
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new GatewayRequestException(code, status, $"The gateway answered {status} ({code}).");
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw new GatewayRequestException("unreachable", 0, "The gateway answer could not be read.");
        }
    }

    private static GatewayRequestException Unreadable() => new("unreadable", 200, "The gateway answer could not be understood.");
}

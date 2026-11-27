using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Transactions;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// A fake public Esplora explorer for DigiDollar (specs/035 T008). It serves the paths <c>EsploraDigiDollarApi</c> calls
/// (<c>/blocks/tip/height</c>, <c>/address/{a}/txs</c>, <c>/address/{a}/txs/chain/{last}</c>, <c>/tx/{id}</c>,
/// <c>/tx/{id}/status</c>) for both networks, with well-formed DD transfer transactions built from <see cref="Pay"/>.
/// </summary>
public sealed class FakeDdExplorer : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly List<Payment> _payments = [];

    /// <summary>The chain tip for both networks. Above the mainnet DD activation height.</summary>
    public int Tip { get; set; } = 30_000_000;

    /// <summary>When true every request fails with 503, like an unreachable explorer.</summary>
    public bool Down { get; set; }

    /// <summary>Every request path, in order.</summary>
    public List<string> Requests { get; } = [];

    private sealed class Payment
    {
        public required string Txid { get; init; }
        public required string Address { get; init; }
        public required long Cents { get; init; }
        public int? Height { get; set; }
        public bool Vanished { get; set; }
    }

    /// <summary>Send <paramref name="cents"/> DD to <paramref name="address"/>; 0 confirmations means it's in the mempool. Returns the txid.</summary>
    public string Pay(string address, long cents, int confirmations = 0)
    {
        var txid = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (_lock)
            _payments.Add(new Payment { Txid = txid, Address = address, Cents = cents, Height = HeightFor(confirmations) });
        return txid;
    }

    /// <summary>Set a transaction's confirmations (0 = back in the mempool).</summary>
    public void Confirm(string txid, int confirmations)
    {
        lock (_lock) _payments.Single(p => p.Txid == txid).Height = HeightFor(confirmations);
    }

    /// <summary>Make a transaction disappear from the explorer entirely, as after a reorganisation.</summary>
    public void Vanish(string txid)
    {
        lock (_lock) _payments.Single(p => p.Txid == txid).Vanished = true;
    }

    public int RequestCount(Func<string, bool> match)
    {
        lock (Requests) return Requests.Count(match);
    }

    private int? HeightFor(int confirmations) => confirmations <= 0 ? null : Tip - confirmations + 1;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        lock (Requests) Requests.Add(path);
        if (Down) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") });

        var testnet = path.Contains("/testnet/");
        var rest = path[(path.IndexOf("/api", StringComparison.Ordinal) + 4)..];

        if (rest == "/blocks/tip/height") return Text(Tip.ToString());

        List<Payment> live;
        lock (_lock) live = _payments.Where(p => !p.Vanished).ToList();

        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[0] == "address" && parts[2] == "txs")
        {
            if (parts.Length > 3) return Json("[]"); // one page is always enough here
            var mine = live.Where(p => ExplorerAddress(p.Address) == parts[1])
                .OrderBy(p => p.Height is null ? 0 : 1).ThenByDescending(p => p.Height);
            return Json("[" + string.Join(",", mine.Select(p => TxJson(p, testnet))) + "]");
        }
        if (parts.Length >= 2 && parts[0] == "tx")
        {
            var tx = live.FirstOrDefault(p => p.Txid == parts[1]);
            if (tx is null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
            return parts.Length == 3 && parts[2] == "status" ? Json(StatusJson(tx)) : Json(TxJson(tx, testnet));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
    }

    private static string StatusJson(Payment p) => p.Height is { } h
        ? $"{{\"confirmed\":true,\"block_height\":{h},\"block_time\":1700000000}}"
        : "{\"confirmed\":false}";

    private static string TxJson(Payment p, bool testnet)
    {
        DigiDollarAddressValidator.TryDecode(p.Address, null, out _, out var key);
        var output = DigiDollarScripts.CreateOutputScript(key).ToHex();
        var opReturn = DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [p.Cents]).ToHex();
        var version = DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer);
        return $$"""
            {"txid":"{{p.Txid}}","version":{{version}},"vin":[],"fee":1000,
             "vout":[{"scriptpubkey":"{{output}}","value":0},{"scriptpubkey":"{{opReturn}}","value":0}],
             "status":{{StatusJson(p)}}}
            """;
    }

    private static Task<HttpResponseMessage> Text(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    /// <summary>The explorer's bech32m (witness v1) form of a DD address, as <c>EsploraDigiDollarApi</c> requests it.</summary>
    public static string ExplorerAddress(string ddAddress)
    {
        DigiDollarAddressValidator.TryDecode(ddAddress, null, out var network, out var key);
        return EncodeWitnessV1(network == "mainnet" ? "dgb" : "dgbt", key);
    }

    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    private static string EncodeWitnessV1(string hrp, byte[] program)
    {
        var data = new List<byte> { 1 };
        int acc = 0, bits = 0;
        foreach (var b in program)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5) { bits -= 5; data.Add((byte)((acc >> bits) & 31)); }
        }
        if (bits > 0) data.Add((byte)((acc << (5 - bits)) & 31));

        var values = new List<byte>();
        foreach (var c in hrp) values.Add((byte)(c >> 5));
        values.Add(0);
        foreach (var c in hrp) values.Add((byte)(c & 31));
        values.AddRange(data);
        values.AddRange(new byte[6]);
        uint[] gen = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++) if (((top >> i) & 1) != 0) chk ^= gen[i];
        }
        chk ^= 0x2bc830a3;

        var sb = new StringBuilder(hrp).Append('1');
        foreach (var d in data) sb.Append(Charset[d]);
        for (var i = 0; i < 6; i++) sb.Append(Charset[(int)((chk >> (5 * (5 - i))) & 31)]);
        return sb.ToString();
    }
}

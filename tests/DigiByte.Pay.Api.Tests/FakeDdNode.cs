using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// Stand-in for NodeApi's <c>/api/digidollar/*</c> routes behind <c>DigiDollarNodeClient</c> (specs/033 research R13).
/// Addresses come from <see cref="Addresses"/> (or are made up), balances from <see cref="Balances"/>, and
/// <see cref="Healthy"/> / <see cref="FailAddresses"/> simulate an outage.
/// </summary>
public sealed class FakeDdNode : HttpMessageHandler
{
    private int _counter;

    /// <summary>Addresses handed out by <c>address/new</c>, in order. Once empty, made-up ones are returned.</summary>
    public ConcurrentQueue<string> Addresses { get; } = new();

    /// <summary>Balance per address in USD cents. Missing = zero.</summary>
    public ConcurrentDictionary<string, (long Confirmed, long Unconfirmed)> Balances { get; } = new();

    /// <summary>Receive txid per address for <c>transactions</c>.</summary>
    public ConcurrentDictionary<string, string> Txids { get; } = new();

    public bool Healthy { get; set; } = true;

    /// <summary>When true, <c>address/new</c> answers 503 like an unreachable node.</summary>
    public bool FailAddresses { get; set; }

    /// <summary>Every path requested, for assertions such as "the node was never called".</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    // ---- DD payouts (specs/034 research R13) ----

    /// <summary>The wallet's receives, as <c>transactions?category=receive</c> lists them (oldest first).</summary>
    public List<FakeReceive> Receives { get; } = [];

    /// <summary>Sends made through <c>/send</c> (or added by a test), as <c>/sends</c> lists them.</summary>
    public List<FakeSend> Sends { get; } = [];

    /// <summary>The wallet's whole DD balance in cents (<c>/balance</c>). Null = the sum of receives minus sends.</summary>
    public long? WalletBalanceCents { get; set; }

    public bool ReserveHealthy { get; set; } = true;

    /// <summary>Overrides <c>/send</c>. Return null to fall through to the default (send succeeds, new txid).</summary>
    public Func<string, long, string?, HttpResponseMessage?>? OnSend { get; set; }

    /// <summary>Status per txid for <c>tx/{txid}/status</c>. Missing = <c>unknown</c>.</summary>
    public ConcurrentDictionary<string, (string Status, int Confirmations)> TxStatuses { get; } = new();

    public int SendCalls => Requests.Count(r => r.EndsWith("/api/digidollar/send", StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue(path);

        if (path.EndsWith("/api/digidollar/health", StringComparison.Ordinal))
            return Json(new { enabled = Healthy, deployment_active = Healthy, dgb_fee_reserve_dgb = ReserveHealthy ? 500m : 1m, reserve_healthy = ReserveHealthy });

        if (path.EndsWith("/api/digidollar/balance", StringComparison.Ordinal))
        {
            if (!Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            long total;
            lock (Receives) total = WalletBalanceCents ?? Receives.Where(r => r.Confirmations >= 0).Sum(r => r.AmountCents) - Sends.Sum(s => s.AmountCents);
            return Json(new { confirmed = total, unconfirmed = 0L, total });
        }

        if (path.EndsWith("/api/digidollar/send", StringComparison.Ordinal))
        {
            if (!Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var doc = JsonDocument.Parse(request.Content!.ReadAsStringAsync(cancellationToken).Result);
            var to = doc.RootElement.GetProperty("address").GetString()!;
            var cents = doc.RootElement.GetProperty("amountCents").GetInt64();
            var comment = doc.RootElement.TryGetProperty("comment", out var c) ? c.GetString() : null;
            if (OnSend?.Invoke(to, cents, comment) is { } custom) return Task.FromResult(custom);
            var sendTxid = NewTxid();
            lock (Receives) Sends.Add(new FakeSend(sendTxid, to, cents, comment, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0));
            return Json(new { txid = sendTxid });
        }

        if (path.EndsWith("/api/digidollar/sends", StringComparison.Ordinal))
        {
            if (!Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            lock (Receives)
                return Json(new { items = Sends.OrderByDescending(s => s.Time).Select(s => new { txid = s.Txid, address = s.Address, amountCents = s.AmountCents, comment = s.Comment, time = s.Time, confirmations = s.Confirmations }) });
        }

        const string txPrefix = "/api/digidollar/tx/";
        var txAt = path.IndexOf(txPrefix, StringComparison.Ordinal);
        if (txAt >= 0 && path.EndsWith("/status", StringComparison.Ordinal))
        {
            var id = path[(txAt + txPrefix.Length)..^"/status".Length];
            var (status, confs) = TxStatuses.TryGetValue(id, out var st) ? st : ("unknown", 0);
            return Json(new { status, confirmations = confs });
        }

        if (path.EndsWith("/api/digidollar/address/new", StringComparison.Ordinal))
        {
            if (FailAddresses || !Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var address = Addresses.TryDequeue(out var next) ? next : $"DDfake{Interlocked.Increment(ref _counter):D6}";
            return Json(new { address });
        }

        const string balancePrefix = "/api/digidollar/balance/";
        var at = path.IndexOf(balancePrefix, StringComparison.Ordinal);
        if (at >= 0)
        {
            if (!Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var address = Uri.UnescapeDataString(path[(at + balancePrefix.Length)..]);
            var (confirmed, unconfirmed) = Balances.TryGetValue(address, out var b) ? b : (0L, 0L);
            return Json(new { confirmed, unconfirmed, total = confirmed + unconfirmed });
        }

        if (path.EndsWith("/api/digidollar/transactions", StringComparison.Ordinal))
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var address = query["address"] ?? "";
            if (address.Length == 0 && query["category"] == "receive")
            {
                if (!Healthy) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                var count = int.TryParse(query["count"], out var n) ? n : 20;
                var skip = int.TryParse(query["skip"], out var k) ? k : 0;
                lock (Receives)
                {
                    // Like listtransactions: the most recent `count` after skipping `skip`, oldest first.
                    var page = Receives.AsEnumerable().Reverse().Skip(skip).Take(count).Reverse()
                        .Select(r => new { txid = r.Txid, address = r.Address, amount = r.AmountCents, confirmations = r.Confirmations, time = r.Time, category = "receive" })
                        .ToList();
                    return Json(page);
                }
            }
            return Txids.TryGetValue(address, out var txid) ? Json(new[] { new { txid } }) : Json(Array.Empty<object>());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>Adds a receive to the wallet and returns its txid.</summary>
    public string Receive(string address, long cents, int confirmations = 6, DateTime? at = null)
    {
        var txid = NewTxid();
        lock (Receives) Receives.Add(new FakeReceive(txid, address, cents, confirmations, new DateTimeOffset(at ?? DateTime.UtcNow).ToUnixTimeSeconds()));
        return txid;
    }

    public void SetConfirmations(string txid, int confirmations)
    {
        lock (Receives)
        {
            var i = Receives.FindIndex(r => r.Txid == txid);
            if (i >= 0) Receives[i] = Receives[i] with { Confirmations = confirmations };
        }
    }

    private static string NewTxid() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static HttpResponseMessage Reply(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };

    private static Task<HttpResponseMessage> Json(object body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        });
}

public sealed record FakeReceive(string Txid, string Address, long AmountCents, int Confirmations, long Time);

public sealed record FakeSend(string Txid, string Address, long AmountCents, string? Comment, long Time, int Confirmations);

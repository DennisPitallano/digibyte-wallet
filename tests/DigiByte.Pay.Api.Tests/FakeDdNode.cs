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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue(path);

        if (path.EndsWith("/api/digidollar/health", StringComparison.Ordinal))
            return Json(new { enabled = Healthy, deployment_active = Healthy });

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
            return Txids.TryGetValue(address, out var txid) ? Json(new[] { new { txid } }) : Json(Array.Empty<object>());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json(object body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        });
}

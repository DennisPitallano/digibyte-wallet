using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Test doubles shared by the node-source tests (specs/005-wallet-own-node-source).</summary>
internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

internal sealed class FakeHealthSource : INodeHealthSource
{
    public Func<CancellationToken, Task<NodeHealthResult>> Respond { get; set; } =
        _ => Task.FromResult(Ok());

    public int Calls;

    public Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Respond(cancellationToken);
    }

    public static NodeHealthResult Ok(int blocks = 1000) =>
        NodeReadiness.ParseHealth(200,
            $"{{\"status\":\"ok\",\"blocks\":{blocks},\"headers\":{blocks},\"lag\":0,\"peers\":8,\"txindex\":true,\"versionOk\":true}}");

    public static NodeHealthResult WithStatus(string status, int blocks = 1000) =>
        NodeReadiness.ParseHealth(200,
            $"{{\"status\":\"{status}\",\"blocks\":{blocks},\"headers\":{blocks},\"lag\":0,\"peers\":8,\"txindex\":true,\"versionOk\":true}}");

    public void Returns(NodeHealthResult result) => Respond = _ => Task.FromResult(result);
}

internal sealed class FakeExplorerHeightSource : IExplorerHeightSource
{
    public Func<CancellationToken, Task<int?>> Respond { get; set; } = _ => Task.FromResult<int?>(1000);

    public int Calls;

    public Task<int?> GetHeightAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Calls);
        return Respond(cancellationToken);
    }

    public void Returns(int? height) => Respond = _ => Task.FromResult(height);
}

/// <summary>An explorer that only knows its block height; every other call is unsupported.</summary>
internal sealed class HeightOnlyExplorer(Func<Task<int>> height) : IBlockchainService
{
    public int Calls;

    public Task<int> GetBlockHeightAsync()
    {
        Interlocked.Increment(ref Calls);
        return height();
    }

    public Task<long> GetBalanceAsync(string address) => throw new NotSupportedException();
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
    public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new NotSupportedException();
    public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
}

/// <summary>A node state the tests can set directly; also records the read reports the fallback service sends.</summary>
internal sealed class FakeNodeState : INodeSourceState, INodeReadFailureSink
{
    public NodeSourceState State { get; set; } = NodeSourceState.NotReady;
    public NodeNotReadyReason Reason { get; set; }
    public bool IsActive => State == NodeSourceState.Active;
    public bool NodeWasActive { get; set; }
    public NodeReadinessSnapshot? LastSnapshot { get; set; }
    public event Action? Changed;

    public int Failures;
    public int Successes;

    public void ReportNodeReadFailure() => Failures++;
    public void ReportNodeReadSuccess() => Successes++;

    public void Set(NodeSourceState state)
    {
        State = state;
        if (state == NodeSourceState.Active) NodeWasActive = true;
        Changed?.Invoke();
    }
}

/// <summary>An explorer with configurable answers that records which methods were called.</summary>
internal sealed class ScriptedExplorer(string name) : IBlockchainService
{
    public List<string> Calls { get; } = [];
    public bool Fails { get; set; }
    public long Balance { get; set; } = 100;
    public int Height { get; set; } = 1000;
    public decimal FeeRate { get; set; } = 0.2m;
    public string BroadcastTxId { get; set; } = name + "-txid";
    public bool BroadcastFails { get; set; }
    public TransactionInfo? Transaction { get; set; }
    public List<TransactionInfo> History { get; set; } = [];
    public List<UtxoInfo> Utxos { get; set; } = [];

    private void Record(string call)
    {
        lock (Calls) Calls.Add(call);
        if (Fails) throw new Exception($"{name}: read failed");
    }

    public Task<long> GetBalanceAsync(string address) { Record("balance"); return Task.FromResult(Balance); }
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) { Record("balance"); return Task.FromResult(Balance); }
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) { Record("utxos"); return Task.FromResult(Utxos); }
    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) { Record("utxos"); return Task.FromResult(Utxos); }
    public Task<TransactionInfo?> GetTransactionAsync(string txId) { Record("tx"); return Task.FromResult(Transaction); }
    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) { Record("history"); return Task.FromResult(History); }
    public Task<decimal> GetFeeRateAsync() { Record("fee"); return Task.FromResult(FeeRate); }
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
    public Task<int> GetBlockHeightAsync() { Record("height"); return Task.FromResult(Height); }

    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        lock (Calls) Calls.Add("broadcast");
        if (BroadcastFails) throw new Exception($"{name}: broadcast failed");
        return Task.FromResult(BroadcastTxId);
    }
}

/// <summary>A fake node API: records requests and answers by path. Build the client with <see cref="Client"/>.</summary>
internal sealed class FakeNodeApi
{
    private sealed class Handler(FakeNodeApi owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (owner.Requests) owner.Requests.Add($"{request.Method} {path}");
            if (owner.Delay is { } delay) await Task.Delay(delay, ct);
            return owner.Respond(request);
        }
    }

    public List<string> Requests { get; } = [];
    public TimeSpan? Delay { get; set; }
    public bool Fails { get; set; }
    public long Balance { get; set; } = 100;
    public int Height { get; set; } = 1000;
    public double FeeRate { get; set; } = 0.1;
    public string BroadcastTxId { get; set; } = "node-txid";
    public bool TransactionNotFound { get; set; }

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (Fails)
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"node down\"}") };

        var path = request.RequestUri!.AbsolutePath;
        string body;
        if (path.EndsWith("/balance") || path == "/api/address/balances") body = $"{{\"balanceSatoshis\":{Balance}}}";
        else if (path.EndsWith("/utxos")) body = "{\"utxos\":[]}";
        else if (path == "/api/blockchain/height") body = $"{{\"height\":{Height}}}";
        else if (path.StartsWith("/api/network/fee/")) body = $"{{\"feerate\":{FeeRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        else if (path == "/api/tx/broadcast") body = $"{{\"txid\":\"{BroadcastTxId}\"}}";
        else if (path.StartsWith("/api/tx/"))
        {
            if (TransactionNotFound)
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            body = $"{{\"txid\":\"{path[8..]}\",\"confirmations\":7,\"vin\":[],\"vout\":[]}}";
        }
        else body = "{}";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    public int RequestCount { get { lock (Requests) return Requests.Count; } }

    /// <summary>A node client wired to this fake. Strict, because that is how the app builds it when the feature is on.</summary>
    public NodeApiBlockchainService Client(bool strict = true) =>
        new(new HttpClient(new Handler(this)), "http://fake-node:5260", strict);
}

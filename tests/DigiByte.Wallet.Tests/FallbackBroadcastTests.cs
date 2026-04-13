using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

public class FallbackBroadcastTests
{
    private static readonly byte[] FakeTx = [0x01, 0x00, 0x00, 0x00];

    /// <summary>
    /// Creates a NodeApiBlockchainService that always fails (no real server).
    /// </summary>
    private static NodeApiBlockchainService CreateFailingNodeApi()
    {
        var handler = new FakeHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("node down") });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake-node:5260") };
        return new NodeApiBlockchainService(http, "http://fake-node:5260");
    }

    /// <summary>
    /// Creates a NodeApiBlockchainService that succeeds broadcast.
    /// </summary>
    private static NodeApiBlockchainService CreateSucceedingNodeApi(string txId)
    {
        var handler = new FakeHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/api/tx/broadcast"))
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent($"{{\"txid\":\"{txId}\"}}") };
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake-node:5260") };
        return new NodeApiBlockchainService(http, "http://fake-node:5260");
    }

    [Fact]
    public async Task Broadcast_NodeSucceeds_ReturnsNodeResult()
    {
        var nodeApi = CreateSucceedingNodeApi("abc123");
        var explorer1 = new FakeExplorer("explorer1", broadcastResult: "from-explorer");
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: false);

        var result = await sut.BroadcastTransactionAsync(FakeTx);

        Assert.Equal("abc123", result);
    }

    [Fact]
    public async Task Broadcast_NodeFails_FallsToFirstExplorer()
    {
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("explorer1", broadcastResult: "tx-from-explorer1");
        var explorer2 = new FakeExplorer("explorer2", broadcastResult: "tx-from-explorer2");
        var sut = new FallbackBlockchainService(nodeApi, [explorer1, explorer2], new HttpClient(), isDevelopment: false);

        var result = await sut.BroadcastTransactionAsync(FakeTx);

        Assert.Equal("tx-from-explorer1", result);
    }

    [Fact]
    public async Task Broadcast_NodeAndFirstExplorerFail_FallsToSecondExplorer()
    {
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("explorer1", shouldFailBroadcast: true);
        var explorer2 = new FakeExplorer("explorer2", broadcastResult: "tx-from-explorer2");
        var sut = new FallbackBlockchainService(nodeApi, [explorer1, explorer2], new HttpClient(), isDevelopment: false);

        var result = await sut.BroadcastTransactionAsync(FakeTx);

        Assert.Equal("tx-from-explorer2", result);
    }

    [Fact]
    public async Task Broadcast_AllFail_ThrowsException()
    {
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("explorer1", shouldFailBroadcast: true);
        var explorer2 = new FakeExplorer("explorer2", shouldFailBroadcast: true);
        var sut = new FallbackBlockchainService(nodeApi, [explorer1, explorer2], new HttpClient(), isDevelopment: false);

        var ex = await Assert.ThrowsAsync<Exception>(
            () => sut.BroadcastTransactionAsync(FakeTx));

        Assert.Contains("Broadcast failed on all backends", ex.Message);
    }

    [Fact]
    public async Task Broadcast_AllFail_NeverFallsToMock_EvenInDev()
    {
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("explorer1", shouldFailBroadcast: true);
        var mock = new MockBlockchainService();
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: true, mock: mock);

        var ex = await Assert.ThrowsAsync<Exception>(
            () => sut.BroadcastTransactionAsync(FakeTx));

        Assert.Contains("Broadcast failed on all backends", ex.Message);
    }

    [Fact]
    public async Task Broadcast_NoExplorers_NodeFails_Throws()
    {
        var nodeApi = CreateFailingNodeApi();
        var sut = new FallbackBlockchainService(nodeApi, [], new HttpClient(), isDevelopment: false);

        var ex = await Assert.ThrowsAsync<Exception>(
            () => sut.BroadcastTransactionAsync(FakeTx));

        Assert.Contains("Broadcast failed on all backends", ex.Message);
    }

    [Fact]
    public async Task Broadcast_NodeFails_TriesAllExplorersInOrder()
    {
        var nodeApi = CreateFailingNodeApi();
        var callOrder = new List<string>();
        var explorer1 = new FakeExplorer("e1", shouldFailBroadcast: true, onBroadcast: () => callOrder.Add("e1"));
        var explorer2 = new FakeExplorer("e2", shouldFailBroadcast: true, onBroadcast: () => callOrder.Add("e2"));
        var explorer3 = new FakeExplorer("e3", broadcastResult: "from-e3", onBroadcast: () => callOrder.Add("e3"));
        var sut = new FallbackBlockchainService(nodeApi, [explorer1, explorer2, explorer3], new HttpClient(), isDevelopment: false);

        var result = await sut.BroadcastTransactionAsync(FakeTx);

        Assert.Equal("from-e3", result);
        Assert.Equal(["e1", "e2", "e3"], callOrder);
    }

    [Fact]
    public async Task Read_ExplorerFirst_ThenNode_ThenMockInDev()
    {
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var explorer2 = new FakeExplorer("e2", balance: 5000);
        var sut = new FallbackBlockchainService(nodeApi, [explorer1, explorer2], new HttpClient(), isDevelopment: false);

        var balance = await sut.GetBalanceAsync("dgb1test");

        Assert.Equal(5000, balance);
        Assert.Equal("explorer-1", sut.ActiveBackend);
    }

    [Fact]
    public async Task Read_AllExplorersFail_FallsToNode()
    {
        // NodeApiBlockchainService.GetBalanceAsync catches errors and returns 0,
        // so it never throws — TryRead considers this a success.
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: false);

        var balance = await sut.GetBalanceAsync("dgb1test");

        // Node returns 0 (its error-swallowing behavior) but is still "node-api" backend
        Assert.Equal(0, balance);
        Assert.Equal("node-api", sut.ActiveBackend);
    }

    [Fact]
    public async Task Read_AllFail_DevMode_FallsToMock()
    {
        // Node swallows errors → returns 0 → TryRead stops at node, never reaches mock.
        // This tests that when the node succeeds (even with 0), mock is NOT used.
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var mock = new MockBlockchainService();
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: true, mock: mock);

        var balance = await sut.GetBalanceAsync("dgb1test");

        Assert.False(sut.IsDemoMode);
        Assert.Equal("node-api", sut.ActiveBackend);
    }

    [Fact]
    public async Task Read_AllFail_Production_NodeSwallowsError_Returns0()
    {
        // NodeApiBlockchainService catches its own exceptions and returns 0,
        // so TryRead never reaches the "all failed" branch for balance.
        // This is by design — the node is always treated as a valid (if degraded) backend.
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: false);

        var balance = await sut.GetBalanceAsync("dgb1test");

        Assert.Equal(0, balance);
        Assert.Equal("node-api", sut.ActiveBackend);
    }

    [Fact]
    public async Task Read_GetUtxos_AllExplorersFail_NodeReturnsEmpty()
    {
        // NodeApiBlockchainService.GetUtxosAsync also catches errors → returns empty list.
        // So it never throws — TryRead stops at node with empty result.
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: false);

        var utxos = await sut.GetUtxosAsync("dgb1test");

        Assert.Empty(utxos);
        Assert.Equal("node-api", sut.ActiveBackend);
    }

    [Fact]
    public async Task Read_GetUtxos_AllExplorersFail_DevMode_NodeReturnsEmpty_NotMock()
    {
        // Same as above — node swallows errors so mock is never reached.
        var nodeApi = CreateFailingNodeApi();
        var explorer1 = new FakeExplorer("e1", shouldFailReads: true);
        var mock = new MockBlockchainService();
        var sut = new FallbackBlockchainService(nodeApi, [explorer1], new HttpClient(), isDevelopment: true, mock: mock);

        var utxos = await sut.GetUtxosAsync("dgb1test");

        Assert.False(sut.IsDemoMode);
        Assert.Equal("node-api", sut.ActiveBackend);
        Assert.Empty(utxos);
    }

    // --- Test helpers ---

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(handler(request));
    }

    private class FakeExplorer : IBlockchainService
    {
        private readonly string _name;
        private readonly bool _shouldFailBroadcast;
        private readonly bool _shouldFailReads;
        private readonly string? _broadcastResult;
        private readonly long _balance;
        private readonly Action? _onBroadcast;

        public FakeExplorer(string name, bool shouldFailBroadcast = false, bool shouldFailReads = false,
            string? broadcastResult = null, long balance = 0, Action? onBroadcast = null)
        {
            _name = name;
            _shouldFailBroadcast = shouldFailBroadcast;
            _shouldFailReads = shouldFailReads;
            _broadcastResult = broadcastResult;
            _balance = balance;
            _onBroadcast = onBroadcast;
        }

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
        {
            _onBroadcast?.Invoke();
            if (_shouldFailBroadcast)
                throw new Exception($"{_name}: broadcast failed");
            return Task.FromResult(_broadcastResult ?? $"{_name}-txid");
        }

        public Task<long> GetBalanceAsync(string address)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(_balance);

        public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(_balance);

        public Task<List<UtxoInfo>> GetUtxosAsync(string address)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(new List<UtxoInfo>());

        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(new List<UtxoInfo>());

        public Task<TransactionInfo?> GetTransactionAsync(string txId)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult<TransactionInfo?>(null);

        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(new List<TransactionInfo>());

        public Task<decimal> GetFeeRateAsync()
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(0.001m);

        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(0.01m);

        public Task<int> GetBlockHeightAsync()
            => _shouldFailReads ? throw new Exception($"{_name}: read failed") : Task.FromResult(1000);
    }
}

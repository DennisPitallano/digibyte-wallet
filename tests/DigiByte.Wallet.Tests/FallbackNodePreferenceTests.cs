using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Read and broadcast ordering of <see cref="FallbackBlockchainService"/> when our own node is a source
/// (specs/005-wallet-own-node-source, research R3 and R14).
/// </summary>
public class FallbackNodePreferenceTests
{
    private static readonly byte[] Tx = [1, 0, 0, 0];

    private sealed class Rig
    {
        public FakeNodeApi Node { get; } = new();
        public ScriptedExplorer Explorer1 { get; } = new("explorer1");
        public ScriptedExplorer Explorer2 { get; } = new("explorer2");
        public FakeNodeState State { get; } = new();
        public FallbackBlockchainService Sut { get; }

        public Rig(NodeSourceState state = NodeSourceState.NotReady, bool withMonitor = true, bool strictNode = true)
        {
            State.State = state;
            Sut = new FallbackBlockchainService(
                Node.Client(strictNode), [Explorer1, Explorer2], new HttpClient(), isDevelopment: false,
                nodeState: withMonitor ? State : null, failureSink: withMonitor ? State : null);
            Sut.FastNodeReadBudget = TimeSpan.FromMilliseconds(400);
            Sut.AddressNodeReadBudget = TimeSpan.FromMilliseconds(800);
        }

        public void AllExplorersFail()
        {
            Explorer1.Fails = true;
            Explorer2.Fails = true;
        }
    }

    // ================= feature on, node not active: the node is never used =================

    public static IEnumerable<object[]> NotActiveStates() =>
    [
        [NodeSourceState.Unavailable],
        [NodeSourceState.NotReady],
        [NodeSourceState.Stabilizing],
    ];

    [Theory, MemberData(nameof(NotActiveStates))]
    public async Task NotActive_ReadsComeFromExplorers_AndNeverTouchTheNode(NodeSourceState state)
    {
        var rig = new Rig(state);

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        await rig.Sut.GetUtxosAsync("DAbc");
        Assert.Equal(1000, await rig.Sut.GetBlockHeightAsync());
        Assert.Equal(0.2m, await rig.Sut.GetFeeRateAsync());
        await rig.Sut.GetTransactionAsync("abc");

        Assert.Equal(0, rig.Node.RequestCount);
    }

    [Theory, MemberData(nameof(NotActiveStates))]
    public async Task NotActive_EvenWhenEveryExplorerFails_TheNodeIsNotAskedForData(NodeSourceState state)
    {
        var rig = new Rig(state);
        rig.AllExplorersFail();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBlockHeightAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetUtxosAsync("DAbc"));

        Assert.Contains("unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, rig.Node.RequestCount);
    }

    [Theory, MemberData(nameof(NotActiveStates))]
    public async Task NotActive_BroadcastUsesExplorersFirst_AndNeverTheNode(NodeSourceState state)
    {
        var rig = new Rig(state);

        var txid = await rig.Sut.BroadcastTransactionAsync(Tx);

        Assert.Equal("explorer1-txid", txid);
        Assert.Equal(0, rig.Node.RequestCount);
    }

    [Theory, MemberData(nameof(NotActiveStates))]
    public async Task NotActive_BroadcastFailingEverywhere_StillNeverTriesTheNode(NodeSourceState state)
    {
        var rig = new Rig(state);
        rig.Explorer1.BroadcastFails = true;
        rig.Explorer2.BroadcastFails = true;

        var ex = await Assert.ThrowsAsync<Exception>(() => rig.Sut.BroadcastTransactionAsync(Tx));

        Assert.Contains("Broadcast failed on all backends", ex.Message);
        Assert.Equal(0, rig.Node.RequestCount);
    }

    // ================= feature off: exactly today's order =================

    public static IEnumerable<object[]> FeatureOffSetups() =>
    [
        [NodeSourceState.Disabled, true],    // monitor present but feature off
        [NodeSourceState.Disabled, false],   // no monitor at all
    ];

    [Theory, MemberData(nameof(FeatureOffSetups))]
    public async Task FeatureOff_ReadsStayExplorerFirst(NodeSourceState state, bool withMonitor)
    {
        var rig = new Rig(state, withMonitor, strictNode: false);

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal(0, rig.Node.RequestCount);
        Assert.Single(rig.Explorer1.Calls);
    }

    [Theory, MemberData(nameof(FeatureOffSetups))]
    public async Task FeatureOff_NodeIsStillTheLastResortForReads(NodeSourceState state, bool withMonitor)
    {
        var rig = new Rig(state, withMonitor, strictNode: false);
        rig.AllExplorersFail();
        rig.Node.Balance = 4242;

        Assert.Equal(4242, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Contains("GET /api/address/DAbc/balance", rig.Node.Requests);
    }

    [Theory, MemberData(nameof(FeatureOffSetups))]
    public async Task FeatureOff_BroadcastStillTriesTheNodeFirst(NodeSourceState state, bool withMonitor)
    {
        var rig = new Rig(state, withMonitor, strictNode: false);

        var txid = await rig.Sut.BroadcastTransactionAsync(Tx);

        Assert.Equal("node-txid", txid);
        Assert.Empty(rig.Explorer1.Calls);
    }

    [Theory, MemberData(nameof(FeatureOffSetups))]
    public async Task FeatureOff_NodeFailureIsStillSwallowedAsBefore(NodeSourceState state, bool withMonitor)
    {
        // Today's lenient node client: a failing node answers with a neutral value instead of an error.
        var rig = new Rig(state, withMonitor, strictNode: false);
        rig.AllExplorersFail();
        rig.Node.Fails = true;

        Assert.Equal(0, await rig.Sut.GetBalanceAsync("DAbc"));
    }

    // ================= feature on, node active (T018) =================

    private static TransactionInfo TxInfo(string id, int confirmations) => new()
    {
        TxId = id, Confirmations = confirmations, Timestamp = DateTime.UtcNow, FeeSatoshis = 0, Inputs = [], Outputs = [],
    };

    private static readonly byte[] TxBytes = [1, 0, 0, 0];

    [Fact]
    public async Task Active_FastRoutes_AreAnsweredByTheNodeFirst()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.Height = 2000;
        rig.Node.FeeRate = 0.1;

        Assert.Equal(2000, await rig.Sut.GetBlockHeightAsync());
        Assert.Equal(0.1m, await rig.Sut.GetFeeRateAsync());
        Assert.Equal("abc", (await rig.Sut.GetTransactionAsync("abc"))!.TxId);

        Assert.Empty(rig.Explorer1.Calls);
    }

    [Fact]
    public async Task Active_FastRead_NodeError_IsAnsweredByAnExplorer_NeverAsZeroOrADefault()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.Fails = true;

        Assert.Equal(1000, await rig.Sut.GetBlockHeightAsync());   // not 0
        Assert.Equal(0.2m, await rig.Sut.GetFeeRateAsync());       // not the 0.00001 default

        Assert.Equal(2, rig.State.Failures);
    }

    [Fact]
    public async Task Active_TransactionNotFoundOnTheNode_AsksTheExplorersBeforeSayingUnknown()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.TransactionNotFound = true;
        rig.Explorer1.Transaction = TxInfo("abc", 3);

        var tx = await rig.Sut.GetTransactionAsync("abc");

        Assert.Equal("abc", tx!.TxId);
        Assert.Equal(0, rig.State.Failures);   // not found is not a failed read
    }

    [Fact]
    public async Task Active_TransactionNotFoundAnywhere_IsNull()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.TransactionNotFound = true;

        Assert.Null(await rig.Sut.GetTransactionAsync("abc"));
    }

    [Fact]
    public async Task Active_FastRead_OverTheBudget_IsAbandonedAndAnsweredByAnExplorer()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.Delay = TimeSpan.FromSeconds(3);

        var started = DateTime.UtcNow;
        var height = await rig.Sut.GetBlockHeightAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(1000, height);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"took {elapsed}");
        Assert.Equal(1, rig.State.Failures);
    }

    [Fact]
    public async Task Active_AddressReads_StayExplorerFirst()
    {
        var rig = new Rig(NodeSourceState.Active);

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        await rig.Sut.GetUtxosAsync("DAbc");

        Assert.Equal(0, rig.Node.RequestCount);
    }

    [Fact]
    public async Task Active_AddressReads_UseTheNodeOnlyWhenEveryExplorerFails()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.AllExplorersFail();
        rig.Node.Balance = 777;

        Assert.Equal(777, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal(1, rig.State.Successes);
    }

    [Fact]
    public async Task Active_AddressRead_NodeAlsoFailing_ThrowsInsteadOfShowingZero()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.AllExplorersFail();
        rig.Node.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetUtxosAsync("DAbc"));
    }

    [Fact]
    public async Task Active_History_ComesFromExplorersOnly_EvenWhenTheyAllFail()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Explorer1.History = [TxInfo("h1", 5)];

        Assert.Equal("h1", Assert.Single(await rig.Sut.GetAddressTransactionsAsync("DAbc")).TxId);

        rig.AllExplorersFail();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetAddressTransactionsAsync("DAbc", 1, 10));
        Assert.Equal(0, rig.Node.RequestCount);
    }

    [Fact]
    public async Task Active_Broadcast_TriesTheNodeFirst_AndIsNeverRepeated()
    {
        var rig = new Rig(NodeSourceState.Active);

        var txid = await rig.Sut.BroadcastTransactionAsync(TxBytes);

        Assert.Equal("node-txid", txid);
        Assert.Empty(rig.Explorer1.Calls);
        Assert.Empty(rig.Explorer2.Calls);
        Assert.Single(rig.Node.Requests);
    }

    [Fact]
    public async Task Active_Broadcast_NodeFailure_FallsThroughToAnExplorer_WithoutBlamingTheNodeRead()
    {
        var rig = new Rig(NodeSourceState.Active);
        rig.Node.Fails = true;

        var txid = await rig.Sut.BroadcastTransactionAsync(TxBytes);

        Assert.Equal("explorer1-txid", txid);
        Assert.Equal(0, rig.State.Failures);   // a rejected broadcast is not a failed read
    }

    // ---------- switching ----------

    [Fact]
    public async Task ASourceSwitch_ClearsTheReadCache()
    {
        var rig = new Rig(NodeSourceState.NotReady);
        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        rig.Explorer1.Balance = 200;

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));   // still cached
        rig.State.Set(NodeSourceState.Active);

        Assert.Equal(200, await rig.Sut.GetBalanceAsync("DAbc"));   // switched: fresh read
    }

    [Fact]
    public async Task TwentyForcedSwitches_NeverLoseOrMixBalanceUtxosOrHistory()
    {
        var rig = new Rig(NodeSourceState.NotReady);
        rig.Explorer1.History = [TxInfo("h1", 5), TxInfo("h2", 6)];
        rig.Explorer1.Utxos = [new UtxoInfo { TxId = "u1", OutputIndex = 0, AmountSatoshis = 50, ScriptPubKey = "76", Confirmations = 9 }];

        for (var i = 1; i <= 20; i++)
        {
            rig.State.Set(i % 2 == 0 ? NodeSourceState.Active : NodeSourceState.NotReady);
            rig.Explorer1.Balance = i * 10;
            rig.Node.Balance = i * 10;

            Assert.Equal(i * 10, await rig.Sut.GetBalanceAsync("DAbc"));
            var utxo = Assert.Single(await rig.Sut.GetUtxosAsync("DAbc"));
            Assert.Equal("u1", utxo.TxId);
            Assert.Equal(["h1", "h2"], (await rig.Sut.GetAddressTransactionsAsync("DAbc")).Select(t => t.TxId));
        }
    }

    [Fact]
    public async Task Switches_DoNotTouchPendingTransactions()
    {
        var storage = new MemoryStorage();
        var tracker = new TransactionTracker(storage);
        tracker.SetActiveWallet("w");
        await tracker.RecordSendAsync("pending1", "dgb1qdest", 1_000, 10);

        var rig = new Rig(NodeSourceState.NotReady);
        rig.Explorer1.Transaction = TxInfo("pending1", 7);

        for (var i = 0; i < 10; i++)
        {
            rig.State.Set(i % 2 == 0 ? NodeSourceState.Active : NodeSourceState.NotReady);
            await tracker.UpdateConfirmationsAsync(rig.Sut);
        }

        var all = await tracker.GetAllAsync();
        var entry = Assert.Single(all);
        Assert.Equal("pending1", entry.TxId);
        Assert.Equal(7, entry.Confirmations);
    }

    // ---------- failure-driven demotion with the real monitor ----------

    private sealed class MonitorRig
    {
        public FakeNodeApi Node { get; } = new();
        public ScriptedExplorer Explorer { get; } = new("explorer1");
        public NodeSourceMonitor Monitor { get; }
        public FallbackBlockchainService Sut { get; }

        public MonitorRig()
        {
            var clock = new FakeClock();
            var options = new NodeSourceOptions
            {
                Enabled = true, ApiUrl = "https://node.example.org",
                CheckInterval = TimeSpan.FromSeconds(30), StablePeriod = TimeSpan.FromSeconds(120),
                HealthTimeout = TimeSpan.FromMilliseconds(300), ExplorerBudget = TimeSpan.FromMilliseconds(300),
            };
            Monitor = new NodeSourceMonitor(options, new FakeHealthSource(), new FakeExplorerHeightSource(), clock);
            Sut = new FallbackBlockchainService(Node.Client(), [Explorer], new HttpClient(), isDevelopment: false,
                nodeState: Monitor, failureSink: Monitor);
            Sut.FastNodeReadBudget = TimeSpan.FromMilliseconds(400);

            for (var i = 0; i < 5; i++)
            {
                Monitor.CheckAsync().GetAwaiter().GetResult();
                clock.Advance(TimeSpan.FromSeconds(30));
            }
            Assert.True(Monitor.IsActive);
        }
    }

    [Fact]
    public async Task ThreeFailedNodeReads_DemoteTheNode_AndLaterRequestsSkipIt()
    {
        var rig = new MonitorRig();
        rig.Explorer.Transaction = TxInfo("abc", 3);
        rig.Node.Fails = true;

        for (var i = 0; i < 3; i++)
            Assert.Equal("abc", (await rig.Sut.GetTransactionAsync("abc"))!.TxId);   // each answered by the explorer (transaction lookups are not cached)
        Assert.Equal(3, rig.Node.RequestCount);
        Assert.False(rig.Monitor.IsActive);
        Assert.Equal(NodeNotReadyReason.ReadFailures, rig.Monitor.Reason);

        Assert.Equal("abc", (await rig.Sut.GetTransactionAsync("abc"))!.TxId);
        Assert.Equal(3, rig.Node.RequestCount);   // the node is no longer asked
    }

    [Fact]
    public async Task ASuccessfulNodeRead_ResetsTheFailureCount()
    {
        var rig = new MonitorRig();
        rig.Explorer.Transaction = TxInfo("abc", 3);

        rig.Node.Fails = true;
        await rig.Sut.GetTransactionAsync("abc");
        await rig.Sut.GetTransactionAsync("abc");
        rig.Node.Fails = false;
        Assert.NotNull(await rig.Sut.GetTransactionAsync("abc"));   // a real node answer resets the count
        rig.Node.Fails = true;
        await rig.Sut.GetTransactionAsync("abc");
        await rig.Sut.GetTransactionAsync("abc");

        Assert.True(rig.Monitor.IsActive);
    }
}

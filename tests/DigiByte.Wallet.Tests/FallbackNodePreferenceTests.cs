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
}

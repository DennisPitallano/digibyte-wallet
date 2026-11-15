using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// With the gateway stopped, syncing or behind, every read and send gives the same answer as it does today and no read ever waits on
/// the gateway health check (spec FR-018, FR-019, SC-005, SC-006; T099, T102).
/// </summary>
public class GatewayDegradedFlowsTests
{
    private static readonly byte[] Tx = [1, 0, 0, 0];

    /// <summary>A health source that never answers, like a gateway that accepted the connection and then went silent.</summary>
    private sealed class HangingHealth : INodeHealthSource
    {
        public int Calls { get; private set; }

        public async Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return NodeHealthResult.Unreachable;
        }
    }

    private sealed class Rig
    {
        public ScriptedExplorer Explorer1 { get; } = new("explorer1");
        public ScriptedExplorer Explorer2 { get; } = new("explorer2");
        public ScriptedExplorer Gateway { get; } = new("gateway") { Balance = 777, Height = 5000 };
        public FakeNodeState GatewayState { get; } = new();
        public FakeNodeState NodeState { get; } = new() { State = NodeSourceState.NotReady };
        public FakeNodeApi Node { get; } = new();
        public FallbackBlockchainService Sut { get; }
        public FallbackBlockchainService Baseline { get; }

        public Rig(NodeSourceState gatewayState, GatewayPosition position = GatewayPosition.Backup, INodeSourceState? stateOverride = null)
        {
            GatewayState.State = gatewayState;
            Sut = Build(withGateway: true, position, stateOverride);
            Baseline = Build(withGateway: false, position, null);
        }

        private FallbackBlockchainService Build(bool withGateway, GatewayPosition position, INodeSourceState? stateOverride) =>
            new(Node.Client(true), [Explorer1, Explorer2], new HttpClient(), isDevelopment: false,
                nodeState: NodeState, failureSink: NodeState,
                gatewaySource: withGateway ? Gateway : null,
                gatewayState: withGateway ? stateOverride ?? GatewayState : null,
                gatewayFailureSink: withGateway ? GatewayState : null,
                gatewayPosition: position);
    }

    [Theory]
    [InlineData(NodeSourceState.Disabled)]
    [InlineData(NodeSourceState.Unavailable)]
    [InlineData(NodeSourceState.NotReady)]
    [InlineData(NodeSourceState.Stabilizing)]
    public async Task A_stopped_syncing_or_behind_gateway_changes_no_answer_and_is_never_asked(NodeSourceState state)
    {
        var rig = new Rig(state);

        Assert.Equal(await rig.Baseline.GetBalanceAsync("DAbc"), await rig.Sut.GetBalanceAsync("DAbc"));
        Assert.Equal(await rig.Baseline.GetBlockHeightAsync(), await rig.Sut.GetBlockHeightAsync());
        Assert.Equal(await rig.Baseline.GetFeeRateAsync(), await rig.Sut.GetFeeRateAsync());
        Assert.Equal("explorer1-txid", await rig.Sut.BroadcastTransactionAsync(Tx));
        Assert.Equal((await rig.Baseline.GetAddressTransactionsAsync("DAbc")).Count, (await rig.Sut.GetAddressTransactionsAsync("DAbc")).Count);

        Assert.Empty(rig.Gateway.Calls);
        Assert.Equal(0, rig.GatewayState.Failures);
    }

    [Theory]
    [InlineData(GatewayPosition.Backup)]
    [InlineData(GatewayPosition.First)]
    public async Task A_gateway_that_is_not_active_never_makes_a_read_slower_than_it_is_without_one(GatewayPosition position)
    {
        var rig = new Rig(NodeSourceState.Unavailable, position);
        var start = DateTime.UtcNow;

        for (var i = 0; i < 20; i++)
            await rig.Sut.GetBalanceAsync($"DAbc{i}");

        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_read_never_waits_on_a_gateway_health_check_that_never_answers()
    {
        var hanging = new HangingHealth();
        var options = P2pGatewayOptions.FromConfiguration(k => k switch
        {
            "SpvGateway:Enabled" => "true",
            "SpvGateway:ApiUrl" => "https://gateway.example.org",
            _ => null,
        }).ToMonitorOptions();
        var monitor = new NodeSourceMonitor(options, hanging, new FakeExplorerHeightSource(), new FakeClock());
        var rig = new Rig(NodeSourceState.Disabled, GatewayPosition.Backup, stateOverride: monitor);
        // A check is started and never finishes, as a stalled gateway would make it.
        _ = monitor.CheckAsync(CancellationToken.None);
        await Task.Delay(50);

        var start = DateTime.UtcNow;
        var balance = await rig.Sut.GetBalanceAsync("DAbc");
        await rig.Sut.GetBlockHeightAsync();
        await rig.Sut.BroadcastTransactionAsync(Tx);

        Assert.Equal(100, balance);
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(1), "a read waited on the gateway health check");
        Assert.True(hanging.Calls >= 1);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Every_failed_gateway_read_is_reported_to_its_monitor_so_repeated_failures_can_drop_it()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.Explorer1.Fails = true;
        rig.Explorer2.Fails = true;
        rig.Gateway.Fails = true;

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal(3, rig.GatewayState.Failures);
    }

    [Fact]
    public async Task Sends_still_go_out_through_the_explorers_when_the_gateway_refuses_or_declines()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First);
        rig.Gateway.BroadcastFails = true;

        Assert.Equal("explorer1-txid", await rig.Sut.BroadcastTransactionAsync(Tx));
    }
}

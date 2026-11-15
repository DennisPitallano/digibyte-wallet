using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The arrangement of the sources the wallet reads from, as the status pages show it.</summary>
public class ReadSourceArrangementTests
{
    private sealed class Rig
    {
        public FakeNodeApi Node { get; } = new();
        public ScriptedExplorer Explorer1 { get; } = new("explorer1");
        public ScriptedExplorer Explorer2 { get; } = new("explorer2");
        public ScriptedExplorer Gateway { get; } = new("gateway") { Balance = 777, Height = 5000 };
        public FakeNodeState GatewayState { get; } = new();
        public FakeNodeState NodeState { get; } = new();
        public FallbackBlockchainService Sut { get; }

        public Rig(NodeSourceState gateway, NodeSourceState node, GatewayPosition position = GatewayPosition.Backup, bool withGateway = true, bool withNodeMonitor = true)
        {
            GatewayState.State = gateway;
            NodeState.State = node;
            Sut = new FallbackBlockchainService(
                Node.Client(true), [Explorer1, Explorer2], new HttpClient(), isDevelopment: false,
                nodeState: withNodeMonitor ? NodeState : null, failureSink: withNodeMonitor ? NodeState : null,
                gatewaySource: withGateway ? Gateway : null,
                gatewayState: withGateway ? GatewayState : null,
                gatewayFailureSink: withGateway ? GatewayState : null,
                gatewayPosition: position);
        }
    }

    [Fact]
    public void With_node_use_off_the_explorers_come_first_and_the_node_is_the_last_resort()
    {
        var rig = new Rig(NodeSourceState.Disabled, NodeSourceState.Disabled, withGateway: false, withNodeMonitor: false);

        var order = rig.Sut.DescribeReadSources();

        Assert.Equal(["Primary", "Fallback 1", "Fallback 2"], order.Select(e => e.Role));
        Assert.Equal([ReadSourceKind.Explorer, ReadSourceKind.Explorer, ReadSourceKind.OwnNode], order.Select(e => e.Kind));
        Assert.Equal("last resort", order[2].Status);
        Assert.All(order, e => Assert.True(e.InUse));
    }

    [Fact]
    public void An_active_gateway_in_backup_position_is_the_last_fallback_and_a_node_that_is_not_ready_waits_as_standby()
    {
        var rig = new Rig(NodeSourceState.Active, NodeSourceState.NotReady);

        var order = rig.Sut.DescribeReadSources();

        Assert.Equal([ReadSourceKind.Explorer, ReadSourceKind.Explorer, ReadSourceKind.Gateway, ReadSourceKind.OwnNode], order.Select(e => e.Kind));
        Assert.Equal(["Primary", "Fallback 1", "Fallback 2", "Standby"], order.Select(e => e.Role));
        Assert.Equal("active", order[2].Status);
        Assert.Equal("not ready", order[3].Status);
        Assert.False(order[3].InUse);
    }

    [Fact]
    public void An_active_gateway_in_first_position_leads()
    {
        var rig = new Rig(NodeSourceState.Active, NodeSourceState.NotReady, GatewayPosition.First);

        var order = rig.Sut.DescribeReadSources();

        Assert.Equal(ReadSourceKind.Gateway, order[0].Kind);
        Assert.Equal("Primary", order[0].Role);
    }

    [Fact]
    public void A_gateway_that_is_still_stabilizing_is_standby_with_that_status()
    {
        var rig = new Rig(NodeSourceState.Stabilizing, NodeSourceState.Disabled, withNodeMonitor: false);

        var gateway = rig.Sut.DescribeReadSources().Single(e => e.Kind == ReadSourceKind.Gateway);

        Assert.Equal("Standby", gateway.Role);
        Assert.Equal("stabilizing", gateway.Status);
        Assert.Equal(ReadSourceTone.Waiting, gateway.Tone);
        Assert.False(gateway.InUse);
    }

    [Fact]
    public void An_active_node_is_a_fallback_after_the_explorers()
    {
        var rig = new Rig(NodeSourceState.Disabled, NodeSourceState.Active, withGateway: false);

        var order = rig.Sut.DescribeReadSources();

        Assert.Equal(ReadSourceKind.OwnNode, order[2].Kind);
        Assert.Equal("Fallback 2", order[2].Role);
        Assert.Equal("active", order[2].Status);
    }

    [Fact]
    public async Task A_failing_explorer_cools_down_and_the_next_source_is_marked_as_the_last_to_answer()
    {
        var rig = new Rig(NodeSourceState.Disabled, NodeSourceState.Disabled, withGateway: false, withNodeMonitor: false);
        rig.Explorer1.Fails = true;

        await rig.Sut.GetBalanceAsync("DAbc");
        var order = rig.Sut.DescribeReadSources();

        var first = order[0];
        var second = order[1];
        Assert.Equal("cooling down", first.Status);
        Assert.Equal(ReadSourceTone.Bad, first.Tone);
        Assert.NotNull(first.CooldownUntilUtc);
        Assert.Equal(1, first.Failures);
        Assert.False(first.AnsweredLast);
        Assert.True(second.AnsweredLast);
        Assert.Equal(1, second.Successes);
        Assert.NotNull(second.LastSuccessUtc);
    }

    [Fact]
    public async Task Counters_count_each_answer_and_stay_at_zero_for_sources_not_asked()
    {
        var rig = new Rig(NodeSourceState.Active, NodeSourceState.Disabled, withNodeMonitor: false);

        await rig.Sut.GetBalanceAsync("DAbc");
        await rig.Sut.GetBalanceAsync("DAbc2");
        var order = rig.Sut.DescribeReadSources();

        Assert.Equal(2, order[0].Successes);
        Assert.Equal(0, order.Single(e => e.Kind == ReadSourceKind.Gateway).Successes);
    }
}

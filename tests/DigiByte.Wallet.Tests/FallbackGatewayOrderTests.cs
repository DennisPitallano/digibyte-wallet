using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Read and send ordering of <see cref="FallbackBlockchainService"/> with the SPV gateway as a source
/// (specs/011-p2p-spv-gateway, T042 and T043; FR-016, FR-018, FR-019).
/// </summary>
public class FallbackGatewayOrderTests
{
    private static readonly byte[] Tx = [1, 0, 0, 0];

    private sealed class SlowGateway(ScriptedExplorer inner, TimeSpan delay) : IBlockchainService
    {
        public async Task<long> GetBalanceAsync(string address) { await Task.Delay(delay); return await inner.GetBalanceAsync(address); }
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new NotSupportedException();
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new NotSupportedException();
        public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
        public Task<int> GetBlockHeightAsync() => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public FakeNodeApi Node { get; } = new();
        public ScriptedExplorer Explorer1 { get; } = new("explorer1");
        public ScriptedExplorer Explorer2 { get; } = new("explorer2");
        public ScriptedExplorer Gateway { get; } = new("gateway") { Balance = 777, Height = 5000 };
        public FakeNodeState GatewayState { get; } = new();
        // Our own node is switched on but not ready, so it is never asked and only the gateway changes the picture.
        public FakeNodeState NodeState { get; } = new() { State = NodeSourceState.NotReady };
        public FallbackBlockchainService Sut { get; }

        public Rig(NodeSourceState state, GatewayPosition position, IBlockchainService? gatewayOverride = null, bool withGateway = true)
        {
            GatewayState.State = state;
            Sut = new FallbackBlockchainService(
                Node.Client(true), [Explorer1, Explorer2], new HttpClient(), isDevelopment: false,
                nodeState: NodeState, failureSink: NodeState,
                gatewaySource: withGateway ? gatewayOverride ?? Gateway : null,
                gatewayState: withGateway ? GatewayState : null,
                gatewayFailureSink: withGateway ? GatewayState : null,
                gatewayPosition: position);
            Sut.GatewayReadBudget = TimeSpan.FromMilliseconds(400);
        }

        public void AllExplorersFail()
        {
            Explorer1.Fails = true;
            Explorer2.Fails = true;
        }
    }

    private sealed class DecliningGateway : IBlockchainService
    {
        public Task<long> GetBalanceAsync(string address) => throw new GatewayDeclinedException("no record yet");
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new GatewayDeclinedException("no record yet");
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new GatewayDeclinedException("no record yet");
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new GatewayDeclinedException("no record yet");
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new GatewayDeclinedException("no record yet");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new GatewayDeclinedException("no record yet");
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new GatewayDeclinedException("no record yet");
        public Task<decimal> GetFeeRateAsync() => throw new GatewayDeclinedException("no record yet");
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new GatewayDeclinedException("no record yet");
        public Task<int> GetBlockHeightAsync() => throw new GatewayDeclinedException("no record yet");
    }

    [Fact]
    public async Task A_gateway_that_declines_is_skipped_without_counting_as_a_failed_read()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First, gatewayOverride: new DecliningGateway());

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc2"));
        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc3"));

        Assert.Equal(0, rig.GatewayState.Failures);
        Assert.Equal(NodeSourceState.Active, rig.GatewayState.State);
        Assert.NotEqual("spv-gateway", rig.Sut.ActiveBackend);
    }

    // ================= backup position =================

    [Fact]
    public async Task Backup_with_working_explorers_never_asks_the_gateway()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        await rig.Sut.GetBlockHeightAsync();

        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Backup_asks_the_gateway_when_every_explorer_failed_and_it_is_active()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.AllExplorersFail();

        Assert.Equal(777, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal("spv-gateway", rig.Sut.ActiveBackend);
        Assert.Equal(["balance"], rig.Gateway.Calls);
        Assert.False(rig.Sut.AllSourcesFailed);
        Assert.Equal(1, rig.GatewayState.Successes);
    }

    [Theory]
    [InlineData(NodeSourceState.Disabled)]
    [InlineData(NodeSourceState.Unavailable)]
    [InlineData(NodeSourceState.NotReady)]
    [InlineData(NodeSourceState.Stabilizing)]
    public async Task A_gateway_that_is_not_active_is_never_asked_even_when_everything_else_failed(NodeSourceState state)
    {
        var rig = new Rig(state, GatewayPosition.Backup);
        rig.AllExplorersFail();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBlockHeightAsync());

        Assert.Empty(rig.Gateway.Calls);
        Assert.True(rig.Sut.AllSourcesFailed);
    }

    [Fact]
    public async Task Without_a_gateway_configured_reads_behave_as_before()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup, withGateway: false);
        rig.AllExplorersFail();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));
    }

    [Fact]
    public async Task A_gateway_failure_as_a_backup_is_reported_and_then_the_read_fails_as_usual()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.AllExplorersFail();
        rig.Gateway.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal(1, rig.GatewayState.Failures);
        Assert.True(rig.Sut.AllSourcesFailed);
    }

    [Fact]
    public async Task A_null_answer_from_the_gateway_is_not_authoritative()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.AllExplorersFail();
        rig.Gateway.Transaction = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetTransactionAsync("abc"));
    }

    // ================= first position =================

    [Fact]
    public async Task First_with_an_active_gateway_answers_before_the_explorers()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First);

        Assert.Equal(777, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal("spv-gateway", rig.Sut.ActiveBackend);
        Assert.Empty(rig.Explorer1.Calls);
        Assert.Empty(rig.Explorer2.Calls);
    }

    [Fact]
    public async Task First_but_not_active_uses_the_explorers()
    {
        var rig = new Rig(NodeSourceState.Stabilizing, GatewayPosition.First);

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task First_with_a_failing_gateway_falls_back_to_the_explorers_and_reports_the_failure()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First);
        rig.Gateway.Fails = true;

        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));

        Assert.Equal(1, rig.GatewayState.Failures);
        Assert.Equal("explorer-0", rig.Sut.ActiveBackend);
    }

    [Fact]
    public async Task A_slow_gateway_is_given_up_on_after_its_time_budget()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First, new SlowGateway(new ScriptedExplorer("gateway"), TimeSpan.FromSeconds(5)));

        var start = DateTime.UtcNow;
        var balance = await rig.Sut.GetBalanceAsync("DAbc");

        Assert.Equal(100, balance);
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(3));
        Assert.Equal(1, rig.GatewayState.Failures);
    }

    // ================= sending =================

    [Fact]
    public async Task Broadcast_uses_the_explorers_first_when_the_gateway_is_a_backup()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);

        Assert.Equal("explorer1-txid", await rig.Sut.BroadcastTransactionAsync(Tx));

        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Broadcast_falls_back_to_the_gateway_when_every_explorer_refuses()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.Explorer1.BroadcastFails = true;
        rig.Explorer2.BroadcastFails = true;

        Assert.Equal("gateway-txid", await rig.Sut.BroadcastTransactionAsync(Tx));

        Assert.Equal(["broadcast"], rig.Gateway.Calls);
    }

    [Fact]
    public async Task Broadcast_goes_through_the_gateway_first_when_it_is_first_and_active()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First);

        Assert.Equal("gateway-txid", await rig.Sut.BroadcastTransactionAsync(Tx));

        Assert.Empty(rig.Explorer1.Calls);
    }

    [Fact]
    public async Task Broadcast_with_a_failing_gateway_still_succeeds_through_the_explorers()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.First);
        rig.Gateway.BroadcastFails = true;

        Assert.Equal("explorer1-txid", await rig.Sut.BroadcastTransactionAsync(Tx));
    }

    [Fact]
    public async Task Broadcast_never_touches_an_inactive_gateway()
    {
        var rig = new Rig(NodeSourceState.NotReady, GatewayPosition.Backup);
        rig.Explorer1.BroadcastFails = true;
        rig.Explorer2.BroadcastFails = true;

        await Assert.ThrowsAsync<Exception>(() => rig.Sut.BroadcastTransactionAsync(Tx));

        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task Broadcast_failing_everywhere_including_the_gateway_reports_failure()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.Explorer1.BroadcastFails = true;
        rig.Explorer2.BroadcastFails = true;
        rig.Gateway.BroadcastFails = true;

        await Assert.ThrowsAsync<Exception>(() => rig.Sut.BroadcastTransactionAsync(Tx));
    }

    // ================= verified coins =================

    [Fact]
    public async Task Verified_coins_come_from_the_gateway_when_it_is_a_backup_and_the_explorers_cannot_answer()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.AllExplorersFail();
        rig.Gateway.Utxos = [new UtxoInfo { TxId = "ab", OutputIndex = 0, AmountSatoshis = 5, ScriptPubKey = "00", Confirmations = 8 }];

        var utxos = await rig.Sut.GetVerifiedUtxosAsync("DAbc");

        Assert.Single(utxos);
        Assert.Equal(1, rig.GatewayState.Successes);
    }

    [Fact]
    public async Task Verified_coins_with_no_source_able_to_answer_throws_rather_than_returning_empty()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        rig.AllExplorersFail();
        rig.Gateway.Fails = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.GetVerifiedUtxosAsync("DAbc"));
    }

    // ================= cache =================

    [Fact]
    public async Task A_change_in_the_gateway_state_drops_cached_answers()
    {
        var rig = new Rig(NodeSourceState.Active, GatewayPosition.Backup);
        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));
        rig.Explorer1.Balance = 200;
        Assert.Equal(100, await rig.Sut.GetBalanceAsync("DAbc"));

        rig.GatewayState.Set(NodeSourceState.NotReady);

        Assert.Equal(200, await rig.Sut.GetBalanceAsync("DAbc"));
    }
}

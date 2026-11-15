using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The three send outcomes reach the screen: sent, sent but not yet confirmed by peers, and a failure with the reason (T081, T088).</summary>
public class GatewayBroadcastTests
{
    private static readonly byte[] Tx = [1, 0, 0, 0];

    private sealed class ReportingGateway(string outcome, params string[] rejections) : IBlockchainService, IBroadcastReporter
    {
        public BroadcastReport? LastBroadcast { get; private set; }

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
        {
            LastBroadcast = null;
            if (outcome == "rejected")
                throw new InvalidOperationException("The network rejected the transaction: " + string.Join("; ", rejections));
            LastBroadcast = new BroadcastReport(outcome, rejections);
            return Task.FromResult("gateway-txid");
        }

        public Task<long> GetBalanceAsync(string address) => throw new NotSupportedException();
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new NotSupportedException();
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new NotSupportedException();
        public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
        public Task<int> GetBlockHeightAsync() => throw new NotSupportedException();
    }

    private static (FallbackBlockchainService Sut, ScriptedExplorer Explorer1) Rig(IBlockchainService gateway, GatewayPosition position, bool explorersFail)
    {
        var explorer1 = new ScriptedExplorer("explorer1") { BroadcastFails = explorersFail };
        var explorer2 = new ScriptedExplorer("explorer2") { BroadcastFails = explorersFail };
        var node = new FakeNodeApi();
        var nodeState = new FakeNodeState { State = NodeSourceState.NotReady };
        var gatewayState = new FakeNodeState { State = NodeSourceState.Active };
        var sut = new FallbackBlockchainService(node.Client(true), [explorer1, explorer2], new HttpClient(), isDevelopment: false,
            nodeState: nodeState, failureSink: nodeState, gatewaySource: gateway, gatewayState: gatewayState, gatewayFailureSink: gatewayState, gatewayPosition: position);
        return (sut, explorer1);
    }

    [Fact]
    public async Task An_accepted_send_is_sent_and_nothing_extra_is_said()
    {
        var (sut, _) = Rig(new ReportingGateway("accepted"), GatewayPosition.First, explorersFail: false);

        Assert.Equal("gateway-txid", await sut.BroadcastTransactionAsync(Tx));

        Assert.NotNull(sut.LastBroadcast);
        Assert.False(sut.LastBroadcast!.IsUnconfirmedByPeers);
    }

    [Fact]
    public async Task A_send_no_peer_has_confirmed_is_sent_and_flagged_so_the_screen_can_say_so()
    {
        var (sut, _) = Rig(new ReportingGateway("no_response"), GatewayPosition.First, explorersFail: false);

        Assert.Equal("gateway-txid", await sut.BroadcastTransactionAsync(Tx));

        Assert.True(sut.LastBroadcast!.IsUnconfirmedByPeers);
    }

    [Fact]
    public async Task A_send_carried_by_an_explorer_has_no_gateway_report()
    {
        var (sut, _) = Rig(new ReportingGateway("no_response"), GatewayPosition.Backup, explorersFail: false);

        Assert.Equal("explorer1-txid", await sut.BroadcastTransactionAsync(Tx));

        Assert.Null(sut.LastBroadcast);
    }

    [Fact]
    public async Task A_report_from_an_earlier_send_is_never_shown_for_a_later_one()
    {
        var gateway = new ReportingGateway("no_response");
        var (sut, explorer) = Rig(gateway, GatewayPosition.First, explorersFail: false);
        await sut.BroadcastTransactionAsync(Tx);
        Assert.NotNull(sut.LastBroadcast);

        // The gateway now fails, so the explorer carries the next send.
        var failing = Rig(new ReportingGateway("rejected", "66: low fee"), GatewayPosition.First, explorersFail: false);
        Assert.Equal("explorer1-txid", await failing.Sut.BroadcastTransactionAsync(Tx));
        Assert.Null(failing.Sut.LastBroadcast);
    }

    [Fact]
    public async Task A_send_every_source_rejects_fails_and_the_reason_does_not_become_a_success()
    {
        var (sut, _) = Rig(new ReportingGateway("rejected", "66: insufficient fee"), GatewayPosition.First, explorersFail: true);

        await Assert.ThrowsAnyAsync<Exception>(() => sut.BroadcastTransactionAsync(Tx));

        Assert.Null(sut.LastBroadcast);
    }

    [Fact]
    public async Task Sending_the_same_transaction_twice_through_the_gateway_shows_no_error()
    {
        var (sut, _) = Rig(new ReportingGateway("accepted"), GatewayPosition.First, explorersFail: false);

        Assert.Equal("gateway-txid", await sut.BroadcastTransactionAsync(Tx));
        Assert.Equal("gateway-txid", await sut.BroadcastTransactionAsync(Tx));
    }
}

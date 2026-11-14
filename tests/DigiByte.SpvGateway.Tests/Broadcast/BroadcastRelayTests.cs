using DigiByte.SpvGateway.Broadcast;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Broadcast;

/// <summary>A signed transaction is offered to several peers and the result is reported as it is, without claiming more than peers say (T079, R8).</summary>
public class BroadcastRelayTests
{
    private static byte[] SampleTransaction() =>
        RealBlocks.Parse(RealBlocks.All.First(b => RealBlocks.Parse(b.Raw).Transactions.Count > 2).Raw).Transactions[1].ToBytes();

    private static uint256 SampleTxid() => Transaction.Load(SampleTransaction(), GatewayWorld.Network).GetHash();

    private static async Task<(BroadcastRelay Relay, FakePeerNetwork Net)> RigAsync(int peers)
    {
        var net = new FakePeerNetwork();
        for (var i = 0; i < peers; i++)
            net.Add($"{20 + i}.{10 + i}.1.1");
        var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = 10 }, PeerBook.Open(":memory:"), net, net, new FakeTimeProvider());
        await manager.MaintainAsync(default);
        return (new BroadcastRelay(manager, GatewayWorld.Network, seenBackWait: TimeSpan.Zero), net);
    }

    [Fact]
    public async Task The_transaction_goes_to_at_least_four_peers_and_is_accepted_when_none_rejects()
    {
        var (relay, net) = await RigAsync(8);

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal(SampleTxid().ToString(), result.Txid);
        Assert.True(result.RelayedTo >= 4);
        Assert.Equal(result.RelayedTo, net.Peers.Count(p => p.Relayed.Count == 1));
        Assert.Empty(result.Rejections);
        Assert.Equal("accepted", result.Outcome);
    }

    [Fact]
    public async Task With_fewer_than_four_peers_it_reports_how_many_were_reached()
    {
        var (relay, _) = await RigAsync(2);

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal(2, result.RelayedTo);
        Assert.Equal("accepted", result.Outcome);
    }

    [Fact]
    public async Task A_rejection_is_reported_with_its_reason_and_the_outcome_is_rejected()
    {
        var (relay, net) = await RigAsync(5);
        foreach (var peer in net.Peers)
            peer.RejectReason = "66: insufficient fee";

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal("rejected", result.Outcome);
        Assert.Equal(5, result.Rejections.Count);
        Assert.All(result.Rejections, r => Assert.Equal("66: insufficient fee", r));
    }

    [Fact]
    public async Task One_rejection_among_peers_that_accepted_does_not_hide_the_acceptance()
    {
        var (relay, net) = await RigAsync(5);
        net.Peers.First().RejectReason = "64: non-standard";
        net.Peers.Last().Announced.Add(SampleTxid());

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal("accepted", result.Outcome);
        Assert.Single(result.Rejections);
        Assert.Equal(1, result.SeenBackFrom);
    }

    [Fact]
    public async Task When_no_peer_takes_the_transaction_there_is_no_response()
    {
        var (relay, net) = await RigAsync(5);
        foreach (var peer in net.Peers)
            peer.TakesTransactions = false;

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal("no_response", result.Outcome);
        Assert.Equal(0, result.RelayedTo);
    }

    [Fact]
    public async Task With_no_peers_connected_there_is_no_response()
    {
        var (relay, _) = await RigAsync(0);

        var result = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal("no_response", result.Outcome);
        Assert.Equal(0, result.RelayedTo);
    }

    [Fact]
    public async Task Submitting_the_same_transaction_again_gives_the_same_id_and_does_not_send_it_twice()
    {
        var (relay, net) = await RigAsync(5);
        var first = await relay.RelayAsync(SampleTransaction(), default);

        var second = await relay.RelayAsync(SampleTransaction(), default);

        Assert.Equal(first.Txid, second.Txid);
        Assert.Equal(first.Outcome, second.Outcome);
        Assert.All(net.Peers, p => Assert.True(p.Relayed.Count <= 1));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public async Task Bytes_that_are_not_a_transaction_are_refused(byte[] raw)
    {
        var (relay, net) = await RigAsync(5);

        await Assert.ThrowsAsync<FormatException>(() => relay.RelayAsync(raw, default));
        Assert.All(net.Peers, p => Assert.Empty(p.Relayed));
    }

    [Fact]
    public async Task A_transaction_without_inputs_or_outputs_is_refused()
    {
        var (relay, net) = await RigAsync(5);
        var empty = Convert.FromHexString("0100000000000000000000");

        await Assert.ThrowsAsync<FormatException>(() => relay.RelayAsync(empty, default));
        Assert.All(net.Peers, p => Assert.Empty(p.Relayed));
    }
}

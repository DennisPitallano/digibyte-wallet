using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Sync;

namespace DigiByte.SpvGateway.Tests.Health;

public class HealthReporterTests
{
    private const int Cp = 24563100;

    private static HeaderStore NewStore()
    {
        var store = HeaderStore.Open(":memory:", "mainnet");
        store.SeedCheckpoint(Checkpoints.Mainnet);
        return store;
    }

    private static string StatusFor(PeerSnapshot peers, SpvGatewayOptions? options = null)
    {
        using var store = NewStore();
        return HealthReporter.Build(options ?? new SpvGatewayOptions(), store, peers).Status;
    }

    [Fact]
    public void No_peer_has_connected_yet_means_starting()
    {
        Assert.Equal("starting", StatusFor(new PeerSnapshot(0, 0, 0, null, false)));
    }

    [Fact]
    public void Too_few_peers_after_connecting_is_unhealthy()
    {
        Assert.Equal("unhealthy", StatusFor(new PeerSnapshot(3, 2, 0, Cp, true)));
        Assert.Equal("unhealthy", StatusFor(new PeerSnapshot(0, 0, 0, null, true)));
    }

    [Fact]
    public void Being_far_behind_the_best_peer_is_syncing()
    {
        Assert.Equal("syncing", StatusFor(new PeerSnapshot(8, 5, 0, Cp + 1000, true)));
    }

    [Fact]
    public void Enough_peers_and_close_to_the_best_peer_is_ok()
    {
        Assert.Equal("ok", StatusFor(new PeerSnapshot(8, 5, 0, Cp + 3, true)));
        Assert.Equal("ok", StatusFor(new PeerSnapshot(4, 2, 0, Cp, true)));
    }

    [Fact]
    public void The_report_carries_peer_counts_lag_and_mode()
    {
        using var store = NewStore();

        var report = HealthReporter.Build(new SpvGatewayOptions(), store, new PeerSnapshot(8, 5, 3, Cp + 40, true));

        Assert.Equal(8, report.PeerCount);
        Assert.Equal(5, report.FullHistoryPeerCount);
        Assert.Equal(3, report.FilterPeerCount);
        Assert.Equal(40, report.LagBlocks);
        Assert.Equal(Cp, report.HeaderHeight);
        Assert.Equal("blocks", report.MatchingMode);
        Assert.Equal("mainnet", report.Network);
    }

    [Fact]
    public void A_peer_height_below_our_tip_does_not_give_a_negative_lag()
    {
        using var store = NewStore();

        Assert.Equal(0, HealthReporter.Build(new SpvGatewayOptions(), store, new PeerSnapshot(8, 5, 0, Cp - 50, true)).LagBlocks);
    }

    [Fact]
    public void The_minimum_peer_count_follows_the_setting()
    {
        var options = new SpvGatewayOptions { MinHealthyPeers = 2 };

        Assert.Equal("ok", StatusFor(new PeerSnapshot(2, 1, 0, Cp, true), options));
    }
}

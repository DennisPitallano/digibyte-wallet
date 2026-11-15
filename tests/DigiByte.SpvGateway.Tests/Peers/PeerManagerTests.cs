using System.Net;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.SpvGateway.Tests.Peers;

/// <summary>Peer connection policy (spec FR-001, T028) and the peer book (T029).</summary>
public class PeerManagerTests
{
    private const ulong Limited = 0x400; // NODE_NETWORK_LIMITED only

    private static SpvGatewayOptions Options(int target = 8, int min = 4, int perRange = 2) =>
        new() { PeerTarget = target, MinHealthyPeers = min, MaxPeersPerRange = perRange };

    private static (PeerManager Manager, PeerBook Book, FakePeerNetwork Net, FakeTimeProvider Time) Rig(SpvGatewayOptions? options = null)
    {
        var net = new FakePeerNetwork();
        var book = PeerBook.Open(":memory:");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        return (new PeerManager(options ?? Options(), book, net, net, time), book, net, time);
    }

    private static void AddSpread(FakePeerNetwork net, int count, ulong services = PeerRecord.NodeNetwork)
    {
        for (var i = 0; i < count; i++)
            net.Add($"{20 + i}.{10 + i}.1.1", services);
    }

    [Fact]
    public async Task Connects_to_the_target_number_of_peers_and_reports_them()
    {
        var (manager, _, net, _) = Rig();
        AddSpread(net, 12);

        await manager.MaintainAsync(default);

        var snapshot = manager.Snapshot();
        Assert.Equal(8, snapshot.PeerCount);
        Assert.Equal(8, snapshot.FullHistoryPeerCount);
        Assert.True(snapshot.EverConnected);
        Assert.Equal(24563100, snapshot.BestPeerHeight);
    }

    [Fact]
    public async Task Peers_that_advertise_bloom_filters_are_counted_but_never_asked_to_filter()
    {
        var (manager, _, net, _) = Rig();
        const ulong fullHistory = 1, bloom = 1UL << 2;
        net.Add("20.10.1.1", fullHistory | bloom);
        net.Add("21.11.1.1", fullHistory | bloom);
        net.Add("22.12.1.1", fullHistory);

        await manager.MaintainAsync(default);

        var snapshot = manager.Snapshot();
        Assert.Equal(3, snapshot.PeerCount);
        Assert.Equal(2, snapshot.BloomPeerCount);
    }

    [Fact]
    public async Task Keeps_no_more_than_two_peers_from_the_same_address_range()
    {
        var (manager, _, net, _) = Rig();
        for (var i = 1; i <= 6; i++)
            net.Add($"45.67.{i}.1"); // six peers in 45.67.x.x
        net.Add("80.1.1.1");
        net.Add("81.1.1.1");
        net.Add("82.1.1.1");

        await manager.MaintainAsync(default);

        var byRange = manager.Sessions.GroupBy(s => s.Endpoint.Range()).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(2, byRange["45.67"]);
        Assert.Equal(5, manager.Sessions.Count);
        Assert.True(byRange.Count >= 4);
    }

    [Fact]
    public async Task Peers_come_from_at_least_four_distinct_ranges_when_enough_exist()
    {
        var (manager, _, net, _) = Rig();
        AddSpread(net, 8);

        await manager.MaintainAsync(default);

        Assert.True(manager.Sessions.Select(s => s.Endpoint.Range()).Distinct().Count() >= 4);
    }

    [Fact]
    public async Task Peers_known_to_have_full_history_are_tried_before_limited_ones()
    {
        // A peer's services are only known after a handshake, so the book is primed the way an earlier run would have left it.
        var (manager, book, net, time) = Rig(Options(target: 2));
        foreach (var (address, services) in new[] { ("20.1.1.1", Limited), ("21.1.1.1", Limited), ("22.1.1.1", PeerRecord.NodeNetwork), ("23.1.1.1", PeerRecord.NodeNetwork) })
        {
            net.Add(address, services);
            book.RecordGood(new PeerEndpoint(IPAddress.Parse(address), 12024), services, "/x/", 70019, time.GetUtcNow().AddDays(-1));
        }

        await manager.MaintainAsync(default);

        Assert.Equal(2, manager.Sessions.Count);
        Assert.All(manager.Sessions, s => Assert.NotEqual(0UL, s.Services & PeerRecord.NodeNetwork));
    }

    [Fact]
    public async Task Old_blocks_are_asked_of_full_history_peers_only()
    {
        var (manager, _, net, _) = Rig();
        net.Add("20.1.1.1", Limited);
        net.Add("21.1.1.1");

        await manager.MaintainAsync(default);

        Assert.Equal(IPAddress.Parse("21.1.1.1"), manager.Pick(needFullHistory: true)!.Endpoint.Address);
        Assert.NotNull(manager.Pick(needFullHistory: false));

        var (limitedOnly, _, onlyLimited, _) = Rig();
        onlyLimited.Add("20.1.1.1", Limited);
        await limitedOnly.MaintainAsync(default);
        Assert.Null(limitedOnly.Pick(needFullHistory: true));
    }

    [Fact]
    public async Task A_peer_that_sends_an_invalid_header_is_dropped_and_not_tried_again()
    {
        var (manager, book, net, time) = Rig();
        AddSpread(net, 5);
        await manager.MaintainAsync(default);
        var bad = manager.Sessions.First();

        await manager.ReportInvalidHeaderAsync(bad);

        Assert.DoesNotContain(bad, manager.Sessions);
        Assert.NotNull(book.Get(bad.Endpoint)!.BannedUntil);
        net.ConnectAttempts.Clear();
        await manager.MaintainAsync(default);
        Assert.DoesNotContain(bad.Endpoint, net.ConnectAttempts);

        time.Advance(TimeSpan.FromHours(25));
        Assert.True(PeerBook.IsUsable(book.Get(bad.Endpoint)!, time.GetUtcNow()));
    }

    [Fact]
    public async Task A_failing_peer_is_not_retried_until_its_backoff_has_passed_and_the_wait_doubles()
    {
        var (manager, book, net, time) = Rig(Options(target: 1));
        var flaky = net.Add("20.1.1.1", refuses: true);

        await manager.MaintainAsync(default);
        Assert.Equal(1, book.Get(flaky.Endpoint)!.FailureCount);

        net.ConnectAttempts.Clear();
        time.Advance(TimeSpan.FromSeconds(10));
        await manager.MaintainAsync(default);
        Assert.Empty(net.ConnectAttempts);

        time.Advance(TimeSpan.FromSeconds(25));
        await manager.MaintainAsync(default);
        Assert.Single(net.ConnectAttempts);
        Assert.Equal(2, book.Get(flaky.Endpoint)!.FailureCount);

        net.ConnectAttempts.Clear();
        time.Advance(TimeSpan.FromSeconds(45)); // second wait is 60 seconds
        await manager.MaintainAsync(default);
        Assert.Empty(net.ConnectAttempts);
        time.Advance(TimeSpan.FromSeconds(20));
        await manager.MaintainAsync(default);
        Assert.Single(net.ConnectAttempts);
    }

    private sealed class TimingOutConnector(FakePeerNetwork inner, PeerEndpoint slow) : IPeerConnector
    {
        public Task<IPeerSession> ConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken) =>
            endpoint.Equals(slow) ? throw new OperationCanceledException("handshake timed out") : inner.ConnectAsync(endpoint, cancellationToken);
    }

    [Fact]
    public async Task A_peer_whose_connection_times_out_is_a_failed_peer_and_does_not_fail_the_round()
    {
        var net = new FakePeerNetwork();
        for (var i = 0; i < 5; i++)
            net.Add($"{20 + i}.{10 + i}.1.1");
        var book = PeerBook.Open(":memory:");
        var manager = new PeerManager(Options(), book, new TimingOutConnector(net, new PeerEndpoint(IPAddress.Parse("20.10.1.1"), 12024)), net, new FakeTimeProvider());

        await manager.MaintainAsync(default);

        Assert.Equal(4, manager.Snapshot().PeerCount);
        Assert.Equal(1, book.Get(new PeerEndpoint(IPAddress.Parse("20.10.1.1"), 12024))!.FailureCount);
    }

    [Fact]
    public async Task A_cancelled_caller_still_cancels_the_round()
    {
        var net = new FakePeerNetwork();
        net.Add("20.10.1.1");
        var manager = new PeerManager(Options(), PeerBook.Open(":memory:"), new TimingOutConnector(net, new PeerEndpoint(IPAddress.Parse("20.10.1.1"), 12024)), net, new FakeTimeProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.MaintainAsync(cts.Token));
    }

    [Fact]
    public async Task A_good_connection_clears_the_failure_count()
    {
        var (manager, book, net, time) = Rig(Options(target: 1));
        var peer = net.Add("20.1.1.1", refuses: true);
        await manager.MaintainAsync(default);
        peer.Refuses = false;
        time.Advance(TimeSpan.FromMinutes(5));

        await manager.MaintainAsync(default);

        Assert.Equal(0, book.Get(peer.Endpoint)!.FailureCount);
        Assert.NotNull(book.Get(peer.Endpoint)!.LastGood);
    }

    [Fact]
    public async Task Fewer_than_the_healthy_minimum_peers_is_reported_by_the_count()
    {
        var (manager, _, net, _) = Rig();
        AddSpread(net, 3);

        await manager.MaintainAsync(default);

        var snapshot = manager.Snapshot();
        Assert.Equal(3, snapshot.PeerCount);
        Assert.True(snapshot.PeerCount < Options().MinHealthyPeers);
    }

    [Fact]
    public async Task Lost_connections_are_replaced_on_the_next_round()
    {
        var (manager, _, net, _) = Rig();
        AddSpread(net, 10);
        await manager.MaintainAsync(default);
        foreach (var peer in net.Peers.Where(p => p.Connected).Take(3))
            peer.Connected = false;
        Assert.Equal(5, manager.Snapshot().PeerCount);

        await manager.MaintainAsync(default);

        Assert.Equal(8, manager.Snapshot().PeerCount);
    }

    [Fact]
    public async Task With_nothing_reachable_the_snapshot_says_never_connected()
    {
        var (manager, _, net, _) = Rig();
        net.Add("20.1.1.1", refuses: true);

        await manager.MaintainAsync(default);

        Assert.False(manager.Snapshot().EverConnected);
        Assert.Equal(0, manager.Snapshot().PeerCount);
    }

    [Theory]
    [InlineData("45.67.1.2", "45.67")]
    [InlineData("::ffff:45.67.1.2", "45.67")]
    [InlineData("2001:db8:1:2::1", "2001:0db8")]
    public void The_address_range_is_the_slash_16_for_IPv4_and_the_first_32_bits_for_IPv6(string address, string range)
    {
        Assert.Equal(range, new PeerEndpoint(IPAddress.Parse(address), 12024).Range());
    }

    [Fact]
    public void The_book_keeps_one_record_per_address_and_port_and_survives_repeats()
    {
        using var book = PeerBook.Open(":memory:");
        var now = DateTimeOffset.UtcNow;
        var endpoint = new PeerEndpoint(IPAddress.Parse("20.1.1.1"), 12024);

        book.Add(endpoint, now);
        book.Add(endpoint, now);
        book.Add(new PeerEndpoint(endpoint.Address, 12025), now);

        Assert.Equal(2, book.Count());
    }

    [Fact]
    public void Backoff_doubles_from_thirty_seconds_and_stops_at_six_hours()
    {
        Assert.Equal(TimeSpan.Zero, PeerBook.Backoff(0));
        Assert.Equal(TimeSpan.FromSeconds(30), PeerBook.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(60), PeerBook.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(120), PeerBook.Backoff(3));
        Assert.Equal(TimeSpan.FromHours(6), PeerBook.Backoff(40));
    }
}

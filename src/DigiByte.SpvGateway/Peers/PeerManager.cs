using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Health;
using NBitcoin;

namespace DigiByte.SpvGateway.Peers;

/// <summary>A connected peer after its handshake.</summary>
public interface IPeerSession : IAsyncDisposable
{
    PeerEndpoint Endpoint { get; }
    ulong Services { get; }
    string UserAgent { get; }
    int ProtocolVersion { get; }

    /// <summary>The height the peer reported in its version message.</summary>
    int StartHeight { get; }

    bool IsConnected { get; }

    /// <summary>Asks for the headers that follow the first locator hash the peer knows (up to 2000).</summary>
    Task<IReadOnlyList<BlockHeader80>> GetHeadersAsync(IReadOnlyList<uint256> locator, CancellationToken cancellationToken);

    /// <summary>Asks for one whole block (with witness data). Returns null when the peer does not have it.</summary>
    Task<byte[]?> GetBlockAsync(uint256 hash, CancellationToken cancellationToken);

    /// <summary>Offers a transaction to the peer (inv), hands it over when the peer asks (getdata, tx), and waits briefly for a reject.</summary>
    Task<PeerRelayOutcome> RelayTransactionAsync(uint256 txid, byte[] raw, CancellationToken cancellationToken);

    /// <summary>True when this peer has announced the transaction to the gateway, which shows it spread through the network.</summary>
    bool HasAnnounced(uint256 txid);
}

/// <summary>What happened when a transaction was offered to one peer.</summary>
/// <param name="Delivered">The peer asked for the transaction and the gateway sent it.</param>
/// <param name="RejectReason">The peer reject message (code and text), when it sent one.</param>
public sealed record PeerRelayOutcome(bool Delivered, string? RejectReason);

/// <summary>Opens a connection and completes the handshake. Throws when the peer cannot be reached or does not answer properly.</summary>
public interface IPeerConnector
{
    Task<IPeerSession> ConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken);
}

/// <summary>Finds addresses to try: DNS seeds and a built-in list.</summary>
public interface IPeerDiscovery
{
    Task<IReadOnlyList<PeerEndpoint>> DiscoverAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the gateway connected to enough different peers (spec FR-001): up to <see cref="SpvGatewayOptions.PeerTarget"/> connections,
/// at most <see cref="SpvGatewayOptions.MaxPeersPerRange"/> from any one address range, preferring full-history peers and ones that
/// behaved well. A peer that sends an invalid header is dropped and banned. Each call to <see cref="MaintainAsync"/> does one round;
/// the hosted service calls it on a timer.
/// </summary>
public sealed class PeerManager : IPeerStatusSource, IAsyncDisposable
{
    private const int InvalidHeaderPoints = PeerBook.BanThreshold;

    private readonly SpvGatewayOptions _options;
    private readonly PeerBook _book;
    private readonly IPeerConnector _connector;
    private readonly IPeerDiscovery _discovery;
    private readonly TimeProvider _time;
    private readonly List<IPeerSession> _sessions = [];
    private readonly object _gate = new();
    private bool _everConnected;

    public PeerManager(SpvGatewayOptions options, PeerBook book, IPeerConnector connector, IPeerDiscovery discovery, TimeProvider? time = null)
    {
        _options = options;
        _book = book;
        _connector = connector;
        _discovery = discovery;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The peers connected now.</summary>
    public IReadOnlyList<IPeerSession> Sessions
    {
        get { lock (_gate) return _sessions.ToList(); }
    }

    /// <summary>One round: drop lost connections, learn addresses when the book is short, and connect toward the target.</summary>
    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            _sessions.RemoveAll(s => !s.IsConnected);

        var now = _time.GetUtcNow();
        var candidates = _book.Candidates(now);
        if (candidates.Count < _options.PeerTarget)
        {
            try
            {
                foreach (var endpoint in await _discovery.DiscoverAsync(cancellationToken))
                    _book.Add(endpoint, now);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Seeds unreachable: carry on with the peers already in the book.
            }
            candidates = _book.Candidates(now);
        }

        var attempts = new List<Task>();
        foreach (var candidate in PickCandidates(candidates))
            attempts.Add(TryConnectAsync(candidate.Endpoint, cancellationToken));
        await Task.WhenAll(attempts);
    }

    private List<PeerRecord> PickCandidates(IReadOnlyList<PeerRecord> candidates)
    {
        List<IPeerSession> connected;
        lock (_gate)
            connected = _sessions.ToList();

        var open = _options.PeerTarget - connected.Count;
        var perRange = connected.GroupBy(s => s.Endpoint.Range()).ToDictionary(g => g.Key, g => g.Count());
        var chosen = new List<PeerRecord>();
        foreach (var candidate in candidates)
        {
            if (chosen.Count >= open)
                break;
            if (connected.Any(s => s.Endpoint.Equals(candidate.Endpoint)))
                continue;
            var range = candidate.Endpoint.Range();
            perRange.TryGetValue(range, out var inRange);
            if (inRange >= _options.MaxPeersPerRange)
                continue;
            perRange[range] = inRange + 1;
            chosen.Add(candidate);
        }
        return chosen;
    }

    private async Task TryConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _connector.ConnectAsync(endpoint, cancellationToken);
            _book.RecordGood(endpoint, session.Services, session.UserAgent, session.ProtocolVersion, _time.GetUtcNow());
            lock (_gate)
            {
                _sessions.Add(session);
                _everConnected = true;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _book.RecordFailure(endpoint, _time.GetUtcNow());
        }
    }

    /// <summary>The peer sent a header that failed the checks: disconnect it and keep it away for a day.</summary>
    public async Task ReportInvalidHeaderAsync(IPeerSession peer)
    {
        _book.AddMisbehavior(peer.Endpoint, InvalidHeaderPoints, _time.GetUtcNow());
        lock (_gate)
            _sessions.Remove(peer);
        await peer.DisposeAsync();
    }

    /// <summary>The peer sent a block that is not the one asked for, or whose transactions do not match its header: same treatment as an invalid header.</summary>
    public Task ReportInvalidBlockAsync(IPeerSession peer) => ReportInvalidHeaderAsync(peer);

    /// <summary>
    /// A peer to ask. With <paramref name="needFullHistory"/> only full-history peers qualify (old blocks); otherwise full-history peers
    /// still come first. Rotates through the eligible peers so one peer does not carry every request.
    /// </summary>
    public IPeerSession? Pick(bool needFullHistory, int rotation = 0)
    {
        var eligible = Sessions.Where(s => s.IsConnected).OrderByDescending(s => (s.Services & PeerRecord.NodeNetwork) != 0).ThenByDescending(s => s.StartHeight).ToList();
        if (needFullHistory)
            eligible = eligible.Where(s => (s.Services & PeerRecord.NodeNetwork) != 0).ToList();
        return eligible.Count == 0 ? null : eligible[Math.Abs(rotation) % eligible.Count];
    }

    public PeerSnapshot Snapshot()
    {
        List<IPeerSession> live;
        bool ever;
        lock (_gate)
        {
            live = _sessions.Where(s => s.IsConnected).ToList();
            ever = _everConnected;
        }
        return new PeerSnapshot(
            live.Count,
            live.Count(s => (s.Services & PeerRecord.NodeNetwork) != 0),
            live.Count(s => (s.Services & NodeCompactFilters) != 0),
            live.Count == 0 ? null : live.Max(s => s.StartHeight),
            ever);
    }

    private const ulong NodeCompactFilters = 1UL << 6;

    public async ValueTask DisposeAsync()
    {
        List<IPeerSession> all;
        lock (_gate)
        {
            all = _sessions.ToList();
            _sessions.Clear();
        }
        foreach (var session in all)
            await session.DisposeAsync();
    }
}

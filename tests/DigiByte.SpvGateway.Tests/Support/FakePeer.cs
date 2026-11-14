using System.Net;
using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Peers;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Support;

/// <summary>
/// An in-process peer network for tests (T027): scripted peers that answer the handshake and <c>getheaders</c> from a chain the test
/// controls (including a switch to a fork), and peers that refuse the connection. It stands in for the socket layer, which has its own
/// test against a loopback listener.
/// </summary>
internal sealed class FakePeerNetwork : IPeerConnector, IPeerDiscovery
{
    private readonly Dictionary<PeerEndpoint, FakePeer> _peers = [];

    public List<PeerEndpoint> ConnectAttempts { get; } = [];

    public IReadOnlyCollection<FakePeer> Peers => _peers.Values;

    public FakePeer Add(string address, ulong services = PeerRecord.NodeNetwork, int startHeight = 24563100, int port = 12024, bool refuses = false)
    {
        var peer = new FakePeer(new PeerEndpoint(IPAddress.Parse(address), port), services, startHeight) { Refuses = refuses };
        _peers[peer.Endpoint] = peer;
        return peer;
    }

    public Task<IPeerSession> ConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken)
    {
        ConnectAttempts.Add(endpoint);
        if (!_peers.TryGetValue(endpoint, out var peer) || peer.Refuses)
            throw new IOException("connection refused");
        peer.Connected = true;
        return Task.FromResult<IPeerSession>(peer);
    }

    public Task<IReadOnlyList<PeerEndpoint>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PeerEndpoint>>(_peers.Keys.ToList());
}

internal sealed class FakePeer(PeerEndpoint endpoint, ulong services, int startHeight) : IPeerSession
{
    public PeerEndpoint Endpoint { get; } = endpoint;
    public ulong Services { get; } = services;
    public string UserAgent => "/DigiByte:8.26.2/";
    public int ProtocolVersion => 70019;
    public int StartHeight { get; } = startHeight;
    public bool IsConnected => Connected;

    public bool Refuses { get; set; }
    public bool Connected { get; set; }

    /// <summary>The headers this peer serves, in order, each linking to the one before. The first one links to <see cref="Anchor"/>.</summary>
    public List<BlockHeader80> Chain { get; set; } = [];

    public uint256? Anchor { get; set; }

    public Task<IReadOnlyList<BlockHeader80>> GetHeadersAsync(IReadOnlyList<uint256> locator, CancellationToken cancellationToken)
    {
        var start = 0;
        foreach (var hash in locator)
        {
            if (hash == Anchor) { start = 0; break; }
            var index = Chain.FindIndex(h => h.Hash == hash);
            if (index >= 0) { start = index + 1; break; }
        }
        return Task.FromResult<IReadOnlyList<BlockHeader80>>(Chain.Skip(start).Take(2000).ToList());
    }

    /// <summary>Whole blocks this peer can serve, by block hash.</summary>
    public Dictionary<uint256, byte[]> Blocks { get; } = [];

    public List<uint256> BlockRequests { get; } = [];

    /// <summary>Raw transactions offered to this peer.</summary>
    public List<byte[]> Relayed { get; } = [];

    /// <summary>When false the peer never asks for the transaction (no answer).</summary>
    public bool TakesTransactions { get; set; } = true;

    /// <summary>When set the peer sends a reject with this reason after receiving the transaction.</summary>
    public string? RejectReason { get; set; }

    public HashSet<uint256> Announced { get; } = [];

    public Task<PeerRelayOutcome> RelayTransactionAsync(uint256 txid, byte[] raw, CancellationToken cancellationToken)
    {
        Relayed.Add(raw);
        return Task.FromResult(new PeerRelayOutcome(TakesTransactions, TakesTransactions ? RejectReason : null));
    }

    public bool HasAnnounced(uint256 txid) => Announced.Contains(txid);

    public Task<byte[]?> GetBlockAsync(uint256 hash, CancellationToken cancellationToken)
    {
        BlockRequests.Add(hash);
        return Task.FromResult(Blocks.TryGetValue(hash, out var block) ? block : null);
    }

    public ValueTask DisposeAsync()
    {
        Connected = false;
        return ValueTask.CompletedTask;
    }
}

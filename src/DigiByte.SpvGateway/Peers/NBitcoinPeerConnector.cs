using System.Net;
using DigiByte.Crypto.Headers;
using NBitcoin;
using NBitcoin.Protocol;

namespace DigiByte.SpvGateway.Peers;

/// <summary>Opens real connections with NBitcoin's networking classes on the DigiByte network, and finds addresses through the DNS seeds.</summary>
public sealed class NBitcoinPeerConnector(Network network, TimeSpan? connectTimeout = null) : IPeerConnector, IPeerDiscovery
{
    /// <summary>DigiByte Core 8 asks for at least 70017 and speaks 70019.</summary>
    private const uint WireVersion = 70019;
    private static readonly string UserAgent = "/DigiByte.SpvGateway:1.0/";
    private readonly TimeSpan _timeout = connectTimeout ?? TimeSpan.FromSeconds(10);

    public async Task<IPeerSession> ConnectAsync(PeerEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var parameters = new NodeConnectionParameters
        {
            Version = WireVersion,
            UserAgent = UserAgent,
            Services = NodeServices.Nothing,
            IsRelay = false, // no transaction announcements: the gateway only wants headers and what it asks for
            ConnectCancellation = timeout.Token,
        };
        parameters.TemplateBehaviors.Clear();
        Node? node = null;
        try
        {
            node = await Node.ConnectAsync(network, new IPEndPoint(endpoint.Address, endpoint.Port), parameters);
            var connected = node;
            await Task.Run(() => connected.VersionHandshake(timeout.Token), timeout.Token);
            return new NBitcoinPeerSession(endpoint, node);
        }
        catch
        {
            node?.DisconnectAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<PeerEndpoint>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var found = new List<PeerEndpoint>();
        foreach (var seed in network.DNSSeeds)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(seed.Host, cancellationToken);
                found.AddRange(addresses.Select(a => new PeerEndpoint(a, network.DefaultPort)));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One seed down is normal; the others still answer.
            }
        }
        return found.Distinct().ToList();
    }
}

internal sealed class NBitcoinPeerSession : IPeerSession
{
    private readonly Node _node;

    public NBitcoinPeerSession(PeerEndpoint endpoint, Node node)
    {
        Endpoint = endpoint;
        _node = node;
    }

    public PeerEndpoint Endpoint { get; }
    public ulong Services => (ulong)_node.PeerVersion.Services;
    public string UserAgent => _node.PeerVersion.UserAgent;
    public int ProtocolVersion => (int)_node.PeerVersion.Version;
    public int StartHeight => _node.PeerVersion.StartHeight;
    public bool IsConnected => _node.State == NodeState.HandShaked && _node.IsConnected;

    public async Task<IReadOnlyList<BlockHeader80>> GetHeadersAsync(IReadOnlyList<uint256> locator, CancellationToken cancellationToken)
    {
        using var listener = _node.CreateListener().Where(m => m.Message.Payload is HeadersPayload);
        await _node.SendMessageAsync(new GetHeadersPayload(new BlockLocator { Blocks = locator.ToList() }));
        var payload = await Task.Run(() => listener.ReceivePayload<HeadersPayload>(cancellationToken), cancellationToken);
        return payload.Headers.Select(h => BlockHeader80.Parse(h.ToBytes())).ToList();
    }

    public ValueTask DisposeAsync()
    {
        _node.DisconnectAsync();
        _node.Dispose();
        return ValueTask.CompletedTask;
    }
}

using System.Net;
using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Tests.Support;
using NBitcoin;
using NBitcoin.Protocol;

namespace DigiByte.SpvGateway.Tests.Peers;

/// <summary>The real socket layer against a listener on this machine speaking DigiByte's wire format (T030 handshake and getheaders).</summary>
public class NBitcoinPeerConnectorTests
{
    private const int Cp = 24563100;
    private static readonly Network Net = DigiByteNetworkSet.Instance.Mainnet;

    private static NodeServer StartServer(out int port, List<BlockHeader80> headers)
    {
        port = 20000 + Random.Shared.Next(20000);
        var server = new NodeServer(Net, null, 8)
        {
            AllowLocalPeers = true,
            LocalEndpoint = new IPEndPoint(IPAddress.Loopback, port),
        };
        server.InboundNodeConnectionParameters = new NodeConnectionParameters { Services = NodeServices.Network, UserAgent = "/test-peer/" };
        server.MessageReceived += (s, message) =>
        {
            if (message.Message.Payload is GetHeadersPayload)
            {
                var payload = new HeadersPayload(headers.Select(h => ToNBitcoin(h)).ToArray());
                message.Node.SendMessageAsync(payload).GetAwaiter().GetResult();
            }
        };
        server.Listen(8);
        return server;
    }

    private static BlockHeader ToNBitcoin(BlockHeader80 header)
    {
        var parsed = Net.Consensus.ConsensusFactory.CreateBlockHeader();
        parsed.FromBytes(header.ToBytes());
        return parsed;
    }

    [Fact]
    public async Task Handshake_and_getheaders_work_over_a_real_socket()
    {
        var headers = Enumerable.Range(Cp + 1, 5).Select(RealHeaders.At).ToList();
        using var server = StartServer(out var port, headers);
        var connector = new NBitcoinPeerConnector(Net, TimeSpan.FromSeconds(10));

        await using var session = await connector.ConnectAsync(new PeerEndpoint(IPAddress.Loopback, port), default);
        var received = await session.GetHeadersAsync([Checkpoints.Mainnet.Hash], CancellationToken.None);

        Assert.True(session.IsConnected);
        Assert.NotEqual(0UL, session.Services & PeerRecord.NodeNetwork);
        Assert.Equal("/test-peer/", session.UserAgent);
        Assert.Equal(headers.Select(h => h.Hash), received.Select(h => h.Hash));
    }

    [Fact]
    public async Task A_closed_port_fails_fast_with_an_exception()
    {
        var connector = new NBitcoinPeerConnector(Net, TimeSpan.FromSeconds(3));

        await Assert.ThrowsAnyAsync<Exception>(() => connector.ConnectAsync(new PeerEndpoint(IPAddress.Loopback, 1), default));
    }
}

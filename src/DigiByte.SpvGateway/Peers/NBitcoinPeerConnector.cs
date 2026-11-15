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

/// <summary>The legacy <c>reject</c> message (BIP 61), which NBitcoin no longer models: message name, code, reason and, for transactions, the id.</summary>
internal sealed record RejectMessage(uint256 Hash, string Description)
{
    public static RejectMessage? Parse(Payload payload)
    {
        if (payload is not UnknownPayload unknown || unknown.Command != "reject")
            return null;
        try
        {
            var bytes = unknown.Data;
            var position = 0;
            var message = ReadString(bytes, ref position);
            var code = bytes[position++];
            var reason = ReadString(bytes, ref position);
            if (message != "tx" || bytes.Length < position + 32)
                return null;
            return new RejectMessage(new uint256(bytes.AsSpan(position, 32)), $"{code}: {reason}");
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string ReadString(byte[] bytes, ref int position)
    {
        var length = bytes[position++]; // reject strings are short, so the length is a single byte
        var text = System.Text.Encoding.UTF8.GetString(bytes, position, length);
        position += length;
        return text;
    }
}

internal sealed class NBitcoinPeerSession : IPeerSession
{
    private readonly Node _node;

    private readonly object _announcedGate = new();
    private readonly HashSet<uint256> _announced = [];
    private readonly Queue<uint256> _announcedOrder = new();

    public NBitcoinPeerSession(PeerEndpoint endpoint, Node node)
    {
        Endpoint = endpoint;
        _node = node;
        // Remember which transactions this peer announces, so a relayed transaction can be seen coming back from other peers.
        _node.MessageReceived += (_, message) =>
        {
            if (message.Message.Payload is not InvPayload inv)
                return;
            lock (_announcedGate)
                foreach (var item in inv.Where(i => (i.Type & InventoryType.MSG_TX) != 0))
                    if (_announced.Add(item.Hash))
                    {
                        _announcedOrder.Enqueue(item.Hash);
                        if (_announcedOrder.Count > 5000)
                            _announced.Remove(_announcedOrder.Dequeue());
                    }
        };
    }

    public bool HasAnnounced(uint256 txid)
    {
        lock (_announcedGate)
            return _announced.Contains(txid);
    }

    public async Task<PeerRelayOutcome> RelayTransactionAsync(uint256 txid, byte[] raw, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var listener = _node.CreateListener().Where(m =>
            (m.Message.Payload is GetDataPayload request && request.Inventory.Any(i => i.Hash == txid))
            || RejectMessage.Parse(m.Message.Payload) is { } reject && reject.Hash == txid);
        try
        {
            await _node.SendMessageAsync(new InvPayload(new InventoryVector(InventoryType.MSG_TX, txid)));
            var transaction = Transaction.Load(raw, DigiByteNetworkFor(_node));
            var first = await Task.Run(() => listener.ReceiveMessage(timeout.Token), timeout.Token);
            if (RejectMessage.Parse(first.Message.Payload) is { } early)
                return new PeerRelayOutcome(false, early.Description);
            await _node.SendMessageAsync(new TxPayload(transaction));
            // A peer that refuses the transaction says so right after receiving it.
            using var rejectWindow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            rejectWindow.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var second = await Task.Run(() => listener.ReceiveMessage(rejectWindow.Token), rejectWindow.Token);
                if (RejectMessage.Parse(second.Message.Payload) is { } late)
                    return new PeerRelayOutcome(true, late.Description);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // No reject within the window: the usual sign of acceptance.
            }
            return new PeerRelayOutcome(true, null);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            return new PeerRelayOutcome(false, null);
        }
    }

    private static Network DigiByteNetworkFor(Node node) => node.Network;


    public PeerEndpoint Endpoint { get; }
    public ulong Services => (ulong)_node.PeerVersion.Services;
    public string UserAgent => _node.PeerVersion.UserAgent;
    public int ProtocolVersion => (int)_node.PeerVersion.Version;
    public int StartHeight => _node.PeerVersion.StartHeight;
    public bool IsConnected => _node.State == NodeState.HandShaked && _node.IsConnected;

    /// <summary>
    /// Waits for the first message that <paramref name="pick"/> turns into an answer, without holding a thread: the wait is a task completed
    /// from the message event, so many requests can be outstanding on one peer (blocking waits would exhaust the thread pool).
    /// </summary>
    private async Task<T> AskAsync<T>(Payload request, Func<Payload, (bool Match, T Value)> pick, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        NodeEventMessageIncoming handler = (_, message) =>
        {
            var (match, value) = pick(message.Message.Payload);
            if (match)
                answer.TrySetResult(value);
        };
        _node.MessageReceived += handler;
        try
        {
            await _node.SendMessageAsync(request);
            using var registration = cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
            return await answer.Task;
        }
        finally
        {
            _node.MessageReceived -= handler;
        }
    }

    public Task<IReadOnlyList<BlockHeader80>> GetHeadersAsync(IReadOnlyList<uint256> locator, CancellationToken cancellationToken) =>
        AskAsync<IReadOnlyList<BlockHeader80>>(
            new GetHeadersPayload(new BlockLocator { Blocks = locator.ToList() }),
            payload => payload is HeadersPayload headers
                ? (true, headers.Headers.Select(h => BlockHeader80.Parse(h.ToBytes())).ToList())
                : (false, []),
            cancellationToken);

    public Task<byte[]?> GetBlockAsync(uint256 hash, CancellationToken cancellationToken) =>
        // Each call listens only for the block (or not-found) that names its own hash, so replies cannot be mixed up and several blocks can
        // be asked of one peer at once.
        AskAsync<byte[]?>(
            new GetDataPayload(new InventoryVector(InventoryType.MSG_WITNESS_BLOCK, hash)),
            payload => payload switch
            {
                BlockPayload block when block.Object.GetHash() == hash => (true, block.Object.ToBytes()),
                NotFoundPayload missing when missing.Inventory.Any(i => i.Hash == hash) => (true, null),
                _ => (false, null),
            },
            cancellationToken);

    public ValueTask DisposeAsync()
    {
        _node.DisconnectAsync();
        _node.Dispose();
        return ValueTask.CompletedTask;
    }
}

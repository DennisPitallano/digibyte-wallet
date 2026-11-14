using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Networks;
using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Support;

/// <summary>
/// A gateway fed with the real blocks of the fixture: their headers are stored, a fake full-history peer serves the blocks, and the
/// match index is filled for the heights asked for.
/// </summary>
internal sealed class GatewayWorld
{
    public const int FirstHeight = 24563101;

    public static readonly Network Network = DigiByteNetworkSet.Instance.Mainnet;

    public FakePeerNetwork Peers { get; } = new();
    public PeerManager Manager { get; private set; } = null!;

    /// <summary>Block heights present in the fixture, in order.</summary>
    public static IReadOnlyList<int> Heights => RealBlocks.All.Select(b => b.Height).OrderBy(h => h).ToList();

    public static (int Height, Block Block) At(int height) => (height, RealBlocks.Parse(RealBlocks.All.First(b => b.Height == height).Raw));

    /// <summary>Registers this world with a gateway test host: the peers are fake and the block fetcher uses them.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(Manager);
        services.AddSingleton<IPeerStatusSource>(Manager);
        services.AddSingleton(sp => new BlockFetcher(Manager, sp.GetRequiredService<HeaderStore>(), Network));
        // The fixture holds blocks from FirstHeight on, so the index must not start earlier.
        services.AddSingleton(sp => new BlockIndexer(sp.GetRequiredService<SpvGatewayOptions>(), sp.GetRequiredService<HeaderStore>(), sp.GetRequiredService<BlockIndexStore>(), sp.GetRequiredService<BlockFetcher>(), null, FirstHeight));
    }

    public static async Task<GatewayWorld> CreateAsync(bool serveBlocks = true)
    {
        var world = new GatewayWorld();
        var peer = world.Peers.Add("20.10.1.1");
        if (serveBlocks)
            foreach (var block in RealBlocks.All)
                peer.Blocks[block.Hash] = block.Raw;
        var book = PeerBook.Open(":memory:");
        world.Manager = new PeerManager(new SpvGatewayOptions { PeerTarget = 1 }, book, world.Peers, world.Peers, new FakeTimeProvider());
        await world.Manager.MaintainAsync(default);
        return world;
    }

    /// <summary>A running gateway host on this world: headers for every fixture block are stored and the blocks up to <paramref name="indexUpTo"/> are indexed.</summary>
    public static async Task<GatewayFactory> HostAsync(GatewayWorld world, int indexFrom, int indexUpTo, Dictionary<string, string?>? settings = null)
    {
        var factory = new GatewayFactory(settings, world.Register);
        factory.Sync.Ingest(Heights.Select(RealHeaders.At));
        var fetcher = factory.Services.GetRequiredService<BlockFetcher>();
        var index = factory.Services.GetRequiredService<BlockIndexStore>();
        await IndexAsync(index, fetcher, indexFrom, indexUpTo);
        return factory;
    }

    public static async Task IndexAsync(BlockIndexStore index, BlockFetcher fetcher, int from, int to)
    {
        for (var height = from; height <= to; height++)
        {
            var block = await fetcher.FetchAsync(height, default);
            Assert.NotNull(block);
            index.Add(BlockIndexBuilder.Build(block!, height), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>The address a standard output pays, or null for outputs without one (such as data outputs).</summary>
    public static string? AddressOf(TxOut output) => output.ScriptPubKey.GetDestinationAddress(Network)?.ToString();
}

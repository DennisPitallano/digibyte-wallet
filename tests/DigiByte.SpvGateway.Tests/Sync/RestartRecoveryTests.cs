using DigiByte.Crypto.Headers;
using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Index;
using DigiByte.SpvGateway.Peers;
using DigiByte.SpvGateway.Sync;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.SpvGateway.Tests.Sync;

/// <summary>After a restart the stored headers, block index and peer book are used as they are, and health goes from starting to ok (T108).</summary>
public class RestartRecoveryTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spvgw-restart-" + Guid.NewGuid().ToString("N"));

    public RestartRecoveryTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private string FileIn(string name) => System.IO.Path.Combine(_folder, name);

    [Fact]
    public void Headers_the_index_and_the_peer_book_are_all_there_after_a_restart_and_nothing_is_rebuilt()
    {
        var heights = GatewayWorld.Heights;
        using (var headers = HeaderStore.Open(FileIn("headers.db"), "mainnet"))
        using (var index = BlockIndexStore.Open(FileIn("index.db")))
        using (var book = PeerBook.Open(FileIn("peers.db")))
        {
            new HeaderSync(headers, Checkpoints.Mainnet, hasher: null).Ingest(heights.Select(RealHeaders.At));
            foreach (var height in heights.Take(10))
                index.Add(BlockIndexBuilder.Build(GatewayWorld.At(height).Block, height), DateTimeOffset.UtcNow);
            book.RecordGood(new PeerEndpoint(System.Net.IPAddress.Parse("20.10.1.1"), 12024), 1, "/x/", 70019, DateTimeOffset.UtcNow);
        }
        SqliteConnection.ClearAllPools();

        using var headers2 = HeaderStore.Open(FileIn("headers.db"), "mainnet");
        using var index2 = BlockIndexStore.Open(FileIn("index.db"));
        using var book2 = PeerBook.Open(FileIn("peers.db"));
        var sync = new HeaderSync(headers2, Checkpoints.Mainnet, hasher: null);

        Assert.Equal(heights.Last(), headers2.BestTip()!.Height);
        Assert.Equal(heights.Last(), sync.TipHeight);
        Assert.Equal(heights.First(), index2.IndexedFromHeight());
        Assert.Equal(heights[9], index2.IndexedToHeight());
        Assert.Single(book2.All());
        Assert.NotNull(book2.All()[0].LastGood);
    }

    [Fact]
    public async Task Indexing_resumes_at_the_next_missing_block_instead_of_starting_over()
    {
        var world = await GatewayWorld.CreateAsync();
        var heights = GatewayWorld.Heights;
        using (var first = new GatewayFactory(null, world.Register, _folder))
        {
            first.Sync.Ingest(heights.Select(RealHeaders.At));
            var fetcher = (BlockFetcher)first.Services.GetService(typeof(BlockFetcher))!;
            var index = (BlockIndexStore)first.Services.GetService(typeof(BlockIndexStore))!;
            await GatewayWorld.IndexAsync(index, fetcher, heights.First(), heights[14]);
        }
        SqliteConnection.ClearAllPools();

        using var second = new GatewayFactory(null, world.Register, _folder);
        var indexer = (BlockIndexer)second.Services.GetService(typeof(BlockIndexer))!;
        var store = (BlockIndexStore)second.Services.GetService(typeof(BlockIndexStore))!;
        Assert.Equal(heights[14], store.IndexedToHeight());

        var added = await indexer.IndexAsync(default);

        Assert.Equal(heights.Count - 15, added);
        Assert.Equal(heights.Last(), store.IndexedToHeight());
        Assert.Equal(heights.First(), store.IndexedFromHeight());
    }

    [Fact]
    public async Task Health_moves_from_starting_to_ok_once_peers_connect_after_the_restart()
    {
        using var headers = HeaderStore.Open(FileIn("headers.db"), "mainnet");
        new HeaderSync(headers, Checkpoints.Mainnet, hasher: null).Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        var net = new FakePeerNetwork();
        for (var i = 0; i < 5; i++)
            net.Add($"{20 + i}.{10 + i}.1.1", startHeight: GatewayWorld.Heights.Last());
        var manager = new PeerManager(new SpvGatewayOptions { PeerTarget = 5 }, PeerBook.Open(FileIn("peers.db")), net, net, new FakeTimeProvider());
        var options = new SpvGatewayOptions { PeerTarget = 5 };

        Assert.Equal("starting", HealthReporter.Build(options, headers, manager.Snapshot()).Status);
        await manager.MaintainAsync(default);

        Assert.Equal("ok", HealthReporter.Build(options, headers, manager.Snapshot()).Status);
    }
}

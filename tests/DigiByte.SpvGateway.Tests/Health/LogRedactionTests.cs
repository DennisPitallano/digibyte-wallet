using System.Net.Http.Json;
using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Health;

/// <summary>Nothing about a request ends up in a log, a response header, the health report or any file the gateway wrote (T104, FR-021, SC-007).</summary>
public class LogRedactionTests
{
    private sealed class Capture : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
                Lines.Add(formatter(state, exception) + " " + exception);
        }
        public void Dispose() { }
    }

    [Fact]
    public void A_redacted_name_is_a_short_stable_prefix_and_never_the_original()
    {
        var address = "dgb1qexampleexampleexampleexampleexample";

        var shortened = LogRedaction.Short(address);

        Assert.Equal(8, shortened.Length);
        Assert.Equal(shortened, LogRedaction.Short(address));
        Assert.DoesNotContain(address[..10], shortened);
        Assert.NotEqual(shortened, LogRedaction.Short(address + "x"));
        Assert.Equal("none", LogRedaction.Short(null));
        Assert.Equal("none", LogRedaction.Short(""));
    }

    [Fact]
    public async Task Scans_and_broadcasts_leave_no_address_transaction_or_id_in_logs_headers_health_or_files()
    {
        var capture = new Capture();
        var world = await GatewayWorld.CreateAsync();
        for (var i = 1; i < 5; i++)
            world.Peers.Add($"{30 + i}.{20 + i}.1.1");
        await world.Manager.MaintainAsync(default);

        using var factory = new GatewayFactory(null, services =>
        {
            world.Register(services);
            services.AddLogging(b => b.AddProvider(capture));
        });
        factory.Sync.Ingest(GatewayWorld.Heights.Select(RealHeaders.At));
        await GatewayWorld.IndexAsync(factory.Services.GetRequiredService<DigiByte.SpvGateway.Index.BlockIndexStore>(),
            factory.Services.GetRequiredService<DigiByte.SpvGateway.Sync.BlockFetcher>(), GatewayWorld.Heights.First(), GatewayWorld.Heights.Last());

        var (height, block) = GatewayWorld.Heights.Select(GatewayWorld.At).First(b => b.Block.Transactions.Count > 2);
        var tx = block.Transactions[1];
        var address = tx.Outputs.Select(GatewayWorld.AddressOf).First(a => a is not null)!;
        using var client = factory.CreateClient();

        var everything = new List<string>();
        var scan = await client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { address }, fromHeight = GatewayWorld.Heights.First(), toHeight = GatewayWorld.Heights.Last() });
        everything.AddRange(scan.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)));
        var broadcast = await client.PostAsJsonAsync("/v1/broadcast", new { raw = tx.ToHex() });
        everything.AddRange(broadcast.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)));
        await client.PostAsJsonAsync("/v1/proofs", new { items = new[] { new { txid = tx.GetHash().ToString(), height } } });
        await client.GetAsync($"/v1/tx/{tx.GetHash()}");
        await client.PostAsJsonAsync("/v1/scan", new { addresses = new[] { address }, fromHeight = 5, toHeight = 1 }); // an error path
        everything.Add(await client.GetStringAsync("/v1/health"));
        string logs;
        lock (capture.Lines)
            logs = string.Join("\n", capture.Lines);
        everything.Add(logs);

        var forbidden = new[] { address, tx.GetHash().ToString(), tx.ToHex() };
        foreach (var text in everything)
            foreach (var secret in forbidden)
                Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);

        // And nothing the gateway wrote about the request is on disk. The header and index files hold public chain data by design (short keys only).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var secrets = forbidden.Select(f => System.Text.Encoding.UTF8.GetBytes(f)).Append(tx.ToBytes()).Append(tx.GetHash().ToBytes()).ToList();
        foreach (var file in Directory.EnumerateFiles(factory.DataDirectory))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("headers-") || name.StartsWith("index-"))
                continue;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            foreach (var secret in secrets)
                Assert.False(bytes.AsSpan().IndexOf(secret) >= 0, $"{name} holds request data");
        }
    }
}

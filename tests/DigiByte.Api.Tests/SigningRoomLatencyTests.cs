using System.Diagnostics;
using System.Threading.Channels;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xunit.Abstractions;

namespace DigiByte.Api.Tests;

/// <summary>
/// Real SignalR round trips against the signing-room hub (specs/017-multisig-online-signing, T053): signatures reach the
/// other participants within 5 s (SC-002), a dropped participant is back with the current state within 60 s (SC-006),
/// and nothing identifying reaches the logs (FR-028).
/// </summary>
public class SigningRoomLatencyTests : IClassFixture<SigningRoomLatencyTests.Factory>
{
    private static readonly Network Net = DigiByteNetwork.Mainnet;
    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public SigningRoomLatencyTests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public List<string> Logs { get; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(s => s.AddLogging(l =>
            {
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(new CaptureLogs(Logs));
            }));
        }
    }

    private sealed class Client : IAsyncDisposable
    {
        public required HubConnection Hub { get; init; }
        public required Key Key { get; init; }
        public Channel<SigningRoomView> States { get; } = Channel.CreateUnbounded<SigningRoomView>();
        public Channel<(string Key, string Psbt)> Signatures { get; } = Channel.CreateUnbounded<(string, string)>();
        public Channel<(string Code, string Message)> Errors { get; } = Channel.CreateUnbounded<(string, string)>();
        public string Pub => Key.PubKey.ToHex();

        public async Task<string> Prove(string purpose, string target)
        {
            var nonce = await Hub.InvokeAsync<string>("RequestChallenge");
            return CoSignerProof.Sign(Key, CoSignerProof.SigningRoomDomain, purpose, target, nonce, Hub.ConnectionId!);
        }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    private async Task<Client> Connect(Key key)
    {
        var server = _factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/multisig-signing"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var client = new Client { Hub = hub, Key = key };
        hub.On<SigningRoomView>("RoomState", r => client.States.Writer.TryWrite(r));
        hub.On<string, string>("SignatureAdded", (k, p) => client.Signatures.Writer.TryWrite((k, p)));
        hub.On<string, string>("Error", (c, m) => client.Errors.Writer.TryWrite((c, m)));
        await hub.StartAsync();
        return client;
    }

    private static async Task<T> Next<T>(Channel<T> channel, int seconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        return await channel.Reader.ReadAsync(cts.Token);
    }

    private static void Drain<T>(Channel<T> channel)
    {
        while (channel.Reader.TryRead(out _)) { }
    }

    [Fact]
    public async Task Signatures_fan_out_within_five_seconds_and_a_dropped_participant_rejoins()
    {
        var keys = new[] { new Key(), new Key(), new Key() };
        var service = new MultisigService(Net);
        var redeem = service.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = service.GetP2SH_P2WSH_Address(redeem);
        var coin = new Coin(RandomUtils.GetUInt256(), 0, Money.Coins(10), address.ScriptPubKey);
        var psbt = service.CreateMultisigPSBTWithFee(redeem, [coin],
            new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net), Money.Coins(1), address, 10_000, 100_000);
        var txId = psbt.GetGlobalTransaction().GetHash().ToString();
        var hexes = keys.Select(k => k.PubKey.ToHex()).ToList();

        await using var alice = await Connect(keys[0]);
        await using var bob = await Connect(keys[1]);
        var carol = await Connect(keys[2]);

        await alice.Hub.InvokeAsync("OpenRoom", new OpenSigningRoomRequest
        {
            Network = "main", CoSignerKeys = hexes, RequiredSignatures = 2, PsbtBase64 = psbt.ToBase64(),
            DisplayName = "Alice", PublicKeyHex = alice.Pub, Proof = await alice.Prove("open", txId),
        });
        var opened = await Next(alice.States);
        var code = opened.InviteCode;

        foreach (var (client, name) in new[] { (bob, "Bob"), (carol, "Carol") })
        {
            await client.Hub.InvokeAsync("JoinRoom", new JoinSigningRoomRequest
            {
                InviteCode = code, DisplayName = name, PublicKeyHex = client.Pub, Proof = await client.Prove("join", code),
            });
            Assert.True((await Next(client.States)).IncludesPayload);
        }

        var signed = psbt.Clone();
        signed.SignWithKeys(keys[0]);
        var copy = signed.ToBase64();

        // SC-002: 20 runs, p95 below 5 s for the other two participants.
        var samples = new List<double>();
        for (var run = 0; run < 20; run++)
        {
            Drain(bob.Signatures);
            Drain(carol.Signatures);
            var watch = Stopwatch.StartNew();
            await alice.Hub.InvokeAsync("SubmitSignature", opened.RoomId, copy);
            await Next(bob.Signatures);
            await Next(carol.Signatures);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        _output.WriteLine($"SC-002 fan-out over {samples.Count} runs: p50 {samples[samples.Count / 2]:F0} ms, p95 {p95:F0} ms");
        Assert.True(p95 < 5000, $"p95 {p95} ms");
        Assert.True(alice.Errors.Reader.Count == 0);

        // SC-006: Carol's connection drops; a new connection rejoins with a fresh proof and gets the current state.
        await carol.DisposeAsync();
        await using var carolAgain = await Connect(keys[2]);
        var rejoinWatch = Stopwatch.StartNew();
        await carolAgain.Hub.InvokeAsync("Rejoin", new RejoinSigningRoomRequest
        {
            RoomId = opened.RoomId, PublicKeyHex = carolAgain.Pub, Proof = await carolAgain.Prove("rejoin", opened.RoomId),
        });
        var state = await Next(carolAgain.States, 60);
        _output.WriteLine($"SC-006 rejoin: {rejoinWatch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(state.IncludesPayload);
        Assert.Contains(hexes[0].ToLowerInvariant(), state.SignedKeys);
        Assert.Contains(state.Participants, p => p.PublicKeyHex == hexes[2].ToLowerInvariant() && p.Online);

        // FR-028: nothing identifying reaches the logs.
        string[] secrets = [code, opened.RoomId, txId, .. hexes, "Alice", "Bob", "Carol", copy[..40]];
        lock (_factory.Logs)
            Assert.DoesNotContain(_factory.Logs, line => secrets.Any(s => line.Contains(s, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task An_outsider_gets_the_invalid_code_answer_and_is_rate_limited_after_five_failures()
    {
        await using var mallory = await Connect(new Key());
        for (var i = 0; i < 5; i++)
        {
            await mallory.Hub.InvokeAsync("JoinRoom", new JoinSigningRoomRequest
            {
                InviteCode = "ZZZZZZ", DisplayName = "M", PublicKeyHex = mallory.Pub, Proof = await mallory.Prove("join", "ZZZZZZ"),
            });
            Assert.Equal(SigningRoomErrors.InvalidCode, (await Next(mallory.Errors)).Code);
        }
        await mallory.Hub.InvokeAsync("JoinRoom", new JoinSigningRoomRequest
        {
            InviteCode = "ZZZZZZ", DisplayName = "M", PublicKeyHex = mallory.Pub, Proof = await mallory.Prove("join", "ZZZZZZ"),
        });
        var limited = await Next(mallory.Errors);
        Assert.Equal(SigningRoomErrors.RateLimited, limited.Code);
        Assert.Equal("Too many attempts, try again shortly.", limited.Message);
    }

    private sealed class CaptureLogs(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(sink);
        public void Dispose() { }

        private sealed class Logger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink) sink.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
            }
        }
    }
}

using System.Threading.Channels;
using DigiByte.Api.Hubs;
using DigiByte.Crypto.Multisig;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace DigiByte.Api.Tests;

/// <summary>
/// Real SignalR round trip against the API host: the client signs with <c>HubConnection.ConnectionId</c> exactly as
/// MultisigRoomPage does, so this proves the client and server agree on the connection id the proof is bound to.
/// </summary>
public class MultisigRoomHubEndToEndTests : IClassFixture<MultisigRoomHubEndToEndTests.Factory>
{
    private readonly Factory _factory;

    public MultisigRoomHubEndToEndTests(Factory factory) => _factory = factory;

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
        public Channel<MultisigRoom> Rooms { get; } = Channel.CreateUnbounded<MultisigRoom>();
        public Channel<string> Errors { get; } = Channel.CreateUnbounded<string>();
        public Key Key { get; } = new();
        public string Pub => Key.PubKey.ToHex();

        public async Task<string> Prove(string purpose, string target)
        {
            var nonce = await Hub.InvokeAsync<string>("RequestChallenge");
            return CoSignerProof.Sign(Key, CoSignerProof.CreationRoomDomain, purpose, target, nonce, Hub.ConnectionId!);
        }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    private async Task<Client> Connect()
    {
        var server = _factory.Server;
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/multisig-room"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var client = new Client { Hub = hub };
        hub.On<MultisigRoom>("RoomUpdated", r => client.Rooms.Writer.TryWrite(r));
        hub.On<string>("Error", e => client.Errors.Writer.TryWrite(e));
        await hub.StartAsync();
        return client;
    }

    private static async Task<T> Next<T>(Channel<T> channel)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await channel.Reader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task Create_and_join_work_end_to_end_and_bad_proofs_are_refused()
    {
        await using var alice = await Connect();
        await alice.Hub.InvokeAsync("CreateRoom", "Family vault", 2, 3, "Alice", alice.Pub, await alice.Prove("create", "Family vault"));
        var created = await Next(alice.Rooms);
        Assert.Single(created.Participants);

        // An impostor claiming Alice-like access with someone else's key is told the code is invalid.
        await using var mallory = await Connect();
        var victim = new Key().PubKey.ToHex();
        await mallory.Hub.InvokeAsync("JoinRoom", created.InviteCode, "Mallory", victim, await mallory.Prove("join", created.InviteCode));
        Assert.Equal(MultisigRoomHub.InvalidInviteMessage, await Next(mallory.Errors));

        await using var bob = await Connect();
        await bob.Hub.InvokeAsync("JoinRoom", created.InviteCode.ToLowerInvariant(), "Bob", bob.Pub, await bob.Prove("join", created.InviteCode));
        var joined = await Next(bob.Rooms);
        Assert.Equal(2, joined.Participants.Count);
        Assert.Contains(joined.Participants, p => p.PublicKeyHex == bob.Pub);
        Assert.False(bob.Errors.Reader.TryRead(out _));

        // Alice (group member) sees the update too.
        var aliceView = await Next(alice.Rooms);
        Assert.Equal(2, aliceView.Participants.Count);

        // Nothing identifying reaches the logs.
        string[] secrets = [created.InviteCode, alice.Pub, bob.Pub, victim, "Family vault", "Mallory"];
        lock (_factory.Logs)
            Assert.DoesNotContain(_factory.Logs, line => secrets.Any(s => line.Contains(s, StringComparison.OrdinalIgnoreCase)));
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

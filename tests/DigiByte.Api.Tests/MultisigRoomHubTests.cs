using System.Security.Claims;
using DigiByte.Api.Hubs;
using DigiByte.Crypto.Multisig;
using DigiByte.P2P.Shared.Contracts;
using DigiByte.P2P.Shared.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Time.Testing;
using NBitcoin;

namespace DigiByte.Api.Tests;

/// <summary>
/// Drives <see cref="MultisigRoomHub"/> directly with fake SignalR plumbing: one hub instance per simulated connection,
/// all sharing one <see cref="CreationRoomGate"/> (as the DI singleton does).
/// </summary>
public class MultisigRoomHubTests
{
    private readonly CreationRoomGate _gate = new(new FakeTimeProvider(DateTimeOffset.UtcNow));

    private sealed class Conn
    {
        public required MultisigRoomHub Hub { get; init; }
        public required RecordingClient Caller { get; init; }
        public required string Id { get; init; }
        public Key Key { get; } = new();
        public string Pub => Key.PubKey.ToHex();
    }

    private Conn Connect()
    {
        var id = "conn-" + Guid.NewGuid().ToString("N");
        var caller = new RecordingClient();
        var hub = new MultisigRoomHub(_gate)
        {
            Context = new FakeContext(id),
            Clients = new FakeClients(caller),
            Groups = new FakeGroups(),
        };
        return new Conn { Hub = hub, Caller = caller, Id = id };
    }

    private static async Task<string> Prove(Conn c, string purpose, string target, Key? signer = null)
    {
        var nonce = await c.Hub.RequestChallenge();
        return CoSignerProof.Sign(signer ?? c.Key, CoSignerProof.CreationRoomDomain, purpose, target, nonce, c.Id);
    }

    private async Task<(Conn Creator, MultisigRoom Room)> CreatedRoom(int total = 2)
    {
        var creator = Connect();
        await creator.Hub.CreateRoom("Vault", 2, total, "Alice", creator.Pub, await Prove(creator, "create", "Vault"));
        var room = Assert.Single(creator.Caller.Rooms);
        return (creator, room);
    }

    [Fact]
    public async Task Create_with_valid_proof_creates_the_room()
    {
        var (creator, room) = await CreatedRoom();
        Assert.Empty(creator.Caller.Errors);
        Assert.Equal(6, room.InviteCode.Length);
        var p = Assert.Single(room.Participants);
        Assert.Equal(creator.Pub, p.PublicKeyHex);
        Assert.True(p.IsInitiator);
        Assert.Equal("", p.ConnectionId);
    }

    [Fact]
    public async Task Create_without_a_challenge_or_with_a_bad_proof_is_refused()
    {
        var c = Connect();
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, "");
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, await Prove(c, "create", "Vault", signer: new Key()));
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, await Prove(c, "join", "Vault"));
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, await Prove(c, "create", "Other name"));

        Assert.Equal(Enumerable.Repeat(MultisigRoomHub.CreateProofFailedMessage, 4), c.Caller.Errors);
        Assert.Empty(c.Caller.Rooms);
    }

    [Fact]
    public async Task Create_proof_cannot_be_replayed()
    {
        var c = Connect();
        var proof = await Prove(c, "create", "Vault");
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, proof);
        await c.Hub.LeaveRoom(c.Caller.Rooms.Single().RoomId);
        await c.Hub.CreateRoom("Vault", 2, 2, "Alice", c.Pub, proof);

        Assert.Single(c.Caller.Rooms);
        Assert.Equal([MultisigRoomHub.CreateProofFailedMessage], c.Caller.Errors);
    }

    [Fact]
    public async Task Join_with_valid_proof_joins()
    {
        var (_, room) = await CreatedRoom();
        var joiner = Connect();

        await joiner.Hub.JoinRoom(room.InviteCode.ToLowerInvariant(), "Bob", joiner.Pub, await Prove(joiner, "join", room.InviteCode));

        Assert.Empty(joiner.Caller.Errors);
        var updated = joiner.Caller.GroupRooms.Last();
        Assert.Equal(2, updated.Participants.Count);
        Assert.Equal(MultisigRoomStatus.Full, updated.Status);
    }

    [Fact]
    public async Task Bad_join_proof_gets_the_same_error_as_an_unknown_code()
    {
        var (_, room) = await CreatedRoom(total: 3);

        var wrongKey = Connect();
        await wrongKey.Hub.JoinRoom(room.InviteCode, "Mallory", wrongKey.Pub, await Prove(wrongKey, "join", room.InviteCode, signer: new Key()));

        var noProof = Connect();
        await noProof.Hub.JoinRoom(room.InviteCode, "Mallory", noProof.Pub, "");

        var unknown = Connect();
        await unknown.Hub.JoinRoom("ZZZZZZ", "Mallory", unknown.Pub, await Prove(unknown, "join", "ZZZZZZ"));

        Assert.Equal([MultisigRoomHub.InvalidInviteMessage], wrongKey.Caller.Errors);
        Assert.Equal([MultisigRoomHub.InvalidInviteMessage], noProof.Caller.Errors);
        Assert.Equal([MultisigRoomHub.InvalidInviteMessage], unknown.Caller.Errors);
        Assert.Empty(wrongKey.Caller.GroupRooms);
        Assert.Empty(noProof.Caller.GroupRooms);
    }

    [Fact]
    public async Task Claiming_someone_elses_public_key_is_refused()
    {
        var (creator, room) = await CreatedRoom(total: 3);
        var victim = new Key();
        var attacker = Connect();

        // Attacker submits the victim's public key but can only sign with its own key.
        await attacker.Hub.JoinRoom(room.InviteCode, "Victim", victim.PubKey.ToHex(), await Prove(attacker, "join", room.InviteCode));

        Assert.Equal([MultisigRoomHub.InvalidInviteMessage], attacker.Caller.Errors);
        Assert.Empty(attacker.Caller.GroupRooms);
    }

    [Fact]
    public async Task Join_proof_made_for_another_connection_is_refused()
    {
        var (_, room) = await CreatedRoom(total: 3);
        var a = Connect();
        var b = Connect();

        var proofForA = await Prove(a, "join", room.InviteCode);
        await b.Hub.RequestChallenge();
        await b.Hub.JoinRoom(room.InviteCode, "Bob", a.Pub, proofForA);

        Assert.Equal([MultisigRoomHub.InvalidInviteMessage], b.Caller.Errors);
    }

    [Fact]
    public async Task Failed_joins_are_rate_limited_per_connection_without_revealing_the_code()
    {
        var (_, room) = await CreatedRoom(total: 3);
        var c = Connect();

        for (var i = 0; i < CreationRoomGate.MaxFailedJoins; i++)
            await c.Hub.JoinRoom($"ZZZZZ{i}", "Eve", c.Pub, await Prove(c, "join", $"ZZZZZ{i}"));

        // Even a correct code with a correct proof is now refused with the generic limit message.
        await c.Hub.JoinRoom(room.InviteCode, "Eve", c.Pub, await Prove(c, "join", room.InviteCode));

        Assert.Equal(CreationRoomGate.MaxFailedJoins + 1, c.Caller.Errors.Count);
        Assert.Equal(MultisigRoomHub.RateLimitedMessage, c.Caller.Errors[^1]);
        Assert.Empty(c.Caller.GroupRooms);

        // Another connection is unaffected.
        var other = Connect();
        await other.Hub.JoinRoom(room.InviteCode, "Bob", other.Pub, await Prove(other, "join", room.InviteCode));
        Assert.Empty(other.Caller.Errors);
    }

    [Fact]
    public async Task A_connection_already_in_a_room_cannot_create_or_join_another()
    {
        var (creator, _) = await CreatedRoom(total: 3);
        var (_, second) = await CreatedRoom(total: 3);

        await creator.Hub.CreateRoom("Vault 2", 2, 2, "Alice", creator.Pub, await Prove(creator, "create", "Vault 2"));
        await creator.Hub.JoinRoom(second.InviteCode, "Alice", creator.Pub, await Prove(creator, "join", second.InviteCode));

        Assert.Equal([MultisigRoomHub.AlreadyInRoomMessage, MultisigRoomHub.AlreadyInRoomMessage], creator.Caller.Errors);
    }

    // ── fakes ──────────────────────────────────────────────────────────────

    private sealed class RecordingClient : IMultisigRoomClient
    {
        public List<string> Errors { get; } = [];
        public List<MultisigRoom> Rooms { get; } = [];
        public List<MultisigRoom> GroupRooms { get; } = [];
        public bool IsGroup { get; init; }
        public RecordingClient? Owner { get; init; }

        public Task RoomUpdated(MultisigRoom room)
        {
            (Owner ?? this).Rooms.Add(room);
            if (IsGroup) (Owner ?? this).GroupRooms.Add(room);
            return Task.CompletedTask;
        }

        public Task Error(string message) { (Owner ?? this).Errors.Add(message); return Task.CompletedTask; }
        public Task ParticipantJoined(MultisigParticipant participant) => Task.CompletedTask;
        public Task ParticipantLeft(string publicKeyHex) => Task.CompletedTask;
        public Task AllReady(MultisigRoom room) => Task.CompletedTask;
        public Task RoomClosed(string reason) => Task.CompletedTask;
    }

    private sealed class FakeClients(RecordingClient caller) : IHubCallerClients<IMultisigRoomClient>
    {
        private readonly RecordingClient _group = new() { IsGroup = true, Owner = caller };
        private readonly RecordingClient _sink = new();

        public IMultisigRoomClient Caller => caller;
        public IMultisigRoomClient Group(string groupName) => _group;
        public IMultisigRoomClient OthersInGroup(string groupName) => _sink;
        public IMultisigRoomClient Others => _sink;
        public IMultisigRoomClient All => _sink;
        public IMultisigRoomClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => _sink;
        public IMultisigRoomClient Client(string connectionId) => _sink;
        public IMultisigRoomClient Clients(IReadOnlyList<string> connectionIds) => _sink;
        public IMultisigRoomClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _sink;
        public IMultisigRoomClient Groups(IReadOnlyList<string> groupNames) => _sink;
        public IMultisigRoomClient User(string userId) => _sink;
        public IMultisigRoomClient Users(IReadOnlyList<string> userIds) => _sink;
    }

    private sealed class FakeGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}

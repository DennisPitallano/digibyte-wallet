using DigiByte.Crypto.Multisig;
using DigiByte.P2P.Shared.Models;
using DigiByte.Wallet.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace DigiByte.Web.Services;

/// <summary>
/// The wallet's connection to the online signing-room hub (specs/017-multisig-online-signing, T017). Connects lazily,
/// only when the user opens or joins a room, so no core page depends on the hub (FR-021). Every proof is signed here with
/// the active wallet's multisig key, which lives only in a method local. After a dropped connection it reconnects and
/// rejoins the current room with a fresh proof (FR-018).
/// </summary>
public sealed class SigningRoomClient : IAsyncDisposable
{
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(5);

    private readonly IConfiguration _config;
    private readonly WalletService _wallets;
    private HubConnection? _hub;
    private bool? _available;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public SigningRoomClient(IConfiguration config, WalletService wallets)
    {
        _config = config;
        _wallets = wallets;
    }

    /// <summary>The room this client is in, from the last full state, or null.</summary>
    public SigningRoomView? Current { get; private set; }

    /// <summary>The public key this client joined with.</summary>
    public string? MyKey { get; private set; }

    public bool IsReconnecting { get; private set; }

    public event Func<SigningRoomView, Task>? RoomStateReceived;
    public event Func<string, Task>? ExistingRoomReceived;
    public event Func<string, string, Task>? SignatureAdded;
    public event Func<Task>? BroadcastGranted;
    public event Func<string, Task>? BroadcastClaimed;
    public event Func<string, Task>? Broadcasted;
    public event Func<string, Task>? BroadcastFailed;
    public event Func<string, Task>? RoomEnded;
    public event Func<string, string, Task>? ErrorReceived;
    public event Func<Task>? ConnectionChanged;

    private string HubUrl => (_config["PriceApiUrl"]?.Replace("/api/price", "") ?? "http://localhost:5007") + "/hubs/multisig-signing";

    /// <summary>True when the signing service answers within 5 s. Cached for the session once it has answered (R12).</summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        if (_available == true && _hub?.State == HubConnectionState.Connected) return true;
        try
        {
            using var cts = new CancellationTokenSource(AvailabilityTimeout);
            await EnsureConnectedAsync(cts.Token);
            // While the client is still retrying a dropped connection the service is not usable.
            _available = _hub?.State == HubConnectionState.Connected;
        }
        catch
        {
            _available = false;
        }
        return _available == true;
    }

    public async Task<SigningRoomOptionsView> GetOptionsAsync()
    {
        await EnsureConnectedAsync();
        return await _hub!.InvokeAsync<SigningRoomOptionsView>("GetOptions");
    }

    /// <summary>Opens a room for a proposal. The answer arrives as RoomState, ExistingRoom or Error.</summary>
    public async Task OpenAsync(string network, IReadOnlyList<string> coSignerKeys, int requiredSignatures, string psbtBase64,
        string? memo, int lifetimeMinutes, string displayName, string unsignedTxId)
    {
        await EnsureConnectedAsync();
        var key = RequireKeyHex();
        var request = new OpenSigningRoomRequest
        {
            Network = network,
            CoSignerKeys = [.. coSignerKeys],
            RequiredSignatures = requiredSignatures,
            PsbtBase64 = psbtBase64,
            Memo = memo,
            LifetimeMinutes = lifetimeMinutes,
            DisplayName = displayName,
            PublicKeyHex = key,
            Proof = await ProveAsync(SigningPurpose.Open, unsignedTxId),
        };
        MyKey = key.ToLowerInvariant();
        await _hub!.InvokeAsync("OpenRoom", request);
    }

    public async Task JoinAsync(string inviteCode, string displayName)
    {
        await EnsureConnectedAsync();
        var key = RequireKeyHex();
        var code = inviteCode.Trim().ToUpperInvariant();
        var request = new JoinSigningRoomRequest
        {
            InviteCode = code,
            DisplayName = displayName,
            PublicKeyHex = key,
            Proof = await ProveAsync(SigningPurpose.Join, code),
        };
        MyKey = key.ToLowerInvariant();
        await _hub!.InvokeAsync("JoinRoom", request);
    }

    public Task SubmitSignatureAsync(string psbtBase64) => InvokeInRoomAsync("SubmitSignature", psbtBase64);
    public Task DeclineAsync() => InvokeInRoomAsync("Decline");
    public Task ClaimBroadcastAsync() => InvokeInRoomAsync("ClaimBroadcast");
    public Task CloseAsync() => InvokeInRoomAsync("CloseRoom");

    public async Task ReportBroadcastAsync(bool success, string txIdOrReason)
    {
        if (Current is null || _hub is null) return;
        await _hub.InvokeAsync("ReportBroadcast", Current.RoomId, success, txIdOrReason);
    }

    /// <summary>Leaves the room on this device; the participant stays in it, shown offline.</summary>
    public async Task LeaveAsync()
    {
        var room = Current;
        Current = null;
        if (room is null || _hub?.State != HubConnectionState.Connected) return;
        try { await _hub.InvokeAsync("LeaveRoom", room.RoomId); } catch { /* already gone */ }
    }

    /// <summary>Forgets the current room locally (after it ended).</summary>
    public void Forget() => Current = null;

    private async Task InvokeInRoomAsync(string method, params object[] args)
    {
        if (Current is null) throw new InvalidOperationException("You are not in a signing room.");
        await EnsureConnectedAsync();
        await _hub!.InvokeCoreAsync(method, [Current.RoomId, .. args]);
    }

    private static class SigningPurpose
    {
        public const string Open = "open";
        public const string Join = "join";
        public const string Rejoin = "rejoin";
    }

    private string RequireKeyHex() =>
        _wallets.GetPublicKey() ?? throw new InvalidOperationException("Unlock your wallet to use online signing.");

    /// <summary>Asks for a single-use nonce and signs it with the wallet's multisig key (constitution 1.2.0).</summary>
    private async Task<string> ProveAsync(string purpose, string target)
    {
        var hub = _hub ?? throw new InvalidOperationException("Not connected to the signing service.");
        var nonce = await hub.InvokeAsync<string>("RequestChallenge");
        var connectionId = hub.ConnectionId ?? throw new InvalidOperationException("Not connected to the signing service.");
        var keyHex = _wallets.GetPrivateKeyForMultisig()
            ?? throw new InvalidOperationException("Unlock your wallet to use online signing.");
        using var key = new NBitcoin.Key(Convert.FromHexString(keyHex));
        return CoSignerProof.Sign(key, CoSignerProof.SigningRoomDomain, purpose, target, nonce, connectionId);
    }

    private async Task EnsureConnectedAsync(CancellationToken token = default)
    {
        if (_hub?.State == HubConnectionState.Connected) return;
        await _connectLock.WaitAsync(token);
        try
        {
            if (_hub is null)
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl(HubUrl)
                    .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)])
                    .Build();
                Wire(_hub);
            }
            if (_hub.State == HubConnectionState.Disconnected)
                await _hub.StartAsync(token);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void Wire(HubConnection hub)
    {
        hub.On<SigningRoomView>("RoomState", async view =>
        {
            Current = Merge(Current, view);
            if (RoomStateReceived is { } h) await h(Current);
        });
        hub.On<string>("ExistingRoom", async code => { if (ExistingRoomReceived is { } h) await h(code); });
        hub.On<string, string>("SignatureAdded", async (key, psbt) => { if (SignatureAdded is { } h) await h(key, psbt); });
        hub.On("BroadcastGranted", async () => { if (BroadcastGranted is { } h) await h(); });
        hub.On<string>("BroadcastClaimed", async key => { if (BroadcastClaimed is { } h) await h(key); });
        hub.On<string>("Broadcasted", async txId => { if (Broadcasted is { } h) await h(txId); });
        hub.On<string>("BroadcastFailed", async reason => { if (BroadcastFailed is { } h) await h(reason); });
        hub.On<string>("RoomEnded", async reason =>
        {
            Current = null;
            if (RoomEnded is { } h) await h(reason);
        });
        hub.On("Superseded", async () =>
        {
            Current = null;
            if (RoomEnded is { } h) await h("superseded");
        });
        hub.On<string, string>("Error", async (code, message) => { if (ErrorReceived is { } h) await h(code, message); });

        hub.Reconnecting += async _ =>
        {
            IsReconnecting = true;
            if (ConnectionChanged is { } h) await h();
        };
        hub.Reconnected += async _ =>
        {
            IsReconnecting = false;
            if (Current is { } room && MyKey is not null)
            {
                try
                {
                    await hub.InvokeAsync("Rejoin", new RejoinSigningRoomRequest
                    {
                        RoomId = room.RoomId,
                        PublicKeyHex = MyKey,
                        Proof = await ProveAsync(SigningPurpose.Rejoin, room.RoomId),
                    });
                }
                catch { /* the room may have ended; the page shows the state it has */ }
            }
            if (ConnectionChanged is { } h) await h();
        };
        hub.Closed += async _ =>
        {
            IsReconnecting = false;
            _available = null;
            if (Current is not null)
            {
                Current = null;
                if (RoomEnded is { } ended) await ended("connection_lost");
            }
            if (ConnectionChanged is { } h) await h();
        };
    }

    /// <summary>Updates without the payload keep the payload already received.</summary>
    private static SigningRoomView Merge(SigningRoomView? previous, SigningRoomView next)
    {
        if (next.IncludesPayload || previous is null || previous.RoomId != next.RoomId) return next;
        next.PsbtBase64 = previous.PsbtBase64;
        next.SignedPsbts = previous.SignedPsbts;
        next.IncludesPayload = previous.IncludesPayload;
        return next;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _connectLock.Dispose();
    }
}

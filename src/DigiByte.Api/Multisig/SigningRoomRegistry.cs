using System.Security.Cryptography;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.P2P.Shared.Models;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace DigiByte.Api.Multisig;

/// <summary>
/// All rules of online multisig signing rooms (specs/017-multisig-online-signing), kept apart from SignalR so they can be
/// unit tested. In memory only; a room's data is dropped when it ends (FR-024). A connection sees or changes a room only
/// after a single-use <see cref="CoSignerProof"/> (signing-room domain) shows it holds one of that room's co-signer keys
/// (constitution 1.2.0, Auth boundary). Holds no key material and logs nothing.
/// </summary>
public sealed class SigningRoomRegistry
{
    public const string PurposeOpen = "open";
    public const string PurposeJoin = "join";
    public const string PurposeRejoin = "rejoin";

    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ClaimLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan BroadcastGrace = TimeSpan.FromSeconds(60);
    public const int MaxPsbtBytes = 100 * 1024;
    public const int MaxMemoLength = 200;
    public const int MaxDisplayNameLength = 40;
    public const int MaxReasonLength = 200;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly TimeProvider _time;
    private readonly SigningRoomOptions _options;
    private readonly object _lock = new();
    private readonly Dictionary<string, Room> _rooms = new();                       // roomId → room
    private readonly Dictionary<string, string> _codes = new();                     // invite code → roomId
    private readonly Dictionary<string, string> _byTxId = new(StringComparer.OrdinalIgnoreCase); // unsigned txid → roomId
    private readonly Dictionary<string, (string Nonce, DateTimeOffset At)> _challenges = new();  // connectionId → nonce
    private readonly Dictionary<string, (string RoomId, string Key)> _connections = new();       // connectionId → membership

    public SigningRoomRegistry(TimeProvider time, IOptions<SigningRoomOptions> options)
    {
        _time = time;
        _options = options.Value;
    }

    // ─── Results ───

    public sealed record Failure(string Code, string Message, bool CountsAsFailedJoin = false);

    public sealed record OpenResult(Failure? Error, string? RoomId = null, string? ExistingCode = null);

    public sealed record JoinResult(Failure? Error, string? RoomId = null, string? SupersededConnection = null);

    public sealed record SubmitResult(Failure? Error, string? PublicKeyHex = null, string? PsbtBase64 = null);

    public sealed record ClaimResult(Failure? Error, bool Granted = false, string? HolderKey = null);

    public sealed record ReportResult(Failure? Error, string? TxId = null, string? FailureReason = null);

    public sealed record EndedRoom(string RoomId, string Reason, IReadOnlyList<string> Connections, int Participants, int Signatures);

    public sealed record SweepResult(IReadOnlyList<EndedRoom> Ended, IReadOnlyList<string> ClaimsReleased);

    // ─── Room state ───

    private sealed class Participant
    {
        public required string Key { get; init; }
        public required string Name { get; set; }
        public string? ConnectionId { get; set; }
        public SigningParticipantAction Action { get; set; }
        public bool IsCreator { get; init; }
    }

    private sealed class Room
    {
        public required string Id { get; init; }
        public required string Code { get; init; }
        public required string TxId { get; init; }
        public required string NetworkName { get; init; }
        public required Network Network { get; init; }
        public required List<string> Keys { get; init; }
        public required int M { get; init; }
        public required string Psbt { get; init; }
        public string? Memo { get; init; }
        public required string CreatorKey { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public Dictionary<string, Participant> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SignedPsbts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SignedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public (string Key, DateTimeOffset At)? Claim { get; set; }
        public string? BroadcastTxId { get; set; }
        public DateTimeOffset? BroadcastAt { get; set; }

        public SigningRoomStatus Status =>
            BroadcastTxId is not null ? SigningRoomStatus.Broadcast
            : SignedKeys.Count >= M ? SigningRoomStatus.Complete
            : SigningRoomStatus.Open;

        public bool Unreachable
        {
            get
            {
                var possible = Keys.Count(k => SignedKeys.Contains(k)
                    || !Participants.TryGetValue(k, out var p) || p.Action != SigningParticipantAction.Declined);
                return possible < M;
            }
        }
    }

    // ─── Options and challenges ───

    public SigningRoomOptionsView Options() => new()
    {
        AllowedLifetimesMinutes = [.. _options.EffectiveLifetimes()],
        DefaultLifetimeMinutes = _options.EffectiveDefault(),
    };

    /// <summary>A fresh 32-byte nonce for the connection, replacing any outstanding one.</summary>
    public string IssueChallenge(string connectionId)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        lock (_lock) _challenges[connectionId] = (nonce, _time.GetUtcNow());
        return nonce;
    }

    /// <summary>Uses up the connection's nonce whatever the outcome; true only for a valid, fresh proof.</summary>
    private bool ConsumeProof(string connectionId, string purpose, string target, string publicKeyHex, string? proof)
    {
        if (!_challenges.Remove(connectionId, out var challenge)) return false;
        if (_time.GetUtcNow() - challenge.At > ChallengeLifetime) return false;
        if (string.IsNullOrWhiteSpace(proof) || string.IsNullOrWhiteSpace(publicKeyHex) || string.IsNullOrWhiteSpace(target)) return false;
        return CoSignerProof.TryVerify(publicKeyHex.Trim(), proof, CoSignerProof.SigningRoomDomain, purpose, target,
            challenge.Nonce, connectionId);
    }

    // ─── Opening ───

    public OpenResult Open(string connectionId, OpenSigningRoomRequest request)
    {
        if (request is null) return new(new Failure(SigningRoomErrors.BadRequest, "Missing request."));

        var network = ParseNetwork(request.Network);
        if (network is null) return new(new Failure(SigningRoomErrors.BadPsbt, "Unknown network."));

        var lifetime = request.LifetimeMinutes ?? _options.EffectiveDefault();
        if (!_options.EffectiveLifetimes().Contains(lifetime))
            return new(new Failure(SigningRoomErrors.BadLifetime, "That room length is not available."));

        var keys = NormalizeKeys(request.CoSignerKeys);
        if (keys is null || keys.Count < 2 || keys.Count > 7)
            return new(new Failure(SigningRoomErrors.BadRequest, "A multisig needs 2 to 7 valid co-signer keys."));
        if (request.RequiredSignatures < 1 || request.RequiredSignatures > keys.Count)
            return new(new Failure(SigningRoomErrors.BadRequest, "Invalid number of required signatures."));

        var name = NormalizeName(request.DisplayName);
        if (name is null)
            return new(new Failure(SigningRoomErrors.BadRequest, $"Your name must be 1–{MaxDisplayNameLength} characters."));
        var memo = string.IsNullOrWhiteSpace(request.Memo) ? null : request.Memo.Trim();
        if (memo is { Length: > MaxMemoLength })
            return new(new Failure(SigningRoomErrors.BadRequest, $"The note must be at most {MaxMemoLength} characters."));

        var psbt = ParsePsbt(request.PsbtBase64, network);
        if (psbt is null) return new(new Failure(SigningRoomErrors.BadPsbt, "That is not a valid transaction (PSBT)."));
        var txId = psbt.GetGlobalTransaction().GetHash().ToString();
        var key = request.PublicKeyHex?.Trim().ToLowerInvariant() ?? "";

        lock (_lock)
        {
            if (!ConsumeProof(connectionId, PurposeOpen, txId, key, request.Proof) || !keys.Contains(key))
                return new(new Failure(SigningRoomErrors.BadProofOpen, "Could not verify that you hold one of this multisig's keys."));

            if (!PsbtSignatureCheck.SpendsMultisig(psbt, keys, request.RequiredSignatures))
                return new(new Failure(SigningRoomErrors.BadPsbt, "This payment does not spend this multisig's coins."));

            // One live room per payment (FR-019a). Its code goes only to a key of that room (constitution 1.2.0).
            if (_byTxId.TryGetValue(txId, out var existingId) && _rooms.TryGetValue(existingId, out var existing))
            {
                return existing.Keys.Contains(key)
                    ? new(null, ExistingCode: existing.Code)
                    : new(new Failure(SigningRoomErrors.InvalidCode, SigningRoomErrors.InvalidCodeMessage, CountsAsFailedJoin: true));
            }

            foreach (var input in psbt.Inputs) input.PartialSigs.Clear();
            var room = new Room
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Code = NewCode(),
                TxId = txId,
                NetworkName = request.Network.Trim().ToLowerInvariant(),
                Network = network,
                Keys = keys,
                M = request.RequiredSignatures,
                Psbt = psbt.ToBase64(),
                Memo = memo,
                CreatorKey = key,
                ExpiresAt = _time.GetUtcNow().AddMinutes(lifetime),
            };
            room.Participants[key] = new Participant { Key = key, Name = name, ConnectionId = connectionId, IsCreator = true };
            DetachConnection(connectionId);
            _rooms[room.Id] = room;
            _codes[room.Code] = room.Id;
            _byTxId[txId] = room.Id;
            _connections[connectionId] = (room.Id, key);

            // Signatures the opener already has are offered to the room as their first submission.
            var signedCopy = ParsePsbt(request.PsbtBase64, network);
            if (signedCopy is not null && signedCopy.Inputs.Any(i => i.PartialSigs.Count > 0))
            {
                var checks = PsbtSignatureCheck.Check(signedCopy);
                if (checks.All(c => c.IsValid && keys.Contains(c.PublicKeyHex.ToLowerInvariant())))
                    AcceptSignatures(room, key, signedCopy);
            }

            return new(null, RoomId: room.Id);
        }
    }

    // ─── Joining ───

    public JoinResult Join(string connectionId, JoinSigningRoomRequest request)
    {
        var name = NormalizeName(request?.DisplayName);
        if (request is null || name is null)
            return new(new Failure(SigningRoomErrors.BadRequest, $"Your name must be 1–{MaxDisplayNameLength} characters."));

        var code = (request.InviteCode ?? "").Trim().ToUpperInvariant();
        var key = request.PublicKeyHex?.Trim().ToLowerInvariant() ?? "";

        lock (_lock)
        {
            // The proof is checked (using up its nonce) before the code is looked up, and every failure looks the same.
            var proofOk = code.Length > 0 && ConsumeProof(connectionId, PurposeJoin, code, key, request.Proof);
            if (!proofOk || !_codes.TryGetValue(code, out var roomId) || !_rooms.TryGetValue(roomId, out var room)
                || !room.Keys.Contains(key))
                return new(InvalidCode());

            DetachConnection(connectionId);
            string? superseded = null;
            if (room.Participants.TryGetValue(key, out var participant))
            {
                if (participant.ConnectionId is not null && participant.ConnectionId != connectionId)
                {
                    superseded = participant.ConnectionId;
                    _connections.Remove(superseded);
                }
                participant.ConnectionId = connectionId;
                participant.Name = name;
            }
            else
            {
                room.Participants[key] = new Participant { Key = key, Name = name, ConnectionId = connectionId };
            }
            _connections[connectionId] = (room.Id, key);
            return new(null, room.Id, superseded);
        }
    }

    /// <summary>Back after a dropped connection: needs a new proof, and only a participant of the room may rejoin (FR-018).</summary>
    public JoinResult Rejoin(string connectionId, RejoinSigningRoomRequest request)
    {
        var roomId = request?.RoomId?.Trim() ?? "";
        var key = request?.PublicKeyHex?.Trim().ToLowerInvariant() ?? "";

        lock (_lock)
        {
            var proofOk = roomId.Length > 0 && ConsumeProof(connectionId, PurposeRejoin, roomId, key, request?.Proof);
            if (!proofOk || !_rooms.TryGetValue(roomId, out var room) || !room.Participants.TryGetValue(key, out var participant))
                return new(InvalidCode());

            DetachConnection(connectionId);
            string? superseded = null;
            if (participant.ConnectionId is not null && participant.ConnectionId != connectionId)
            {
                superseded = participant.ConnectionId;
                _connections.Remove(superseded);
            }
            participant.ConnectionId = connectionId;
            _connections[connectionId] = (room.Id, key);
            return new(null, room.Id, superseded);
        }
    }

    // ─── Signing and declining ───

    /// <summary>
    /// A participant hands the room a signed copy. Every partial signature in it must be from a co-signer key of the room
    /// and verify against its input; otherwise nothing is kept or relayed. Signatures a participant holds from others
    /// (imported offline) are accepted too, since they are verified (FR-012).
    /// </summary>
    public SubmitResult Submit(string connectionId, string roomId, string psbtBase64)
    {
        lock (_lock)
        {
            if (!TryMember(connectionId, roomId, out var room, out var key, out var error)) return new(error);
            if (room.Status == SigningRoomStatus.Broadcast)
                return new(new Failure(SigningRoomErrors.RoomEnded, "This payment was already sent."));

            var psbt = ParsePsbt(psbtBase64, room.Network);
            if (psbt is null || !string.Equals(psbt.GetGlobalTransaction().GetHash().ToString(), room.TxId, StringComparison.OrdinalIgnoreCase))
                return new(new Failure(SigningRoomErrors.BadSignature, "That is not a signed copy of this payment."));

            var checks = PsbtSignatureCheck.Check(psbt);
            if (checks.Count == 0)
                return new(new Failure(SigningRoomErrors.BadSignature, "That copy carries no signatures."));
            if (checks.Any(c => !c.IsValid || !room.Keys.Contains(c.PublicKeyHex.ToLowerInvariant())))
                return new(new Failure(SigningRoomErrors.BadSignature, "That copy has a signature that does not verify or is not from a co-signer."));

            var stored = AcceptSignatures(room, key, psbt);
            return new(null, key, stored);
        }
    }

    public Failure? Decline(string connectionId, string roomId)
    {
        lock (_lock)
        {
            if (!TryMember(connectionId, roomId, out var room, out var key, out var error)) return error;
            if (room.SignedKeys.Contains(key))
                return new Failure(SigningRoomErrors.NotAllowed, "You already signed this payment.");
            room.Participants[key].Action = SigningParticipantAction.Declined;
            return null;
        }
    }

    // ─── Broadcasting ───

    public ClaimResult ClaimBroadcast(string connectionId, string roomId)
    {
        lock (_lock)
        {
            if (!TryMember(connectionId, roomId, out var room, out var key, out var error)) return new(error);
            if (room.Status != SigningRoomStatus.Complete)
                return new(new Failure(SigningRoomErrors.NotAllowed, "This payment does not have enough signatures yet."));

            if (room.Claim is { } claim && claim.Key != key && !ClaimStale(room, claim))
                return new(null, Granted: false, HolderKey: claim.Key);

            room.Claim = (key, _time.GetUtcNow());
            return new(null, Granted: true, HolderKey: key);
        }
    }

    public ReportResult ReportBroadcast(string connectionId, string roomId, bool success, string? txIdOrReason)
    {
        lock (_lock)
        {
            if (!TryMember(connectionId, roomId, out var room, out var key, out var error)) return new(error);
            if (room.Claim?.Key != key)
                return new(new Failure(SigningRoomErrors.NotAllowed, "Only the participant broadcasting can report the result."));

            if (success)
            {
                var txId = (txIdOrReason ?? "").Trim().ToLowerInvariant();
                if (txId.Length != 64 || !txId.All(Uri.IsHexDigit))
                    return new(new Failure(SigningRoomErrors.BadRequest, "That is not a transaction id."));
                room.BroadcastTxId = txId;
                room.BroadcastAt = _time.GetUtcNow();
                room.Claim = null;
                return new(null, TxId: txId);
            }

            room.Claim = null;
            var reason = string.IsNullOrWhiteSpace(txIdOrReason) ? "The network did not accept the transaction." : txIdOrReason.Trim();
            if (reason.Length > MaxReasonLength) reason = reason[..MaxReasonLength];
            return new(null, FailureReason: reason);
        }
    }

    // ─── Closing, leaving, disconnecting ───

    /// <summary>The creator ends the room early (FR-019). The proposal itself is untouched on every device.</summary>
    public (Failure? Error, EndedRoom? Ended) Close(string connectionId, string roomId)
    {
        lock (_lock)
        {
            if (!TryMember(connectionId, roomId, out var room, out var key, out var error)) return (error, null);
            if (room.CreatorKey != key)
                return (new Failure(SigningRoomErrors.NotAllowed, "Only the person who opened the room can close it."), null);
            return (null, Remove(room, SigningRoomEndReasons.Closed));
        }
    }

    /// <summary>The connection leaves; its participant stays in the room, shown offline (FR-018, FR-019).</summary>
    public string? Leave(string connectionId) => Detach(connectionId);

    /// <summary>Same as leaving, plus forgetting the connection's challenge.</summary>
    public string? Disconnect(string connectionId)
    {
        lock (_lock) _challenges.Remove(connectionId);
        return Detach(connectionId);
    }

    private string? Detach(string connectionId)
    {
        lock (_lock) return DetachConnection(connectionId);
    }

    private string? DetachConnection(string connectionId)
    {
        if (!_connections.Remove(connectionId, out var membership)) return null;
        if (!_rooms.TryGetValue(membership.RoomId, out var room)) return null;
        if (room.Participants.TryGetValue(membership.Key, out var p) && p.ConnectionId == connectionId)
            p.ConnectionId = null;
        if (room.Claim?.Key == membership.Key) room.Claim = null;
        return room.Id;
    }

    // ─── Expiry ───

    /// <summary>Ends expired rooms and broadcast rooms past their grace period; releases stale broadcast claims.</summary>
    public SweepResult Sweep()
    {
        var ended = new List<EndedRoom>();
        var released = new List<string>();
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            foreach (var room in _rooms.Values.ToList())
            {
                if (room.BroadcastAt is { } at && now - at >= BroadcastGrace)
                    ended.Add(Remove(room, SigningRoomEndReasons.Broadcast));
                else if (now >= room.ExpiresAt)
                    ended.Add(Remove(room, SigningRoomEndReasons.Expired));
                else if (room.Claim is { } claim && ClaimStale(room, claim))
                {
                    room.Claim = null;
                    released.Add(room.Id);
                }
            }
        }
        return new(ended, released);
    }

    // ─── Views ───

    public SigningRoomView? View(string roomId, bool includePayload)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return null;
            return new SigningRoomView
            {
                RoomId = room.Id,
                InviteCode = room.Code,
                ExpiresAt = room.ExpiresAt,
                Network = room.NetworkName,
                UnsignedTxId = room.TxId,
                RequiredSignatures = room.M,
                CoSignerKeys = [.. room.Keys],
                IncludesPayload = includePayload,
                PsbtBase64 = includePayload ? room.Psbt : null,
                SignedPsbts = includePayload
                    ? room.SignedPsbts.Select(kv => new SignedPsbtView { PublicKeyHex = kv.Key, PsbtBase64 = kv.Value }).ToList()
                    : [],
                SignedKeys = [.. room.SignedKeys.Order()],
                Memo = room.Memo,
                Participants = room.Participants.Values.Select(p => new SigningParticipantView
                {
                    PublicKeyHex = p.Key,
                    DisplayName = p.Name,
                    Online = p.ConnectionId is not null,
                    Action = room.SignedKeys.Contains(p.Key) ? SigningParticipantAction.Signed : p.Action,
                    IsCreator = p.IsCreator,
                }).ToList(),
                Status = room.Status,
                Unreachable = room.Status == SigningRoomStatus.Open && room.Unreachable,
                BroadcastClaimHolder = room.Claim?.Key,
                BroadcastTxId = room.BroadcastTxId,
            };
        }
    }

    /// <summary>The room this connection is in, if any.</summary>
    public string? RoomOf(string connectionId)
    {
        lock (_lock) return _connections.TryGetValue(connectionId, out var m) ? m.RoomId : null;
    }

    public int RoomCount
    {
        get { lock (_lock) return _rooms.Count; }
    }

    // ─── Helpers ───

    private bool TryMember(string connectionId, string roomId, out Room room, out string key, out Failure? error)
    {
        room = null!;
        key = "";
        error = null;
        if (!_connections.TryGetValue(connectionId, out var m) || m.RoomId != roomId || !_rooms.TryGetValue(roomId, out var found))
        {
            error = new Failure(SigningRoomErrors.RoomEnded, "You are not in this room, or it has ended.");
            return false;
        }
        room = found;
        key = m.Key;
        return true;
    }

    private string AcceptSignatures(Room room, string submitterKey, PSBT psbt)
    {
        var combined = psbt;
        if (room.SignedPsbts.TryGetValue(submitterKey, out var previous) && ParsePsbt(previous, room.Network) is { } prev)
            combined = prev.Combine(psbt);
        var stored = combined.ToBase64();
        room.SignedPsbts[submitterKey] = stored;

        // Recompute which keys have a valid signature on every input across everything the room holds.
        var all = ParsePsbt(room.Psbt, room.Network)!;
        foreach (var copy in room.SignedPsbts.Values)
            if (ParsePsbt(copy, room.Network) is { } p) all = all.Combine(p);
        room.SignedKeys.Clear();
        foreach (var k in PsbtSignatureCheck.KeysValidOnEveryInput(all).Where(room.Keys.Contains))
            room.SignedKeys.Add(k);
        return stored;
    }

    private bool ClaimStale(Room room, (string Key, DateTimeOffset At) claim) =>
        _time.GetUtcNow() - claim.At >= ClaimLifetime
        || !room.Participants.TryGetValue(claim.Key, out var holder) || holder.ConnectionId is null;

    private EndedRoom Remove(Room room, string reason)
    {
        _rooms.Remove(room.Id);
        _codes.Remove(room.Code);
        _byTxId.Remove(room.TxId);
        var connections = room.Participants.Values.Select(p => p.ConnectionId).OfType<string>().ToList();
        foreach (var c in connections) _connections.Remove(c);
        return new EndedRoom(room.Id, reason, connections, room.Participants.Count, room.SignedKeys.Count);
    }

    private static Failure InvalidCode() =>
        new(SigningRoomErrors.InvalidCode, SigningRoomErrors.InvalidCodeMessage, CountsAsFailedJoin: true);

    private string NewCode()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            var code = new string(bytes.Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());
            if (!_codes.ContainsKey(code)) return code;
        }
    }

    private static Network? ParseNetwork(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "main" or "mainnet" => DigiByteNetwork.Mainnet,
        "test" or "testnet" => DigiByteNetwork.Testnet,
        "regtest" => DigiByteNetwork.Regtest,
        _ => null,
    };

    private static PSBT? ParsePsbt(string? base64, Network network)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        var trimmed = base64.Trim();
        if (trimmed.Length > MaxPsbtBytes * 4 / 3 + 4) return null;
        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            if (bytes.Length > MaxPsbtBytes) return null;
            return PSBT.Load(bytes, network);
        }
        catch { return null; }
    }

    private static List<string>? NormalizeKeys(IEnumerable<string>? keys)
    {
        if (keys is null) return null;
        var list = new List<string>();
        foreach (var raw in keys)
        {
            var hex = (raw ?? "").Trim().ToLowerInvariant();
            try
            {
                var pub = new PubKey(hex);
                if (!pub.IsCompressed) return null;
            }
            catch { return null; }
            if (list.Contains(hex)) return null;
            list.Add(hex);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static string? NormalizeName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length is >= 1 and <= MaxDisplayNameLength ? trimmed : null;
    }
}

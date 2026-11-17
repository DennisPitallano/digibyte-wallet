using System.Collections.Concurrent;
using System.Security.Cryptography;
using DigiByte.Crypto.Multisig;

namespace DigiByte.Api.Hubs;

/// <summary>
/// Key-possession gate for the multisig creation-room hub (constitution 1.2.0, Auth boundary: co-signer coordination
/// relay). Issues single-use, connection-bound nonces, checks <see cref="CoSignerProof"/> proofs against them, and
/// rate-limits failed joins per connection. Hub-independent so the rules can be unit tested. Holds no key material
/// and logs nothing.
/// </summary>
public sealed class CreationRoomGate
{
    public const string PurposeCreate = "create";
    public const string PurposeJoin = "join";

    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);
    public const int MaxFailedJoins = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new();   // connectionId → outstanding nonce (at most one)
    private readonly ConcurrentDictionary<string, Failures> _failures = new();       // connectionId → failed joins in window

    public CreationRoomGate(TimeProvider time) => _time = time;

    private sealed record Challenge(string NonceHex, DateTimeOffset IssuedAt);

    private sealed class Failures
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }

    /// <summary>Issues a fresh 32-byte nonce for <paramref name="connectionId"/>, replacing any outstanding one.</summary>
    public string IssueChallenge(string connectionId)
    {
        var nonceHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _challenges[connectionId] = new Challenge(nonceHex, _time.GetUtcNow());
        return nonceHex;
    }

    /// <summary>
    /// Uses up the connection's outstanding nonce (whatever the outcome) and returns true only if it was still valid and
    /// <paramref name="proofBase64"/> proves possession of <paramref name="publicKeyHex"/> for this purpose and target.
    /// </summary>
    public bool TryConsumeProof(string connectionId, string purpose, string target, string publicKeyHex, string? proofBase64)
    {
        if (!_challenges.TryRemove(connectionId, out var challenge))
            return false;
        if (_time.GetUtcNow() - challenge.IssuedAt > ChallengeLifetime)
            return false;
        if (string.IsNullOrEmpty(proofBase64))
            return false;

        return CoSignerProof.TryVerify(publicKeyHex, proofBase64, CoSignerProof.CreationRoomDomain, purpose, target,
            challenge.NonceHex, connectionId);
    }

    /// <summary>True when the connection has used up its failed-join allowance for the current window.</summary>
    public bool IsJoinRateLimited(string connectionId)
    {
        if (!_failures.TryGetValue(connectionId, out var f)) return false;
        lock (f)
        {
            if (_time.GetUtcNow() - f.WindowStart >= FailureWindow) return false;
            return f.Count >= MaxFailedJoins;
        }
    }

    /// <summary>Counts one failed join (unknown code or bad proof) for the connection.</summary>
    public void RecordJoinFailure(string connectionId)
    {
        var f = _failures.GetOrAdd(connectionId, _ => new Failures { WindowStart = _time.GetUtcNow() });
        lock (f)
        {
            var now = _time.GetUtcNow();
            if (now - f.WindowStart >= FailureWindow)
            {
                f.WindowStart = now;
                f.Count = 0;
            }
            f.Count++;
        }
    }

    /// <summary>Drops all state for a closed connection; a new connection must ask for a new challenge.</summary>
    public void Forget(string connectionId)
    {
        _challenges.TryRemove(connectionId, out _);
        _failures.TryRemove(connectionId, out _);
    }
}

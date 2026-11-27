using System.Collections.Concurrent;

namespace DigiByte.Pay.Api.Auth;

public enum ChallengeStatus
{
    Pending = 0,
    Signed = 1,
    Expired = 2,
}

public class ChallengeEntry
{
    public required string Nonce { get; init; }
    public required string Uri { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public ChallengeStatus Status { get; set; } = ChallengeStatus.Pending;
    public string? MerchantId { get; set; }
    public string? SessionApiKey { get; set; }
    public string? DisplayName { get; set; }

    // ---- Re-sign-in for one purpose (specs/034 research R6) ----

    /// <summary>Set for a step-up challenge (e.g. "payout-address"); null for an ordinary sign-in.</summary>
    public string? Purpose { get; init; }
    /// <summary>The merchant and Dashboard session the step-up was started from.</summary>
    public string? PurposeMerchantId { get; init; }
    public string? PurposeSessionId { get; init; }
    /// <summary>The grant minted when the step-up was signed.</summary>
    public string? StepUpGrantId { get; set; }
    /// <summary>True once an abandoned step-up has been written to the audit log.</summary>
    public bool AbandonAudited { get; set; }
}

/// <summary>
/// In-memory store for Digi-ID sign-in challenges. v0 scope — one-process only.
/// Replace with a shared cache (Redis) when Pay.Api scales out.
/// </summary>
public class AuthChallengeStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, ChallengeEntry> _entries = new();

    public ChallengeEntry Create(string uri) => Create(uri, null, null, null);

    /// <summary>A challenge bound to a purpose, merchant and Dashboard session (step-up re-sign-in).</summary>
    public ChallengeEntry Create(string uri, string? purpose, string? merchantId, string? sessionId)
    {
        var nonce = ExtractNonce(uri) ?? throw new ArgumentException("uri must contain ?x=nonce", nameof(uri));
        var entry = new ChallengeEntry
        {
            Nonce = nonce,
            Uri = uri,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(DefaultTtl),
            Purpose = purpose,
            PurposeMerchantId = merchantId,
            PurposeSessionId = sessionId,
        };
        _entries[nonce] = entry;
        return entry;
    }

    public ChallengeEntry? Get(string nonce)
    {
        if (!_entries.TryGetValue(nonce, out var entry)) return null;
        if (entry.Status == ChallengeStatus.Pending && DateTime.UtcNow > entry.ExpiresAt)
        {
            entry.Status = ChallengeStatus.Expired;
        }
        return entry;
    }

    public ChallengeEntry? FindByUri(string uri)
    {
        var nonce = ExtractNonce(uri);
        return nonce is null ? null : Get(nonce);
    }

    /// <summary>Best-effort sweep of expired entries; called from endpoints opportunistically.</summary>
    public void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _entries)
        {
            if (kv.Value.Purpose is not null && !kv.Value.AbandonAudited && kv.Value.Status != ChallengeStatus.Signed
                && now - kv.Value.ExpiresAt < TimeSpan.FromMinutes(15))
                continue; // the payout worker audits abandoned step-ups first (specs/034 FR-009)
            if (kv.Value.Status == ChallengeStatus.Expired
                || (kv.Value.Status == ChallengeStatus.Signed && now - kv.Value.CreatedAt > TimeSpan.FromMinutes(10))
                || now - kv.Value.ExpiresAt > TimeSpan.FromMinutes(15))
            {
                _entries.TryRemove(kv.Key, out _);
            }
        }
    }

    /// <summary>
    /// Step-up challenges that expired unsigned and haven't been audited yet. Each is returned once (specs/034
    /// tasks T029: an abandoned re-sign-in is audited as <c>payout.stepup_failed</c>).
    /// </summary>
    public IReadOnlyList<ChallengeEntry> TakeAbandonedStepUps()
    {
        var now = DateTime.UtcNow;
        var taken = new List<ChallengeEntry>();
        foreach (var entry in _entries.Values)
        {
            if (entry.Purpose is null || entry.AbandonAudited || entry.Status == ChallengeStatus.Signed) continue;
            if (entry.Status == ChallengeStatus.Pending && now <= entry.ExpiresAt) continue;
            entry.Status = ChallengeStatus.Expired;
            entry.AbandonAudited = true;
            taken.Add(entry);
        }
        return taken;
    }

    private static string? ExtractNonce(string uri)
    {
        var q = uri.IndexOf('?');
        if (q < 0) return null;
        foreach (var pair in uri[(q + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            if (pair[..eq] == "x" && eq < pair.Length - 1)
                return pair[(eq + 1)..];
        }
        return null;
    }
}

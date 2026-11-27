using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Fresh Digi-ID re-sign-in for setting or changing a payout address (specs/034 FR-004, research R6). The grant is
/// bound to one purpose, merchant and Dashboard session, lasts 5 minutes and works once. Only the Digi-ID callback
/// can mint one, so the dev sign-in paths never can.
/// </summary>
public sealed class StepUpService
{
    private readonly DigiPayDbContext _db;
    private readonly AuthChallengeStore _challenges;
    private readonly AuditLogger _audit;
    private readonly IConfiguration _config;

    public StepUpService(DigiPayDbContext db, AuthChallengeStore challenges, AuditLogger audit, IConfiguration config)
    {
        _db = db;
        _challenges = challenges;
        _audit = audit;
        _config = config;
    }

    public ChallengeEntry IssueChallenge(string purpose, string merchantId, string sessionId)
    {
        _challenges.Prune();
        var nonce = Payout.NewId("") + Payout.NewId("");
        var domain = _config["DigiPay:DigiIdDomain"] ?? "localhost:5252";
        var unsecure = domain.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || domain.StartsWith("127.0.0.1", StringComparison.Ordinal)
            || domain.StartsWith("[::1]", StringComparison.Ordinal);
        var uri = unsecure
            ? $"digiid://{domain}/v1/pay/auth/callback?x={nonce}&u=1"
            : $"digiid://{domain}/v1/pay/auth/callback?x={nonce}";
        return _challenges.Create(uri, purpose, merchantId, sessionId);
    }

    /// <summary>Called by the Digi-ID callback once the signature is verified.</summary>
    public async Task<IResult> CompleteAsync(ChallengeEntry entry, string signingAddress)
    {
        var merchant = await _db.Merchants.FirstOrDefaultAsync(m => m.Id == entry.PurposeMerchantId);
        if (merchant is null || merchant.DigiIdAddress is null
            || !string.Equals(merchant.DigiIdAddress, signingAddress, StringComparison.Ordinal))
        {
            entry.Status = ChallengeStatus.Expired;
            entry.AbandonAudited = true;
            if (merchant is not null)
                await _audit.LogAsync(merchant.Id, "payout.stepup_failed", "Merchant", merchant.Id,
                    summary: "A re-sign-in for a payout address change was signed by a different Digi-ID address",
                    metadata: new { reason = "address_mismatch" });
            return Results.Json(new { error = "stepup_address_mismatch" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var grant = new StepUpGrant
        {
            Id = Payout.NewId("sug_"),
            MerchantId = merchant.Id,
            SessionId = entry.PurposeSessionId!,
            Purpose = entry.Purpose!,
            DigiIdAddress = signingAddress,
            ExpiresAt = DateTime.UtcNow + Payout.StepUpLifetime,
        };
        _db.StepUpGrants.Add(grant);
        await _db.SaveChangesAsync();
        entry.Status = ChallengeStatus.Signed;
        entry.StepUpGrantId = grant.Id;
        entry.MerchantId = merchant.Id;
        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// Marks a grant used (not saved; the caller saves it with the change it authorises). False when the grant is
    /// missing, expired, already used, or bound to another purpose, merchant or session.
    /// </summary>
    public async Task<bool> TryConsumeAsync(string? grantId, string purpose, string merchantId, string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(grantId)) return false;
        var grant = await _db.StepUpGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null || grant.ConsumedAt is not null || grant.ExpiresAt < DateTime.UtcNow) return false;
        if (grant.Purpose != purpose || grant.MerchantId != merchantId || grant.SessionId != sessionId) return false;
        grant.ConsumedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Audits step-ups that expired unsigned (FR-009). Run by the payout worker each cycle.</summary>
    public async Task AuditAbandonedAsync(CancellationToken ct = default)
    {
        foreach (var entry in _challenges.TakeAbandonedStepUps())
        {
            if (entry.PurposeMerchantId is null) continue;
            await _audit.LogAsync(entry.PurposeMerchantId, "payout.stepup_failed", "Merchant", entry.PurposeMerchantId,
                summary: "A re-sign-in for a payout address change expired without being signed",
                metadata: new { reason = "abandoned" });
        }
    }
}

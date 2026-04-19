using DigiByte.Crypto.DigiId;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Digi-ID sign-in for merchants. Flow:
///   1. Browser GETs /v1/pay/auth/challenge → { nonce, uri }
///   2. Browser shows QR of uri, polls /v1/pay/auth/poll/{nonce}
///   3. Wallet scans, signs, POSTs to /v1/pay/auth/callback with { address, uri, signature }
///   4. Server verifies signature, finds/creates the merchant keyed by Digi-ID address,
///      mints a session API key, stores on the challenge entry.
///   5. Poll response flips to { token, merchantId, displayName } → browser stores + navigates.
/// </summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group, IConfiguration config)
    {
        // GET /v1/pay/auth/challenge — fresh nonce for a new sign-in attempt.
        group.MapGet("/challenge", (AuthChallengeStore store, IConfiguration cfg) =>
        {
            store.Prune();
            var nonce = RandomId(24);
            var domain = cfg["DigiPay:DigiIdDomain"] ?? "localhost:5252";
            var uri = $"digiid://{domain}/v1/pay/auth/callback?x={nonce}&u=1";
            var entry = store.Create(uri);
            return Results.Ok(new { nonce = entry.Nonce, uri = entry.Uri, expiresAt = entry.ExpiresAt });
        });

        // POST /v1/pay/auth/callback — wallet delivers { address, uri, signature }.
        group.MapPost("/callback", async (
            DigiIdCallbackBody body,
            AuthChallengeStore store,
            DigiPayDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Address)
                || string.IsNullOrWhiteSpace(body.Uri)
                || string.IsNullOrWhiteSpace(body.Signature))
                return Results.BadRequest(new { error = "address, uri, and signature are required" });

            var entry = store.FindByUri(body.Uri);
            if (entry is null) return Results.NotFound(new { error = "unknown challenge" });
            if (entry.Status != ChallengeStatus.Pending)
                return Results.Conflict(new { error = $"challenge is {entry.Status.ToString().ToLowerInvariant()}" });

            if (!DigiIdService.Verify(body.Address, body.Uri, body.Signature))
                return Results.BadRequest(new { error = "signature verification failed" });

            // Stable merchant identity = Digi-ID address. Create on first sign-in —
            // existing merchants' API keys are left intact (so server-to-server
            // integrations keep working across browser sign-ins).
            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.DigiIdAddress == body.Address);
            if (merchant is null)
            {
                var (apiPrefix, _, apiHash) = MerchantAuthenticator.GenerateApiKey();
                merchant = new PayMerchant
                {
                    Id = $"mer_{RandomId(16)}",
                    DisplayName = $"Merchant {body.Address[..8]}…",
                    DigiIdAddress = body.Address,
                    ApiKeyPrefix = apiPrefix,
                    ApiKeyHash = apiHash,
                };
                db.Merchants.Add(merchant);
                // Every merchant starts with a Default Store; dashboards & older session-create
                // calls that omit storeId transparently use it.
                db.Stores.Add(new PayStore
                {
                    Id = $"sto_{RandomId(16)}",
                    MerchantId = merchant.Id,
                    Name = "Default Store",
                    Network = "mainnet",
                });
                await db.SaveChangesAsync();
            }

            // Mint a per-browser session token. Separate from API keys so revoking
            // a browser's access doesn't break SDK/server integrations.
            var (sPrefix, sSecret, sHash) = MerchantAuthenticator.GenerateSessionToken();
            var merchantSession = new MerchantSession
            {
                Id = $"sess_{RandomId(16)}",
                MerchantId = merchant.Id,
                TokenPrefix = sPrefix,
                TokenHash = sHash,
                ExpiresAt = DateTime.UtcNow.Add(MerchantAuthenticator.SessionLifetime),
            };
            db.MerchantSessions.Add(merchantSession);
            await db.SaveChangesAsync();

            entry.Status = ChallengeStatus.Signed;
            entry.MerchantId = merchant.Id;
            entry.SessionApiKey = $"{sPrefix}_{sSecret}";
            entry.DisplayName = merchant.DisplayName;

            return Results.Ok(new { ok = true });
        });

        // DELETE /v1/pay/auth/session — sign out: invalidates the current session token.
        group.MapDelete("/session", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var header = http.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();
            var token = header["Bearer ".Length..].Trim();
            var underscore = token.LastIndexOf('_');
            if (underscore <= 0) return Results.BadRequest();
            var prefix = token[..underscore];
            if (!prefix.StartsWith("dps_", StringComparison.Ordinal)) return Results.BadRequest(new { error = "only session tokens can be signed out" });

            var session = await db.MerchantSessions.FirstOrDefaultAsync(s => s.TokenPrefix == prefix);
            if (session is not null)
            {
                db.MerchantSessions.Remove(session);
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { ok = true });
        });

        // GET /v1/pay/auth/poll/{nonce} — browser polling for the sign-in result.
        group.MapGet("/poll/{nonce}", (string nonce, AuthChallengeStore store) =>
        {
            var entry = store.Get(nonce);
            if (entry is null) return Results.NotFound(new { error = "unknown nonce" });

            return entry.Status switch
            {
                ChallengeStatus.Pending => Results.Accepted(value: new { status = "pending" }),
                ChallengeStatus.Expired => Results.NotFound(new { status = "expired" }),
                ChallengeStatus.Signed => Results.Ok(new
                {
                    status = "signed",
                    merchantId = entry.MerchantId,
                    displayName = entry.DisplayName,
                    token = entry.SessionApiKey,
                }),
                _ => Results.StatusCode(500),
            };
        });

        return group;
    }

    private static string RandomId(int lengthChars)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[lengthChars];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var chars = new char[lengthChars];
        for (int i = 0; i < lengthChars; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}

public record DigiIdCallbackBody(string Address, string Uri, string Signature);

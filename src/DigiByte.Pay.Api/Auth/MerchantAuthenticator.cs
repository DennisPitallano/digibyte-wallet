using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Auth;

/// <summary>
/// Resolves a Bearer token from the Authorization header to a PayMerchant.
/// Accepts two token kinds:
///   dgp_{prefix}_{secret}  — long-lived merchant API key (server-to-server)
///   dps_{prefix}_{secret}  — per-browser session token minted on Digi-ID sign-in
/// The split keeps session tokens from invalidating API keys and vice-versa.
/// </summary>
public static class MerchantAuthenticator
{
    // 30-day session lifetime; bumped to this on every successful use.
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const string CallerItemKey = "digipay.caller";

    public static async Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db) =>
        (await GetCallerAsync(http, db))?.Merchant;

    /// <summary>
    /// Resolves the caller (merchant + API key kind/store/label) once per request and caches
    /// it on HttpContext.Items, so the POS guard and the handler share one lookup.
    /// </summary>
    public static async Task<ApiCaller?> GetCallerAsync(HttpRequest http, DigiPayDbContext db)
    {
        if (http.HttpContext.Items.TryGetValue(CallerItemKey, out var cached)) return cached as ApiCaller;
        var caller = await ResolveCallerAsync(http, db);
        http.HttpContext.Items[CallerItemKey] = caller;
        return caller;
    }

    private static async Task<ApiCaller?> ResolveCallerAsync(HttpRequest http, DigiPayDbContext db)
    {
        var header = http.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        var underscore = token.LastIndexOf('_');
        if (underscore <= 0 || underscore == token.Length - 1) return null;

        var prefix = token[..underscore];
        var secret = token[(underscore + 1)..];
        var hash = HashSecret(secret);

        if (prefix.StartsWith("dps_", StringComparison.Ordinal))
        {
            var merchant = await AuthenticateSessionAsync(prefix, hash, db);
            return merchant is null ? null : new ApiCaller(merchant, null, null, null);
        }
        if (prefix.StartsWith("dgp_", StringComparison.Ordinal))
            return await AuthenticateApiKeyAsync(prefix, hash, db);
        return null;
    }

    private static async Task<PayMerchant?> AuthenticateSessionAsync(string prefix, string hash, DigiPayDbContext db)
    {
        var session = await db.MerchantSessions.FirstOrDefaultAsync(s => s.TokenPrefix == prefix);
        if (session is null) return null;
        var now = DateTime.UtcNow;
        if (now > session.ExpiresAt) return null;
        if (!FixedEquals(session.TokenHash, hash)) return null;

        session.LastUsedAt = now;
        // Sliding window: every successful auth pushes ExpiresAt back out to a full
        // SessionLifetime from now, so an active dashboard never logs the merchant
        // out mid-session. Once the token is more than SessionLifetime stale it has
        // already failed the expiry check above, so this can only extend forward.
        session.ExpiresAt = now + SessionLifetime;
        await db.SaveChangesAsync();
        return await db.Merchants.FirstOrDefaultAsync(m => m.Id == session.MerchantId);
    }

    private static async Task<ApiCaller?> AuthenticateApiKeyAsync(string prefix, string hash, DigiPayDbContext db)
    {
        // Active (non-revoked) keys only. Multiple keys per merchant are allowed;
        // each is independently revocable without disturbing the others.
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix && k.RevokedAt == null);
        if (key is null) return null;
        if (!FixedEquals(key.Hash, hash)) return null;

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == key.MerchantId);
        return merchant is null ? null : new ApiCaller(merchant, key.Kind, key.StoreId, key.Label);
    }

    public static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool FixedEquals(string expectedHex, string providedHex) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(providedHex));

    public static (string Prefix, string Secret, string Hash) GenerateSessionToken()
    {
        var prefix = $"dps_{RandomId(10)}";
        var secret = RandomId(32);
        return (prefix, secret, HashSecret(secret));
    }

    public static (string Prefix, string Secret, string Hash) GenerateApiKey()
    {
        var prefix = GenerateApiKeyPrefix();
        var (secret, hash) = GenerateApiKeySecret();
        return (prefix, secret, hash);
    }

    /// <summary>A fresh "dgp_…" API key prefix (the non-secret half of a key).</summary>
    public static string GenerateApiKeyPrefix() => $"dgp_{RandomId(10)}";

    /// <summary>
    /// A fresh API key secret and its stored hash, for a prefix chosen earlier (POS pairing
    /// fixes the prefix when the code is made but only mints the secret when it is claimed).
    /// </summary>
    public static (string Secret, string Hash) GenerateApiKeySecret()
    {
        var secret = RandomId(32);
        return (secret, HashSecret(secret));
    }

    private static string RandomId(int lengthChars)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[lengthChars];
        for (int i = 0; i < lengthChars; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }
}

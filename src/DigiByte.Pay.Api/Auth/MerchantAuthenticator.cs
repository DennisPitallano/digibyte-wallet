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

    public static async Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db)
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
            return await AuthenticateSessionAsync(prefix, hash, db);
        if (prefix.StartsWith("dgp_", StringComparison.Ordinal))
            return await AuthenticateApiKeyAsync(prefix, hash, db);
        return null;
    }

    private static async Task<PayMerchant?> AuthenticateSessionAsync(string prefix, string hash, DigiPayDbContext db)
    {
        var session = await db.MerchantSessions.FirstOrDefaultAsync(s => s.TokenPrefix == prefix);
        if (session is null) return null;
        if (DateTime.UtcNow > session.ExpiresAt) return null;
        if (!FixedEquals(session.TokenHash, hash)) return null;

        session.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await db.Merchants.FirstOrDefaultAsync(m => m.Id == session.MerchantId);
    }

    private static async Task<PayMerchant?> AuthenticateApiKeyAsync(string prefix, string hash, DigiPayDbContext db)
    {
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.ApiKeyPrefix == prefix);
        if (merchant is null) return null;
        return FixedEquals(merchant.ApiKeyHash, hash) ? merchant : null;
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
        var prefix = $"dgp_{RandomId(10)}";
        var secret = RandomId(32);
        return (prefix, secret, HashSecret(secret));
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

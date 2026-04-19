using System.Security.Cryptography;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Merchant-owned API key lifecycle. All routes require a Bearer token that
/// resolves to the merchant (session token or another API key — API keys can
/// create/revoke peer keys, useful for automation).
///
/// The raw secret is only returned from POST (create). List returns the
/// prefix + metadata only; the hash never leaves the server.
/// </summary>
public static class ApiKeysEndpoints
{
    public static RouteGroupBuilder MapApiKeysEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var keys = await db.ApiKeys.AsNoTracking()
                .Where(k => k.MerchantId == merchant.Id)
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync();

            return Results.Ok(keys.Select(ToListDto));
        });

        group.MapPost("", async (CreateApiKeyRequest body, HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var label = string.IsNullOrWhiteSpace(body.Label) ? null : body.Label.Trim();
            if (label is not null && label.Length > 80)
                return Results.BadRequest(new { error = "label must be 1-80 chars" });

            var (prefix, secret, hash) = MerchantAuthenticator.GenerateApiKey();
            var key = new PayApiKey
            {
                Id = $"key_{RandomId(16)}",
                MerchantId = merchant.Id,
                Prefix = prefix,
                Hash = hash,
                Label = label,
            };
            db.ApiKeys.Add(key);
            await db.SaveChangesAsync();

            // Secret is returned ONCE here; the client is responsible for storing it.
            return Results.Ok(new
            {
                id = key.Id,
                label = key.Label,
                prefix = key.Prefix,
                createdAt = key.CreatedAt,
                apiKey = $"{prefix}_{secret}",
            });
        });

        group.MapDelete("/{id}", async (string id, HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.MerchantId == merchant.Id);
            if (key is null) return Results.NotFound(new { error = "api key not found" });
            if (key.RevokedAt is not null)
                return Results.Ok(new { ok = true, alreadyRevoked = true });

            key.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        return group;
    }

    private static object ToListDto(PayApiKey k) => new
    {
        k.Id,
        k.Label,
        k.Prefix,
        k.CreatedAt,
        k.LastUsedAt,
        k.RevokedAt,
        IsRevoked = k.RevokedAt is not null,
    };

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

public record CreateApiKeyRequest(string? Label);

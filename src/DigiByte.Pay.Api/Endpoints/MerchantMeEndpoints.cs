using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Self-service endpoints for the currently-authenticated merchant (Bearer token).
/// Used by the DigiPay dashboard — kept separate from the raw PaymentsEndpoints so
/// the "SDK / API consumer" surface and the "dashboard UI" surface can evolve
/// independently (e.g., different rate limits, docs, auth modes later).
/// </summary>
public static class MerchantMeEndpoints
{
    public static RouteGroupBuilder MapMerchantMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            return merchant is null ? Results.Unauthorized() : Results.Ok(ToDto(merchant));
        });

        group.MapPatch("", async (
            UpdateMeRequest body,
            HttpRequest http,
            DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            if (body.DisplayName is not null)
            {
                var trimmed = body.DisplayName.Trim();
                if (trimmed.Length == 0 || trimmed.Length > 120)
                    return Results.BadRequest(new { error = "displayName must be 1-120 chars" });
                merchant.DisplayName = trimmed;
            }

            if (body.Network is not null)
            {
                var net = body.Network.ToLowerInvariant();
                if (net is not ("mainnet" or "testnet" or "regtest"))
                    return Results.BadRequest(new { error = "network must be mainnet | testnet | regtest" });
                merchant.Network = net;
            }

            if (body.AddressOrXpub is not null)
            {
                var trimmed = body.AddressOrXpub.Trim();
                if (trimmed.Length == 0)
                {
                    merchant.Xpub = null;
                    merchant.ReceiveAddress = null;
                    merchant.NextAddressIndex = 0;
                }
                else
                {
                    var kind = MerchantAddressService.Classify(trimmed, merchant.Network, out var err);
                    if (kind is null) return Results.BadRequest(new { error = err });
                    if (kind == MerchantAddressService.MerchantKeyKind.Xpub)
                    {
                        merchant.Xpub = trimmed;
                        merchant.ReceiveAddress = null;
                        merchant.NextAddressIndex = 0;
                    }
                    else
                    {
                        merchant.ReceiveAddress = trimmed;
                        merchant.Xpub = null;
                    }
                }
            }

            if (body.WebhookUrl is not null)
            {
                var trimmed = body.WebhookUrl.Trim();
                if (trimmed.Length == 0)
                {
                    merchant.WebhookUrl = null;
                    merchant.WebhookSecret = null;
                }
                else
                {
                    merchant.WebhookUrl = trimmed;
                    if (string.IsNullOrEmpty(merchant.WebhookSecret))
                    {
                        Span<byte> bytes = stackalloc byte[24];
                        RandomNumberGenerator.Fill(bytes);
                        merchant.WebhookSecret = Convert.ToHexString(bytes).ToLowerInvariant();
                    }
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(merchant));
        });

        return group;
    }

    private static object ToDto(PayMerchant m) => new
    {
        m.Id,
        m.DisplayName,
        m.DigiIdAddress,
        m.Network,
        ReceiveMode = !string.IsNullOrEmpty(m.Xpub) ? "xpub"
            : !string.IsNullOrEmpty(m.ReceiveAddress) ? "address"
            : "unconfigured",
        HasReceive = !string.IsNullOrEmpty(m.Xpub) || !string.IsNullOrEmpty(m.ReceiveAddress),
        // Xpub/address snippets for the UI — truncated for display only.
        ReceivePreview = !string.IsNullOrEmpty(m.Xpub) ? $"{m.Xpub[..12]}…"
            : !string.IsNullOrEmpty(m.ReceiveAddress) ? m.ReceiveAddress
            : null,
        m.WebhookUrl,
        HasWebhookSecret = !string.IsNullOrEmpty(m.WebhookSecret),
        m.CreatedAt,
    };

    // Copy of the auth helper from PaymentsEndpoints. Kept duplicated for now —
    // once Digi-ID session tokens and API keys diverge we'll extract a shared authenticator.
    internal static async Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db)
    {
        var header = http.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        var underscore = token.LastIndexOf('_');
        if (underscore <= 0 || underscore == token.Length - 1) return null;
        var prefix = token[..underscore];
        var secret = token[(underscore + 1)..];
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.ApiKeyPrefix == prefix);
        if (merchant is null) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash),
            Encoding.ASCII.GetBytes(merchant.ApiKeyHash)) ? merchant : null;
    }
}

public record UpdateMeRequest(string? DisplayName, string? Network, string? AddressOrXpub, string? WebhookUrl);

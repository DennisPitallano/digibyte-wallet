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

        // A stable address for donation / tip buttons.
        // - Address mode: the merchant's single configured address.
        // - Xpub mode: index 0 of the receive chain. Reused across all tips (that's the
        //   tradeoff of a static button) but never collides with a tracked invoice,
        //   because invoice sessions allocate from NextAddressIndex which we bump past 0
        //   when this is first asked for.
        group.MapGet("/donation-address", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
            if (!string.IsNullOrEmpty(merchant.ReceiveAddress))
                return Results.Ok(new { address = merchant.ReceiveAddress, mode = "address" });
            if (!string.IsNullOrEmpty(merchant.Xpub))
            {
                var address = MerchantAddressService.DeriveAddress(merchant.Xpub, merchant.Network, 0);
                if (merchant.NextAddressIndex == 0)
                {
                    merchant.NextAddressIndex = 1; // reserve index 0 for the donation button
                    await db.SaveChangesAsync();
                }
                return Results.Ok(new { address, mode = "xpub" });
            }
            return Results.BadRequest(new { error = "merchant has no receive configured" });
        });

        // Fire a synthetic "webhook.test" event at the merchant's configured WebhookUrl so
        // they can verify their handler + HMAC verification without waiting for a real payment.
        group.MapPost("/webhook/test", async (
            HttpRequest http,
            DigiPayDbContext db,
            Services.WebhookDispatcher dispatcher) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(merchant.WebhookUrl))
                return Results.BadRequest(new { error = "merchant has no webhookUrl configured" });

            var dummy = new PaySession
            {
                Id = "ses_test_" + Guid.NewGuid().ToString("N")[..12],
                MerchantId = merchant.Id,
                AddressIndex = 0,
                Address = merchant.ReceiveAddress ?? "dgb1qtest",
                AmountSatoshis = 100_000_000,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Label = "test",
            };
            await dispatcher.DispatchAsync(merchant, dummy, "webhook.test");
            return Results.Ok(new { ok = true, webhookUrl = merchant.WebhookUrl });
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

            if (body.DefaultSessionExpiryMinutes is not null)
            {
                var mins = body.DefaultSessionExpiryMinutes.Value;
                if (mins is < 1 or > 24 * 60)
                    return Results.BadRequest(new { error = "defaultSessionExpiryMinutes must be 1-1440" });
                merchant.DefaultSessionExpiryMinutes = mins;
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
        m.DefaultSessionExpiryMinutes,
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

public record UpdateMeRequest(
    string? DisplayName,
    string? Network,
    string? AddressOrXpub,
    string? WebhookUrl,
    int? DefaultSessionExpiryMinutes);

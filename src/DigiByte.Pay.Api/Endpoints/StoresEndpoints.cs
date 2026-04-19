using System.Security.Cryptography;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// CRUD + store-scoped helpers (donation address, webhook test). Everything here
/// requires a Bearer token that resolves to the store's owning merchant, and the
/// store id in the path is verified to belong to that merchant.
/// </summary>
public static class StoresEndpoints
{
    public static RouteGroupBuilder MapStoresEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
            var stores = await db.Stores.AsNoTracking()
                .Where(s => s.MerchantId == merchant.Id)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();
            return Results.Ok(stores.Select(MerchantMeEndpoints.StoreDto));
        });

        group.MapPost("", async (CreateStoreRequest body, HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });

            var network = string.IsNullOrWhiteSpace(body.Network) ? "mainnet" : body.Network.ToLowerInvariant();
            if (network is not ("mainnet" or "testnet" or "regtest"))
                return Results.BadRequest(new { error = "network must be mainnet | testnet | regtest" });

            var store = new PayStore
            {
                Id = $"sto_{RandomId(16)}",
                MerchantId = merchant.Id,
                Name = body.Name.Trim(),
                Network = network,
            };
            db.Stores.Add(store);
            await db.SaveChangesAsync();
            return Results.Ok(MerchantMeEndpoints.StoreDto(store));
        });

        group.MapGet("/{storeId}", async (string storeId, HttpRequest http, DigiPayDbContext db) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;
            return Results.Ok(MerchantMeEndpoints.StoreDto(store!));
        });

        group.MapPatch("/{storeId}", async (
            string storeId, UpdateStoreRequest body, HttpRequest http, DigiPayDbContext db) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db, tracked: true);
            if (err is not null) return err;

            if (body.Name is not null)
            {
                var trimmed = body.Name.Trim();
                if (trimmed.Length == 0 || trimmed.Length > 120)
                    return Results.BadRequest(new { error = "name must be 1-120 chars" });
                store!.Name = trimmed;
            }
            if (body.Network is not null)
            {
                var net = body.Network.ToLowerInvariant();
                if (net is not ("mainnet" or "testnet" or "regtest"))
                    return Results.BadRequest(new { error = "network must be mainnet | testnet | regtest" });
                store!.Network = net;
            }
            if (body.AddressOrXpub is not null)
            {
                var trimmed = body.AddressOrXpub.Trim();
                if (trimmed.Length == 0)
                {
                    store!.Xpub = null;
                    store.ReceiveAddress = null;
                    store.NextAddressIndex = 0;
                }
                else
                {
                    var kind = MerchantAddressService.Classify(trimmed, store!.Network, out var classifyError);
                    if (kind is null) return Results.BadRequest(new { error = classifyError });
                    if (kind == MerchantAddressService.MerchantKeyKind.Xpub)
                    {
                        store.Xpub = trimmed;
                        store.ReceiveAddress = null;
                        store.NextAddressIndex = 0;
                    }
                    else
                    {
                        store.ReceiveAddress = trimmed;
                        store.Xpub = null;
                    }
                }
            }
            if (body.WebhookUrl is not null)
            {
                var trimmed = body.WebhookUrl.Trim();
                if (trimmed.Length == 0)
                {
                    store!.WebhookUrl = null;
                    store.WebhookSecret = null;
                }
                else
                {
                    store!.WebhookUrl = trimmed;
                    if (string.IsNullOrEmpty(store.WebhookSecret))
                    {
                        Span<byte> bytes = stackalloc byte[24];
                        RandomNumberGenerator.Fill(bytes);
                        store.WebhookSecret = Convert.ToHexString(bytes).ToLowerInvariant();
                    }
                }
            }
            if (body.DefaultSessionExpiryMinutes is not null)
            {
                var mins = body.DefaultSessionExpiryMinutes.Value;
                if (mins is < 1 or > 24 * 60)
                    return Results.BadRequest(new { error = "defaultSessionExpiryMinutes must be 1-1440" });
                store!.DefaultSessionExpiryMinutes = mins;
            }

            await db.SaveChangesAsync();
            return Results.Ok(MerchantMeEndpoints.StoreDto(store!));
        });

        group.MapDelete("/{storeId}", async (string storeId, HttpRequest http, DigiPayDbContext db) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db, tracked: true);
            if (err is not null) return err;
            // Guardrail: don't orphan the account — force at least one store to remain.
            var count = await db.Stores.CountAsync(s => s.MerchantId == merchant!.Id);
            if (count <= 1) return Results.BadRequest(new { error = "cannot delete your only store" });
            db.Stores.Remove(store!);
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/{storeId}/donation-address", async (string storeId, HttpRequest http, DigiPayDbContext db) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db, tracked: true);
            if (err is not null) return err;

            if (!string.IsNullOrEmpty(store!.ReceiveAddress))
                return Results.Ok(new { address = store.ReceiveAddress, mode = "address" });
            if (!string.IsNullOrEmpty(store.Xpub))
            {
                var address = MerchantAddressService.DeriveAddress(store.Xpub, store.Network, 0);
                if (store.NextAddressIndex == 0)
                {
                    store.NextAddressIndex = 1; // reserve 0 for donation
                    await db.SaveChangesAsync();
                }
                return Results.Ok(new { address, mode = "xpub" });
            }
            return Results.BadRequest(new { error = "store has no receive configured" });
        });

        group.MapPost("/{storeId}/webhook/test", async (
            string storeId, HttpRequest http, DigiPayDbContext db, WebhookDispatcher dispatcher) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;
            if (string.IsNullOrWhiteSpace(store!.WebhookUrl))
                return Results.BadRequest(new { error = "store has no webhookUrl configured" });

            var dummy = new PaySession
            {
                Id = "ses_test_" + Guid.NewGuid().ToString("N")[..12],
                MerchantId = merchant!.Id,
                StoreId = store.Id,
                AddressIndex = 0,
                Address = store.ReceiveAddress ?? "dgb1qtest",
                AmountSatoshis = 100_000_000,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                Label = "test",
            };
            await dispatcher.DispatchAsync(store, dummy, "webhook.test");
            return Results.Ok(new { ok = true, webhookUrl = store.WebhookUrl });
        });

        return group;
    }

    private static async Task<(PayMerchant? Merchant, PayStore? Store, IResult? Error)> LoadOwnedAsync(
        string storeId, HttpRequest http, DigiPayDbContext db, bool tracked = false)
    {
        var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
        if (merchant is null) return (null, null, Results.Unauthorized());
        var q = tracked ? db.Stores : db.Stores.AsNoTracking();
        var store = await q.FirstOrDefaultAsync(s => s.Id == storeId);
        if (store is null) return (merchant, null, Results.NotFound(new { error = "store not found" }));
        if (store.MerchantId != merchant.Id) return (merchant, null, Results.NotFound(new { error = "store not found" }));
        return (merchant, store, null);
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

public record CreateStoreRequest(string Name, string? Network);
public record UpdateStoreRequest(
    string? Name,
    string? Network,
    string? AddressOrXpub,
    string? WebhookUrl,
    int? DefaultSessionExpiryMinutes);

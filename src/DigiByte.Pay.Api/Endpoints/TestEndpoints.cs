using System.Security.Cryptography;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Hubs;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Public sandbox endpoints. Used by the embed demo page's "Full simulation"
/// section to walk a checkout through its full lifecycle without needing a
/// real chain transaction.
///
/// These bypass the normal auth model by design — they're the public demo.
/// Safety rails:
///   • /demo-session always creates a brand-new merchant/store/session tagged
///     with *_demo_* ids, so nothing it creates can collide with real data.
///   • /advance refuses any id that doesn't start with ses_demo_ — real
///     merchant sessions can never be flipped through this endpoint.
///   • PublicEndpoints filters ses_demo_* out of any public listings.
/// </summary>
public static class TestEndpoints
{
    public static RouteGroupBuilder MapTestEndpoints(this RouteGroupBuilder group)
    {
        // POST /v1/pay/test/demo-session — create a throwaway merchant + store +
        // session in one shot so the demo page can drive a real UI end-to-end
        // without credentials or a webhook to ignore.
        group.MapPost("/demo-session", async (DigiPayDbContext db) =>
        {
            var merchantId = $"mer_demo_{RandomId(10)}";
            var (keyPrefix, _, keyHash) = MerchantAuthenticator.GenerateApiKey();
            var merchant = new PayMerchant
            {
                Id = merchantId,
                DisplayName = "Demo Store",
                ApiKeyPrefix = keyPrefix,
                ApiKeyHash = keyHash,
            };
            db.Merchants.Add(merchant);

            var storeId = $"sto_demo_{RandomId(10)}";
            db.Stores.Add(new PayStore
            {
                Id = storeId,
                MerchantId = merchantId,
                Name = "Demo Store",
                Network = "mainnet",
                // Throwaway receive — the simulation never sends real funds.
                ReceiveAddress = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa",
            });

            var sessionId = $"ses_demo_{RandomId(12)}";
            db.Sessions.Add(new PaySession
            {
                Id = sessionId,
                MerchantId = merchantId,
                StoreId = storeId,
                AddressIndex = 0,
                Address = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa",
                AmountSatoshis = 500_000_000, // 5 DGB
                Label = "Demo order — simulated payment",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            });
            await db.SaveChangesAsync();

            return Results.Ok(new { sessionId, storeId, merchantId });
        });

        // POST /v1/pay/test/sessions/{id}/advance — move the session one state
        // forward through pending → seen → paid → confirmed. Each click simulates
        // the next chain event without needing a real tx. Pushes via SignalR so
        // the already-open checkout tab updates live.
        group.MapPost("/sessions/{id}/advance", async (
            string id, DigiPayDbContext db, CheckoutNotifier notifier) =>
        {
            // Hard safety rail: this unauthenticated endpoint must never be able
            // to mutate a real merchant's session. Demo sessions always carry
            // the ses_demo_ prefix (see /demo-session above).
            if (!id.StartsWith("ses_demo_", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "advance is only valid for demo sessions" });

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id);
            if (session is null) return Results.NotFound();

            var now = DateTime.UtcNow;
            switch (session.Status)
            {
                case PaySessionStatus.Pending:
                    session.Status = PaySessionStatus.Seen;
                    session.SeenAt ??= now;
                    session.ReceivedSatoshis = session.AmountSatoshis;
                    break;
                case PaySessionStatus.Seen:
                    session.Status = PaySessionStatus.Paid;
                    session.PaidAt ??= now;
                    session.Confirmations = 1;
                    session.PaidTxid ??= $"demo{RandomId(56)}";
                    break;
                case PaySessionStatus.Paid:
                    session.Status = PaySessionStatus.Confirmed;
                    session.Confirmations = 6;
                    break;
                case PaySessionStatus.Confirmed:
                case PaySessionStatus.Expired:
                case PaySessionStatus.Underpaid:
                    // Terminal — nothing to do. Return current state so the client
                    // can reconcile if it's somehow fallen behind.
                    break;
            }
            await db.SaveChangesAsync();

            await notifier.PublishAsync(new CheckoutStatusUpdate
            {
                SessionId = session.Id,
                Status = session.Status.ToString().ToLowerInvariant(),
                ReceivedSatoshis = session.ReceivedSatoshis,
                Confirmations = session.Confirmations,
                Txid = session.PaidTxid,
            });

            return Results.Ok(new
            {
                id = session.Id,
                status = session.Status.ToString().ToLowerInvariant(),
                session.Confirmations,
                session.ReceivedSatoshis,
                session.PaidTxid,
            });
        });

        return group;
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

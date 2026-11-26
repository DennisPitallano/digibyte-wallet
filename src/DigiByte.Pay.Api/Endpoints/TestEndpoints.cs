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
///   • /advance only flips a non-demo session for its authenticated owner,
///     and only on a testnet/regtest store — mainnet sessions can never be
///     flipped through this endpoint.
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

        // POST /v1/pay/test/sessions/{id}/advance — move the session through
        // pending → seen → paid → confirmed without needing a real tx. Pushes via
        // SignalR so the already-open checkout tab updates live.
        //
        //   • ses_demo_* sessions: unauthenticated, one step per call, no webhooks
        //     (the public embed demo; behaviour unchanged).
        //   • Any other session: needs the owning merchant's token (or a POS key for
        //     its store) and a testnet/regtest store. Optional body
        //     { "to": "seen"|"paid"|"confirmed", "confirmations": n } jumps or sets the
        //     count; webhooks fire like a real payment (see AdvanceTestSessionAsync).
        group.MapPost("/sessions/{id}/advance", async (
            string id, HttpRequest http, DigiPayDbContext db, CheckoutNotifier notifier, WebhookDispatcher dispatcher) =>
        {
            if (id.StartsWith("ses_demo_", StringComparison.Ordinal))
                return await AdvanceDemoAsync(id, db, notifier);
            return await AdvanceTestSessionAsync(id, http, db, notifier, dispatcher);
        }).AllowPosKey(); // checks the tablet's store itself

        return group;
    }

    // Demo sessions: one step forward per call, no webhooks (they have none configured).
    private static async Task<IResult> AdvanceDemoAsync(string id, DigiPayDbContext db, CheckoutNotifier notifier)
    {
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
        await PublishAsync(notifier, session);
        return AdvanceResult(session);
    }

    // Matches InvoiceMonitor: 1 confirmation = paid, 6 = confirmed.
    private const int ConfirmedAt = 6;

    /// <summary>
    /// Sandbox payment for a real session on a testnet/regtest store. Sets the same fields
    /// InvoiceMonitor sets from the chain (received amount, confirmations, a placeholder
    /// txid), publishes the checkout update and dispatches the webhook InvoiceMonitor would
    /// for the new status (session.seen / session.paid / session.confirmed; one event per
    /// status change, nothing when only the count changes). InvoiceMonitor writes no audit
    /// rows for these transitions, so neither does this. The session is flagged
    /// <see cref="PaySession.Simulated"/> so InvoiceMonitor stops watching it — otherwise the
    /// next tick would find no payment on chain and expire it.
    /// </summary>
    private static async Task<IResult> AdvanceTestSessionAsync(
        string id, HttpRequest http, DigiPayDbContext db, CheckoutNotifier notifier, WebhookDispatcher dispatcher)
    {
        var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
        if (caller is null)
            return Results.Json(new { error = "sign in or send your API key to advance this session" }, statusCode: StatusCodes.Status401Unauthorized);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == caller.Merchant.Id);
        if (session is null) return Results.NotFound(new { error = "session not found" });
        if (!caller.CanUseStore(session.StoreId)) return ApiCaller.OutsideStore();

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId);
        if (store is null) return Results.NotFound(new { error = "session not found" });
        if (store.Network is not ("testnet" or "regtest"))
            return Results.BadRequest(new { error = "advance only works on testnet or regtest stores" });

        AdvanceRequest? body;
        try { body = await ReadBodyAsync(http); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "body must be JSON like { \"to\": \"paid\", \"confirmations\": 3 }" }); }

        if (session.Status is not (PaySessionStatus.Pending or PaySessionStatus.Seen
            or PaySessionStatus.Paid or PaySessionStatus.Confirmed))
            return Results.Conflict(new { error = $"session is {session.Status.ToString().ToLowerInvariant()} and can't be advanced" });

        // Work out the target status and confirmation count.
        PaySessionStatus target;
        if (body?.To is { } to && !string.IsNullOrWhiteSpace(to))
        {
            target = to.Trim().ToLowerInvariant() switch
            {
                "seen" => PaySessionStatus.Seen,
                "paid" => PaySessionStatus.Paid,
                "confirmed" => PaySessionStatus.Confirmed,
                _ => PaySessionStatus.Pending, // invalid marker
            };
            if (target == PaySessionStatus.Pending)
                return Results.BadRequest(new { error = "to must be seen, paid or confirmed" });
        }
        else if (body?.Confirmations is int n)
        {
            if (n < 0) return Results.BadRequest(new { error = "confirmations can't be negative" });
            target = n == 0 ? PaySessionStatus.Seen : n < ConfirmedAt ? PaySessionStatus.Paid : PaySessionStatus.Confirmed;
        }
        else
        {
            // No body: one step forward, like the demo. Confirmed stays confirmed.
            target = session.Status switch
            {
                PaySessionStatus.Pending => PaySessionStatus.Seen,
                PaySessionStatus.Seen => PaySessionStatus.Paid,
                _ => PaySessionStatus.Confirmed,
            };
        }

        var confirmations = body?.Confirmations ?? target switch
        {
            PaySessionStatus.Seen => 0,
            PaySessionStatus.Paid => Math.Clamp(session.Confirmations, 1, ConfirmedAt - 1),
            _ => Math.Max(session.Confirmations, ConfirmedAt),
        };
        var countError = target switch
        {
            PaySessionStatus.Seen when confirmations != 0 => "a seen payment has 0 confirmations",
            PaySessionStatus.Paid when confirmations is < 1 or >= ConfirmedAt => $"a paid session has 1 to {ConfirmedAt - 1} confirmations",
            PaySessionStatus.Confirmed when confirmations < ConfirmedAt => $"a confirmed session has at least {ConfirmedAt} confirmations",
            _ when confirmations > 10_000 => "confirmations must be 10000 or fewer",
            _ => null,
        };
        if (countError is not null) return Results.BadRequest(new { error = countError });
        if (target < session.Status)
            return Results.Conflict(new { error = $"session is already {session.Status.ToString().ToLowerInvariant()} and can't move back to {target.ToString().ToLowerInvariant()}" });

        var previous = session.Status;
        var previousConfirmations = session.Confirmations;
        var now = DateTime.UtcNow;
        session.Status = target;
        session.Simulated = true;
        session.SeenAt ??= now;
        session.ReceivedSatoshis = session.Currency == "DD" ? session.AmountCentsDd ?? 0 : session.AmountSatoshis;
        session.Confirmations = confirmations;
        // InvoiceMonitor records the txid as soon as the payment is seen; this one is
        // plainly not a real transaction.
        session.PaidTxid ??= $"simulated{RandomHex(55)}";
        if (target is PaySessionStatus.Paid or PaySessionStatus.Confirmed) session.PaidAt ??= now;
        await db.SaveChangesAsync();

        if (session.Status != previous || session.Confirmations != previousConfirmations)
            await PublishAsync(notifier, session);
        if (session.Status != previous)
            await dispatcher.DispatchAsync(store, session, "session." + session.Status.ToString().ToLowerInvariant());

        return AdvanceResult(session);
    }

    // Optional body; an empty request is fine. Read by hand so a body-less POST from the
    // demo page never trips JSON binding.
    private static async Task<AdvanceRequest?> ReadBodyAsync(HttpRequest http)
    {
        using var reader = new StreamReader(http.Body);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return System.Text.Json.JsonSerializer.Deserialize<AdvanceRequest>(text,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    private static Task PublishAsync(CheckoutNotifier notifier, PaySession session) =>
        notifier.PublishAsync(new CheckoutStatusUpdate
        {
            SessionId = session.Id,
            Status = session.Status.ToString().ToLowerInvariant(),
            ReceivedSatoshis = session.ReceivedSatoshis,
            Confirmations = session.Confirmations,
            Txid = session.PaidTxid,
        });

    private static IResult AdvanceResult(PaySession session) => Results.Ok(new
    {
        id = session.Id,
        status = session.Status.ToString().ToLowerInvariant(),
        session.Confirmations,
        session.ReceivedSatoshis,
        session.PaidTxid,
    });

    private static string RandomHex(int lengthChars)
    {
        const string alphabet = "0123456789abcdef";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[lengthChars];
        for (int i = 0; i < lengthChars; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
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

/// <summary>Optional body for POST /v1/pay/test/sessions/{id}/advance on a testnet/regtest session.</summary>
public record AdvanceRequest(string? To, int? Confirmations);

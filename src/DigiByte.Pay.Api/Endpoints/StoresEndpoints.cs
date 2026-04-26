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

        group.MapPost("", async (CreateStoreRequest body, HttpRequest http, DigiPayDbContext db, AuditLogger audit) =>
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

            await audit.LogAsync(merchant.Id, "store.create", "Store", store.Id,
                summary: $"Created store \"{store.Name}\" ({network})",
                metadata: new { name = store.Name, network });

            return Results.Ok(MerchantMeEndpoints.StoreDto(store));
        });

        group.MapGet("/{storeId}", async (string storeId, HttpRequest http, DigiPayDbContext db) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;
            return Results.Ok(MerchantMeEndpoints.StoreDto(store!));
        });

        group.MapPatch("/{storeId}", async (
            string storeId, UpdateStoreRequest body, HttpRequest http, DigiPayDbContext db, AuditLogger audit) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db, tracked: true);
            if (err is not null) return err;

            var changed = new List<string>();
            if (body.Name is not null)
            {
                var trimmed = body.Name.Trim();
                if (trimmed.Length == 0 || trimmed.Length > 120)
                    return Results.BadRequest(new { error = "name must be 1-120 chars" });
                if (store!.Name != trimmed) changed.Add("name");
                store!.Name = trimmed;
            }
            if (body.Network is not null)
            {
                var net = body.Network.ToLowerInvariant();
                if (net is not ("mainnet" or "testnet" or "regtest"))
                    return Results.BadRequest(new { error = "network must be mainnet | testnet | regtest" });
                if (store!.Network != net) changed.Add("network");
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
                changed.Add("receive");
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
                    if (trimmed.Length > 2048)
                        return Results.BadRequest(new { error = "webhookUrl must be 2048 chars or fewer" });
                    if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                        return Results.BadRequest(new { error = "webhookUrl must be a valid absolute URL" });
                    var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
                    var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
                    var isLocalHttp = isHttp && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
                    if (!isHttps && !isLocalHttp)
                        return Results.BadRequest(new { error = "webhookUrl must use https (http allowed only for localhost)" });

                    store!.WebhookUrl = trimmed;
                    if (string.IsNullOrEmpty(store.WebhookSecret))
                    {
                        Span<byte> bytes = stackalloc byte[24];
                        RandomNumberGenerator.Fill(bytes);
                        store.WebhookSecret = Convert.ToHexString(bytes).ToLowerInvariant();
                    }
                }
                changed.Add("webhook");
            }
            if (body.DefaultSessionExpiryMinutes is not null)
            {
                var mins = body.DefaultSessionExpiryMinutes.Value;
                if (mins is < 1 or > 24 * 60)
                    return Results.BadRequest(new { error = "defaultSessionExpiryMinutes must be 1-1440" });
                if (store!.DefaultSessionExpiryMinutes != mins) changed.Add("expiry");
                store!.DefaultSessionExpiryMinutes = mins;
            }

            await db.SaveChangesAsync();

            if (changed.Count > 0)
            {
                await audit.LogAsync(merchant!.Id, "store.update", "Store", store!.Id,
                    summary: $"Updated store \"{store.Name}\" ({string.Join(", ", changed)})",
                    metadata: new { fields = changed });
            }

            return Results.Ok(MerchantMeEndpoints.StoreDto(store!));
        });

        group.MapDelete("/{storeId}", async (string storeId, HttpRequest http, DigiPayDbContext db, AuditLogger audit) =>
        {
            var (merchant, store, err) = await LoadOwnedAsync(storeId, http, db, tracked: true);
            if (err is not null) return err;
            // Guardrail: don't orphan the account — force at least one store to remain.
            var count = await db.Stores.CountAsync(s => s.MerchantId == merchant!.Id);
            if (count <= 1) return Results.BadRequest(new { error = "cannot delete your only store" });
            var snapshotName = store!.Name;
            var snapshotNetwork = store.Network;
            db.Stores.Remove(store!);
            await db.SaveChangesAsync();

            await audit.LogAsync(merchant!.Id, "store.delete", "Store", storeId,
                summary: $"Deleted store \"{snapshotName}\"",
                metadata: new { name = snapshotName, network = snapshotNetwork });

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
            var delivery = await dispatcher.DispatchAsync(store, dummy, "webhook.test");
            return Results.Ok(new
            {
                ok = true,
                webhookUrl = store.WebhookUrl,
                deliveryId = delivery?.Id,
                statusCode = delivery?.StatusCode,
                error = delivery?.ErrorMessage,
            });
        });

        // GET /v1/pay/stores/{storeId}/webhook-deliveries — recent delivery attempts.
        // Returned newest-first; default 25 rows to match sessions pagination.
        // Optional ?sessionId= filter narrows to a single session's delivery chain
        // (including manual replays, which carry the same SessionId with incremented Attempt).
        // ?format=csv returns a downloadable spreadsheet (cap 10 000 rows).
        group.MapGet("/{storeId}/webhook-deliveries", async (
            string storeId, HttpRequest http, DigiPayDbContext db,
            int take = 25, string? sessionId = null, string? format = null) =>
        {
            var (_, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;

            var wantsCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            take = wantsCsv ? Math.Clamp(take, 1, 10_000) : Math.Clamp(take, 1, 100);
            var query = db.WebhookDeliveries.AsNoTracking()
                .Where(d => d.StoreId == store!.Id);
            if (!string.IsNullOrWhiteSpace(sessionId))
                query = query.Where(d => d.SessionId == sessionId);
            var rows = await query
                .OrderByDescending(d => d.CreatedAt)
                .Take(take)
                .ToListAsync();

            if (wantsCsv)
            {
                // Same column shape as DeliveryDto, sans the response snippet
                // (which is often binary-ish HTML and breaks spreadsheets) —
                // we keep just its length so merchants can spot suspiciously
                // empty receivers without having to hand-edit the CSV.
                var sb = new System.Text.StringBuilder(64 + rows.Count * 192);
                CsvWriter.WriteRow(sb, new object?[]
                {
                    "id", "sessionId", "eventName", "url", "attempt",
                    "statusCode", "success", "errorMessage", "durationMs",
                    "responseSnippetLength", "createdAt", "deliveredAt",
                });
                foreach (var d in rows)
                {
                    var success = d.StatusCode is >= 200 and < 300;
                    CsvWriter.WriteRow(sb, new object?[]
                    {
                        d.Id, d.SessionId, d.EventName, d.Url, d.Attempt,
                        d.StatusCode, success, d.ErrorMessage, d.DurationMs,
                        d.ResponseSnippet?.Length ?? 0, d.CreatedAt, d.DeliveredAt,
                    });
                }
                var filename = $"digipay-deliveries-{store!.Id}-{DateTime.UtcNow:yyyyMMdd}.csv";
                return Results.File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                    contentType: "text/csv; charset=utf-8",
                    fileDownloadName: filename);
            }

            return Results.Ok(rows.Select(DeliveryDto));
        });

        // POST /v1/pay/stores/{storeId}/webhook-deliveries/{deliveryId}/replay
        // Re-fires a previous delivery using the same event + session payload.
        // Creates a new row with Attempt incremented so the audit trail is preserved.
        group.MapPost("/{storeId}/webhook-deliveries/{deliveryId}/replay", async (
            string storeId, string deliveryId,
            HttpRequest http, DigiPayDbContext db, WebhookDispatcher dispatcher) =>
        {
            var (_, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;
            if (string.IsNullOrWhiteSpace(store!.WebhookUrl))
                return Results.BadRequest(new { error = "store has no webhookUrl configured" });

            var prev = await db.WebhookDeliveries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deliveryId && d.StoreId == store.Id);
            if (prev is null) return Results.NotFound(new { error = "delivery not found" });

            // Synthetic events (webhook.test) don't persist a session — nothing to replay.
            if (string.IsNullOrEmpty(prev.SessionId))
                return Results.BadRequest(new { error = "test deliveries can't be replayed — fire a fresh test instead" });

            var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == prev.SessionId);
            if (session is null)
                return Results.BadRequest(new { error = "original session no longer exists" });

            var fresh = await dispatcher.ReplayAsync(prev, store, session);
            return Results.Ok(DeliveryDto(fresh!));
        });

        // GET /v1/pay/stores/{storeId}/analytics?days=30
        // Aggregated sales data for the dashboard chart. Returns one bucket per
        // UTC day for the requested window (default 30, max 365). We count both
        // sessions and confirmed DGB so the front-end can toggle between volume
        // (session count) and revenue (DGB) with one payload.
        //
        // Why server-side: the sessions list is paginated/capped at 10k, so a
        // naïve client-side rollup would miss older data. Pushing the GROUP BY
        // to Postgres keeps the response tiny and accurate.
        group.MapGet("/{storeId}/analytics", async (
            string storeId, HttpRequest http, DigiPayDbContext db, int days = 30) =>
        {
            var (_, store, err) = await LoadOwnedAsync(storeId, http, db);
            if (err is not null) return err;

            days = Math.Clamp(days, 1, 365);
            var now = DateTime.UtcNow;
            var since = now.Date.AddDays(-(days - 1));

            // Pull every session in window once; aggregate in-memory. For a
            // typical store this is hundreds of rows — cheap, and avoids
            // provider-specific date truncation SQL.
            var rows = await db.Sessions.AsNoTracking()
                .Where(s => s.StoreId == store!.Id && s.CreatedAt >= since)
                .Select(s => new { s.CreatedAt, s.Status, s.AmountSatoshis })
                .ToListAsync();

            // Dense series: emit every day in range even when zero so the chart
            // draws a continuous line. Day key = UTC midnight.
            var buckets = Enumerable.Range(0, days)
                .Select(i => since.AddDays(i))
                .ToDictionary(d => d, d => new DayBucket { Day = d });
            foreach (var r in rows)
            {
                var key = r.CreatedAt.Date;
                if (!buckets.TryGetValue(key, out var bucket)) continue;
                bucket.Total++;
                if (r.Status is PaySessionStatus.Paid or PaySessionStatus.Confirmed)
                {
                    bucket.Paid++;
                    bucket.VolumeSats += r.AmountSatoshis;
                }
                else if (r.Status is PaySessionStatus.Refunded)
                {
                    bucket.Refunded++;
                }
                else if (r.Status is PaySessionStatus.Voided)
                {
                    bucket.Voided++;
                }
                else if (r.Status is PaySessionStatus.Expired or PaySessionStatus.Underpaid)
                {
                    bucket.Failed++;
                }
            }

            // Top-line KPIs — derived over the same window as the chart so the
            // summary and the bars always agree.
            //
            // A refunded session was paid first; counting it only in the
            // "Refunded" bucket would zero out the refund rate. So for KPI
            // purposes "successful" = paid + refunded (both received funds).
            var totalCurrentlyPaid = buckets.Values.Sum(b => (long)b.Paid);
            var totalRefunded = buckets.Values.Sum(b => (long)b.Refunded);
            var totalSuccess = totalCurrentlyPaid + totalRefunded;
            var totalSessions = buckets.Values.Sum(b => (long)b.Total);
            var grossSats = buckets.Values.Sum(b => b.VolumeSats);
            var conversionPct = totalSessions == 0
                ? 0m
                : Math.Round((decimal)totalSuccess / totalSessions * 100m, 1);
            var refundRatePct = totalSuccess == 0
                ? 0m
                : Math.Round((decimal)totalRefunded / totalSuccess * 100m, 1);

            return Results.Ok(new
            {
                storeId = store!.Id,
                days,
                since,
                until = now,
                summary = new
                {
                    totalSessions,
                    totalPaid = totalSuccess,
                    totalRefunded,
                    grossVolumeDgb = grossSats / 100_000_000m,
                    conversionPct,
                    refundRatePct,
                },
                series = buckets.Values
                    .OrderBy(b => b.Day)
                    .Select(b => new
                    {
                        day = b.Day,
                        total = b.Total,
                        paid = b.Paid,
                        refunded = b.Refunded,
                        voided = b.Voided,
                        failed = b.Failed,
                        volumeDgb = b.VolumeSats / 100_000_000m,
                    }),
            });
        });

        return group;
    }

    private sealed class DayBucket
    {
        public DateTime Day { get; set; }
        public int Total { get; set; }
        public int Paid { get; set; }
        public int Refunded { get; set; }
        public int Voided { get; set; }
        public int Failed { get; set; }
        public long VolumeSats { get; set; }
    }

    internal static object DeliveryDto(WebhookDelivery d) => new
    {
        d.Id,
        d.StoreId,
        d.SessionId,
        d.EventName,
        d.Url,
        d.Attempt,
        d.StatusCode,
        d.ErrorMessage,
        d.DurationMs,
        d.ResponseSnippet,
        d.CreatedAt,
        d.DeliveredAt,
        // When set, the WebhookRetrier will re-dispatch at the given UTC time.
        // Null on succeeded deliveries, permanent 4xx, and dead-lettered rows
        // (retry budget exhausted). Lets the merchant distinguish "still
        // retrying" from "gave up" at a glance in the dashboard.
        d.NextRetryAt,
        Success = d.StatusCode is >= 200 and < 300,
    };

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

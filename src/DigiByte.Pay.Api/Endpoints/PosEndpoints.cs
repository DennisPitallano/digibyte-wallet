using System.Security.Cryptography;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Point-of-sale device pairing. The POS kiosk page (/pos in Pay.Web) is a
/// headless checkout terminal — it needs its own scoped API key + target
/// store, but the tablet can't reasonably sign in with Digi-ID. The flow:
///
///   1. Merchant hits "Pair a device" in the dashboard. This posts to
///      /v1/pay/pos/pairings and gets back a short human-typable code.
///   2. On the tablet at /pos, the cashier punches in the code and posts
///      to /v1/pay/pos/claim. The server exchanges the code once for a
///      freshly-minted API key + the target store id. The tablet stashes
///      those in localStorage and is now a working POS terminal.
///
/// The pairing record lives in IMemoryCache (10-min TTL, single-use). The
/// PayApiKey row is only persisted on claim — so an unclaimed pairing
/// leaves no trail. Claim is rate-limited (see Program.cs) to prevent
/// brute-forcing the 6-char code.
/// </summary>
public static class PosEndpoints
{
    // 32-char unambiguous alphabet (no 0/O/1/I/L) for human-readable codes.
    // 6 chars = 32^6 ≈ 1.07e9 space — combined with 10-min TTL + rate limiting
    // that's far beyond brute-force reach for a kiosk pairing flow.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;
    private const string CachePrefix = "pos:pair:";
    private static readonly TimeSpan PairingTtl = TimeSpan.FromMinutes(10);

    public static RouteGroupBuilder MapPosEndpoints(this RouteGroupBuilder group)
    {
        // Merchant-authed: mint a pairing code. No DB write yet — the actual
        // PayApiKey only lands when the tablet redeems the code.
        group.MapPost("/pairings", async (
            CreatePairingRequest body,
            HttpRequest http,
            DigiPayDbContext db,
            IMemoryCache cache) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            // Resolve the target store so we can bake it into the pairing.
            // Explicit storeId wins; otherwise take the merchant's oldest
            // store (matches /sessions default-store behaviour).
            PayStore? store;
            if (!string.IsNullOrWhiteSpace(body.StoreId))
            {
                store = await db.Stores
                    .FirstOrDefaultAsync(s => s.Id == body.StoreId && s.MerchantId == merchant.Id);
                if (store is null)
                    return Results.BadRequest(new { error = "store not found for this merchant" });
            }
            else
            {
                store = await db.Stores.Where(s => s.MerchantId == merchant.Id)
                    .OrderBy(s => s.CreatedAt).FirstOrDefaultAsync();
                if (store is null)
                    return Results.BadRequest(new { error = "merchant has no stores" });
            }

            var deviceLabel = string.IsNullOrWhiteSpace(body.DeviceLabel)
                ? $"POS device — {DateTime.UtcNow:yyyy-MM-dd}"
                : $"POS — {body.DeviceLabel.Trim()}";
            if (deviceLabel.Length > 80) deviceLabel = deviceLabel[..80];

            var (prefix, secret, hash) = MerchantAuthenticator.GenerateApiKey();

            // Try a small number of rerolls if the human-readable code collides
            // in-cache (probability ~1e-9 per attempt — effectively never).
            string code;
            for (var attempt = 0; ; attempt++)
            {
                code = GeneratePairingCode();
                if (!cache.TryGetValue(CachePrefix + code, out _)) break;
                if (attempt >= 5)
                    return Results.Problem("Could not allocate pairing code, retry.");
            }

            var expiresAt = DateTime.UtcNow.Add(PairingTtl);
            cache.Set(CachePrefix + code, new PairingEntry(
                merchant.Id, store.Id, store.Name, merchant.DisplayName,
                prefix, secret, hash, deviceLabel, expiresAt), PairingTtl);

            return Results.Ok(new
            {
                code,
                expiresAt,
                storeId = store.Id,
                storeName = store.Name,
                deviceLabel,
            });
        });

        // Unauthed redemption. Single-shot: first valid POST wins, cache entry
        // is removed even on failure paths that got past lookup (store gone).
        group.MapPost("/claim", async (
            ClaimPairingRequest body,
            DigiPayDbContext db,
            IMemoryCache cache) =>
        {
            if (string.IsNullOrWhiteSpace(body.Code))
                return Results.BadRequest(new { error = "code is required", reason = "missing" });

            var normalized = body.Code.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
            // Format check first: a code that doesn't match our alphabet/length
            // is almost certainly a typo, not an expired code. Telling the
            // cashier the difference saves them from asking for a new one
            // when they only need to retype.
            if (normalized.Length != CodeLength || normalized.Any(c => !CodeAlphabet.Contains(c)))
                return Results.NotFound(new
                {
                    error = $"that doesn't look like a {CodeLength}-character pairing code — check it carefully and try again",
                    reason = "malformed",
                });

            var cacheKey = CachePrefix + normalized;
            if (!cache.TryGetValue<PairingEntry>(cacheKey, out var entry) || entry is null)
                return Results.NotFound(new
                {
                    // Cache silently drops expired entries, so from here we can't
                    // tell "never existed" from "expired or already claimed".
                    // Bias the message to expiry — the most common case for a
                    // correctly-formatted code that's no longer redeemable.
                    error = "pairing code expired or already used — ask your manager for a fresh code",
                    reason = "expired",
                });

            // Consume the code immediately — even if the DB write below throws,
            // the code is spent. Prevents replay on transient errors.
            cache.Remove(cacheKey);

            // Re-verify the store still belongs to the merchant (defence-in-depth
            // in case something was revoked in the 0–10min window).
            var store = await db.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == entry.StoreId && s.MerchantId == entry.MerchantId);
            if (store is null)
                return Results.BadRequest(new { error = "pairing target store is no longer available" });

            db.ApiKeys.Add(new PayApiKey
            {
                Id = $"key_{RandomId(16)}",
                MerchantId = entry.MerchantId,
                Prefix = entry.Prefix,
                Hash = entry.Hash,
                Label = entry.DeviceLabel,
            });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                apiKey = $"{entry.Prefix}_{entry.Secret}",
                prefix = entry.Prefix,
                merchantId = entry.MerchantId,
                merchantName = entry.MerchantName,
                storeId = entry.StoreId,
                storeName = entry.StoreName,
                deviceLabel = entry.DeviceLabel,
            });
        });

        // Convenience: list POS-labeled keys so the dashboard can show
        // previously-paired devices and their last-seen timestamp.
        group.MapGet("/devices", async (HttpRequest http, DigiPayDbContext db) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var devices = await db.ApiKeys.AsNoTracking()
                .Where(k => k.MerchantId == merchant.Id && k.RevokedAt == null
                    && (k.Label != null && k.Label.StartsWith("POS")))
                .OrderByDescending(k => k.LastUsedAt ?? k.CreatedAt)
                .Select(k => new
                {
                    id = k.Id,
                    label = k.Label,
                    prefix = k.Prefix,
                    createdAt = k.CreatedAt,
                    lastUsedAt = k.LastUsedAt,
                })
                .ToListAsync();
            return Results.Ok(devices);
        });

        // GET /v1/pay/pos/shift-report — end-of-shift summary for the POS
        // tablet. The cashier hits "Close shift" and gets back counts +
        // totals since a given start time (default: start of UTC day).
        // Scoped to the API key's merchant *and* an explicit storeId so a
        // multi-store merchant's tablet only reports the till it's signed
        // into. Tips are derived from the label convention written by
        // Pos.razor ("Tip X% (Y CUR)") — the on-chain amount is total-only
        // so there's no separate column to aggregate.
        group.MapGet("/shift-report", async (
            HttpRequest http,
            DigiPayDbContext db,
            string storeId,
            DateTime? since = null) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(storeId))
                return Results.BadRequest(new { error = "storeId is required" });

            // Verify the store belongs to the caller — don't leak counts
            // across merchants if a storeId is guessed.
            var ownsStore = await db.Stores.AsNoTracking()
                .AnyAsync(s => s.Id == storeId && s.MerchantId == merchant.Id);
            if (!ownsStore) return Results.NotFound(new { error = "store not found for this merchant" });

            var from = since?.ToUniversalTime() ?? DateTime.UtcNow.Date;
            var to = DateTime.UtcNow;

            var rows = await db.Sessions.AsNoTracking()
                .Where(s => s.MerchantId == merchant.Id
                    && s.StoreId == storeId
                    && s.CreatedAt >= from)
                .Select(s => new
                {
                    s.Status,
                    s.AmountSatoshis,
                    s.ReceivedSatoshis,
                    s.FiatCurrency,
                    s.FiatAmount,
                    s.Label,
                })
                .ToListAsync();

            // Treat paid *and* confirmed as "collected" — from the cashier's
            // point of view both are money in; the chain will finish confirming
            // in the background after the shift closes.
            var collected = rows.Where(r =>
                r.Status == PaySessionStatus.Paid || r.Status == PaySessionStatus.Confirmed).ToList();
            var expired = rows.Where(r => r.Status == PaySessionStatus.Expired).ToList();
            var underpaid = rows.Where(r => r.Status == PaySessionStatus.Underpaid).ToList();

            var expiredCount = expired.Count;
            var underpaidCount = underpaid.Count;
            var pendingCount = rows.Count(r => r.Status == PaySessionStatus.Pending
                || r.Status == PaySessionStatus.Seen);

            var collectedDgb = collected.Sum(r => r.AmountSatoshis) / 100_000_000m;
            // Lost revenue: what the cashier *would* have taken if the customer
            // completed those sessions. Underpaid uses the *invoiced* amount,
            // not the partial received, since that's the figure the cashier
            // entered and is reasoning about.
            var expiredDgb = expired.Sum(r => r.AmountSatoshis) / 100_000_000m;
            var underpaidDgb = underpaid.Sum(r => r.AmountSatoshis) / 100_000_000m;

            // Group fiat-quoted sales by currency. Sessions that were DGB-only
            // contribute to collectedDgb but not to any fiat bucket — the UI
            // shows those separately. Computed for collected and the two
            // "lost" outcomes so cashiers can see why takings are below
            // expectation, not just that they are.
            static Dictionary<string, decimal> ByFiat<T>(
                IEnumerable<T> rows,
                Func<T, decimal?> amount,
                Func<T, string?> currency) => rows
                .Where(r => amount(r) is not null && !string.IsNullOrWhiteSpace(currency(r)))
                .GroupBy(r => currency(r)!.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(r => amount(r)!.Value));

            var fiatTotals = ByFiat(collected, r => r.FiatAmount, r => r.FiatCurrency);
            var expiredFiatTotals = ByFiat(expired, r => r.FiatAmount, r => r.FiatCurrency);
            var underpaidFiatTotals = ByFiat(underpaid, r => r.FiatAmount, r => r.FiatCurrency);

            // Parse tips out of the label (see Pos.razor ChargeAsync). Safe
            // because the format is tightly controlled by our own client;
            // if someone edits the label manually we just skip that row.
            var tipTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in collected)
            {
                if (string.IsNullOrEmpty(r.Label)) continue;
                var (tipAmt, tipCur) = ParseTipFromLabel(r.Label);
                if (tipAmt is null || tipCur is null) continue;
                var key = tipCur.ToUpperInvariant();
                tipTotals[key] = (tipTotals.TryGetValue(key, out var v) ? v : 0m) + tipAmt.Value;
            }

            return Results.Ok(new
            {
                from,
                to,
                storeId,
                totals = new
                {
                    collectedCount = collected.Count,
                    collectedDgb,
                    fiat = fiatTotals,
                    tips = tipTotals,
                },
                // Lost-revenue breakdown: same shape as `totals` but for the
                // "money on the floor" outcomes. Helps the cashier explain a
                // shortfall ("we had 4 expired and 1 underpaid") rather than
                // just seeing a low collected number.
                missed = new
                {
                    expired = new
                    {
                        count = expiredCount,
                        dgb = expiredDgb,
                        fiat = expiredFiatTotals,
                    },
                    underpaid = new
                    {
                        count = underpaidCount,
                        dgb = underpaidDgb,
                        fiat = underpaidFiatTotals,
                    },
                },
                outcomes = new
                {
                    collected = collected.Count,
                    expired = expiredCount,
                    underpaid = underpaidCount,
                    pending = pendingCount,
                    total = rows.Count,
                },
            });
        });

        return group;
    }

    // Parses the "Tip X% (Y CUR)" suffix that Pos.razor appends to session
    // labels. Returns (null, null) on any shape mismatch so unknown labels
    // just don't contribute to the tip total.
    private static (decimal? Amount, string? Currency) ParseTipFromLabel(string label)
    {
        var idx = label.LastIndexOf("Tip ", StringComparison.Ordinal);
        if (idx < 0) return (null, null);
        var open = label.IndexOf('(', idx);
        var close = label.IndexOf(')', open + 1);
        if (open < 0 || close < 0) return (null, null);
        var inner = label[(open + 1)..close]; // e.g. "3 EUR"
        var space = inner.LastIndexOf(' ');
        if (space <= 0) return (null, null);
        var numPart = inner[..space];
        var curPart = inner[(space + 1)..];
        if (!decimal.TryParse(numPart, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var amt)) return (null, null);
        return (amt, curPart);
    }

    /// <summary>
    /// Dev-only helper to force a session into a target state and push via
    /// SignalR. Lets us exercise the kiosk's confirmed / expired / underpaid
    /// result screens without a real chain tx. Only registered when the host
    /// is in the Development environment; merchant-authed so it can only
    /// flip sessions owned by the caller.
    /// </summary>
    public static RouteGroupBuilder MapPosDevOnlyEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sessions/{id}/force-state", async (
            string id,
            ForceStateRequest body,
            HttpRequest http,
            DigiPayDbContext db,
            CheckoutNotifier notifier) =>
        {
            var merchant = await MerchantAuthenticator.AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            if (!Enum.TryParse<PaySessionStatus>(body.Status, ignoreCase: true, out var target))
                return Results.BadRequest(new { error = "status must be one of: pending, seen, paid, confirmed, expired, underpaid" });

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchant.Id);
            // 404 (not 403) keeps us from leaking that someone else's session exists.
            if (session is null) return Results.NotFound(new { error = "session not found" });

            var now = DateTime.UtcNow;
            session.Status = target;
            switch (target)
            {
                case PaySessionStatus.Seen:
                    session.SeenAt ??= now;
                    session.ReceivedSatoshis = session.AmountSatoshis;
                    break;
                case PaySessionStatus.Paid:
                    session.SeenAt ??= now;
                    session.PaidAt ??= now;
                    session.ReceivedSatoshis = session.AmountSatoshis;
                    session.Confirmations = Math.Max(1, session.Confirmations);
                    session.PaidTxid ??= $"dev{RandomTxid(56)}";
                    break;
                case PaySessionStatus.Confirmed:
                    session.SeenAt ??= now;
                    session.PaidAt ??= now;
                    session.ReceivedSatoshis = session.AmountSatoshis;
                    session.Confirmations = Math.Max(6, session.Confirmations);
                    session.PaidTxid ??= $"dev{RandomTxid(56)}";
                    break;
                case PaySessionStatus.Underpaid:
                    // Simulate partial payment — half the asking amount.
                    session.ReceivedSatoshis = session.AmountSatoshis / 2;
                    session.SeenAt ??= now;
                    break;
                case PaySessionStatus.Expired:
                case PaySessionStatus.Pending:
                    // No extra field mutation; leave prior timestamps alone.
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

    private static string RandomTxid(int lengthChars)
    {
        const string alphabet = "0123456789abcdef";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[lengthChars];
        for (var i = 0; i < lengthChars; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static string GeneratePairingCode()
    {
        Span<byte> bytes = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(chars);
    }

    private static string RandomId(int lengthChars)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[lengthChars];
        for (var i = 0; i < lengthChars; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private sealed record PairingEntry(
        string MerchantId,
        string StoreId,
        string StoreName,
        string MerchantName,
        string Prefix,
        string Secret,
        string Hash,
        string DeviceLabel,
        DateTime ExpiresAt);
}

public record CreatePairingRequest(string? StoreId, string? DeviceLabel);
public record ClaimPairingRequest(string Code);
public record ForceStateRequest(string Status);

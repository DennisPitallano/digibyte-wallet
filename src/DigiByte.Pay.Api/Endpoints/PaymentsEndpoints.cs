using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

public static class PaymentsEndpoints
{
    // 10-minute default price-lock / wait window before a pending session expires
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);

    public static RouteGroupBuilder MapPaymentsEndpoints(this RouteGroupBuilder group, IConfiguration config)
    {
        // POST /v1/pay/merchants — v0 unauthenticated merchant registration.
        // Real auth (Digi-ID signed registration + rotating keys) lands in v1.
        group.MapPost("/merchants", async (CreateMerchantRequest body, DigiPayDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.DisplayName))
                return Results.BadRequest(new { error = "displayName is required" });
            // Accept either a plain DigiByte address (single-address mode — simple) or a
            // BIP84 account-level xpub (per-session derivation). Backwards-compat: the old
            // `xpub` field is still read if `addressOrXpub` isn't provided.
            var key = body.AddressOrXpub ?? body.Xpub;
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { error = "addressOrXpub is required (DigiByte address or BIP84 xpub)" });

            var network = string.IsNullOrWhiteSpace(body.Network) ? "mainnet" : body.Network.ToLowerInvariant();
            var kind = MerchantAddressService.Classify(key, network, out var classifyError);
            if (kind is null)
                return Results.BadRequest(new { error = classifyError });

            var id = $"mer_{RandomId(16)}";
            var (keyPrefix, keySecret, keyHash) = GenerateApiKey();

            var merchant = new PayMerchant
            {
                Id = id,
                DisplayName = body.DisplayName.Trim(),
                Xpub = kind == MerchantAddressService.MerchantKeyKind.Xpub ? key.Trim() : null,
                ReceiveAddress = kind == MerchantAddressService.MerchantKeyKind.Address ? key.Trim() : null,
                Network = network,
                WebhookUrl = body.WebhookUrl,
                WebhookSecret = string.IsNullOrWhiteSpace(body.WebhookUrl) ? null : RandomId(32),
                ApiKeyPrefix = keyPrefix,
                ApiKeyHash = keyHash,
            };
            db.Merchants.Add(merchant);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                merchant.Id,
                merchant.DisplayName,
                merchant.Network,
                mode = kind.Value.ToString().ToLowerInvariant(),
                apiKey = $"{keyPrefix}_{keySecret}", // shown once
                webhookSecret = merchant.WebhookSecret,
            });
        });

        // POST /v1/pay/sessions — create a payment session.
        // Auth: Bearer {apiKey}. The apiKey is split "{prefix}_{secret}"; we look up
        // the merchant by prefix and compare SHA-256(secret) against the stored hash.
        group.MapPost("/sessions", async (
            CreateSessionRequest body,
            HttpRequest http,
            DigiPayDbContext db) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null)
                return Results.Unauthorized();

            if (body.Amount is null || body.Amount <= 0)
                return Results.BadRequest(new { error = "amount (DGB) must be > 0" });

            // Resolve the receive address:
            // - Xpub mode: allocate next index, derive m/.../0/index (per-session privacy).
            // - Address mode: reuse the single stored address (no derivation).
            int index;
            string address;
            if (!string.IsNullOrEmpty(merchant.Xpub))
            {
                index = merchant.NextAddressIndex;
                merchant.NextAddressIndex = index + 1;
                address = MerchantAddressService.DeriveAddress(merchant.Xpub, merchant.Network, index);
            }
            else if (!string.IsNullOrEmpty(merchant.ReceiveAddress))
            {
                index = 0;
                address = merchant.ReceiveAddress;
            }
            else
            {
                return Results.BadRequest(new { error = "merchant has no receive address or xpub configured — set one via PATCH /v1/pay/me" });
            }
            var amountSats = (long)Math.Round(body.Amount.Value * 100_000_000m);

            var session = new PaySession
            {
                Id = $"ses_{RandomId(16)}",
                MerchantId = merchant.Id,
                AddressIndex = index,
                Address = address,
                AmountSatoshis = amountSats,
                FiatCurrency = body.FiatCurrency,
                FiatAmount = body.FiatAmount,
                DgbPriceAtCreation = body.DgbPriceAtCreation,
                Label = body.Label,
                Memo = body.Memo,
                ExpiresAt = DateTime.UtcNow.Add(
                    body.ExpiresInSeconds is > 0 and <= 3600
                        ? TimeSpan.FromSeconds(body.ExpiresInSeconds.Value)
                        : DefaultExpiry),
            };

            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(ToDto(session, $"{checkoutBase}/pay/{session.Id}"));
        });

        // GET /v1/pay/sessions — list sessions for the authenticated merchant.
        // Filters: ?status=pending|seen|paid|confirmed|expired|underpaid
        // Pagination: ?take (1-100, default 25), ?skip (default 0)
        group.MapGet("/sessions", async (
            HttpRequest http,
            DigiPayDbContext db,
            string? status = null,
            int take = 25,
            int skip = 0) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            take = Math.Clamp(take, 1, 100);
            skip = Math.Max(skip, 0);

            var query = db.Sessions.AsNoTracking().Where(s => s.MerchantId == merchant.Id);
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<PaySessionStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(s => s.Status == parsedStatus);
            }

            var total = await query.CountAsync();
            var rows = await query.OrderByDescending(s => s.CreatedAt)
                .Skip(skip).Take(take).ToListAsync();

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(new
            {
                total,
                take,
                skip,
                sessions = rows.Select(s => ToDto(s, $"{checkoutBase}/pay/{s.Id}")),
            });
        });

        // GET /v1/pay/sessions/{id} — public read of session status.
        // Safe to expose without auth because id is random and contains no secret.
        // Used by the hosted checkout page for initial load.
        group.MapGet("/sessions/{id}", async (string id, DigiPayDbContext db) =>
        {
            var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (session is null) return Results.NotFound();

            var merchant = await db.Merchants.AsNoTracking().FirstOrDefaultAsync(m => m.Id == session.MerchantId);
            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(new
            {
                Session = ToDto(session, $"{checkoutBase}/pay/{session.Id}"),
                MerchantName = merchant?.DisplayName,
            });
        });

        return group;
    }

    private static async Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db)
    {
        var header = http.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        // Split on the LAST underscore — prefixes contain an underscore themselves (e.g. "dgp_xxxx").
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

    private static (string Prefix, string Secret, string Hash) GenerateApiKey()
    {
        var prefix = $"dgp_{RandomId(10)}";
        var secret = RandomId(32);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        return (prefix, secret, hash);
    }

    private static string RandomId(int lengthChars)
    {
        // Base32-ish alphanumeric id, URL-safe, no padding
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[lengthChars];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[lengthChars];
        for (int i = 0; i < lengthChars; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static object ToDto(PaySession s, string checkoutUrl) => new
    {
        s.Id,
        s.Address,
        s.AmountSatoshis,
        Amount = s.AmountSatoshis / 100_000_000m,
        s.FiatCurrency,
        s.FiatAmount,
        s.Label,
        s.Memo,
        Status = s.Status.ToString().ToLowerInvariant(),
        s.CreatedAt,
        s.ExpiresAt,
        s.ReceivedSatoshis,
        s.Confirmations,
        s.PaidTxid,
        Uri = BuildBip21(s),
        CheckoutUrl = checkoutUrl,
    };

    private static string BuildBip21(PaySession s)
    {
        var sb = new StringBuilder("digibyte:").Append(s.Address);
        var amount = (s.AmountSatoshis / 100_000_000m).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        sb.Append("?amount=").Append(amount);
        if (!string.IsNullOrWhiteSpace(s.Label))
            sb.Append("&label=").Append(Uri.EscapeDataString(s.Label));
        if (!string.IsNullOrWhiteSpace(s.Memo))
            sb.Append("&message=").Append(Uri.EscapeDataString(s.Memo));
        return sb.ToString();
    }
}

public record CreateMerchantRequest(
    string DisplayName,
    string? AddressOrXpub,
    string? Xpub, // legacy — still accepted if AddressOrXpub omitted
    string? Network,
    string? WebhookUrl);

public record CreateSessionRequest(
    decimal? Amount,
    string? FiatCurrency,
    decimal? FiatAmount,
    decimal? DgbPriceAtCreation,
    string? Label,
    string? Memo,
    int? ExpiresInSeconds);

using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Hubs;
using DigiByte.Pay.Api.Services;
using DigiByte.Server.Shared;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Endpoints;

public static class PaymentsEndpoints
{
    // Price-lock / wait window before a pending session is marked expired.
    // 30m matches BTCPay/BitPay; too short and buyers lose orders mid-flow.
    // Per-merchant override via PayMerchant.DefaultSessionExpiryMinutes.
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);

    public static RouteGroupBuilder MapPaymentsEndpoints(this RouteGroupBuilder group, IConfiguration config)
    {
        // POST /v1/pay/merchants — v0 unauthenticated merchant registration.
        // Real auth (Digi-ID signed registration + rotating keys) lands in v1.
        group.MapPost("/merchants", async (
            CreateMerchantRequest body, DigiPayDbContext db,
            IOptions<OutboundOptions> outbound, IOutboundResolver resolver, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.DisplayName))
                return Results.BadRequest(new { error = "displayName is required" });

            // The webhook URL is merchant-supplied and the server will call it: apply the outbound policy now, and again at every dispatch
            // (specs/008-security-hardening, DGB-03). Only the short category is returned; details stay in the log.
            if (!string.IsNullOrWhiteSpace(body.WebhookUrl))
            {
                var refusal = await OutboundUrlGuard.ValidateAsync(body.WebhookUrl.Trim(), outbound.Value.Webhook(), resolver, ct);
                if (refusal is not null)
                {
                    loggers.CreateLogger("DigiPayWebhook").LogWarning("Merchant registration refused webhook URL ({Reason})", refusal.Value.ToText());
                    return Results.BadRequest(new { error = $"webhookUrl: {refusal.Value.ToText()}" });
                }
            }
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
                // Legacy columns; PayApiKeys is the source of truth for auth.
                ApiKeyPrefix = keyPrefix,
                ApiKeyHash = keyHash,
            };
            db.Merchants.Add(merchant);
            db.ApiKeys.Add(new PayApiKey
            {
                Id = $"key_{RandomId(16)}",
                MerchantId = id,
                Prefix = keyPrefix,
                Hash = keyHash,
                Label = "SDK initial key",
            });

            // SDK-registered merchants get one store with the receive they provided.
            var storeId = $"sto_{RandomId(16)}";
            var webhookSecret = string.IsNullOrWhiteSpace(body.WebhookUrl) ? null : RandomId(32);
            db.Stores.Add(new PayStore
            {
                Id = storeId,
                MerchantId = id,
                Name = body.DisplayName.Trim(),
                Network = network,
                Xpub = kind == MerchantAddressService.MerchantKeyKind.Xpub ? key.Trim() : null,
                ReceiveAddress = kind == MerchantAddressService.MerchantKeyKind.Address ? key.Trim() : null,
                WebhookUrl = string.IsNullOrWhiteSpace(body.WebhookUrl) ? null : body.WebhookUrl.Trim(),
                WebhookSecret = webhookSecret,
            });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                merchant.Id,
                merchant.DisplayName,
                storeId,
                network,
                mode = kind.Value.ToString().ToLowerInvariant(),
                apiKey = $"{keyPrefix}_{keySecret}", // shown once
                webhookSecret,
            });
        }).RequireRateLimiting("register").AllowPosKey(); // unauthenticated

        // POST /v1/pay/sessions — create a payment session.
        // Auth: Bearer {apiKey}. The apiKey is split "{prefix}_{secret}"; we look up
        // the merchant by prefix and compare SHA-256(secret) against the stored hash.
        //
        // Idempotency: clients can send `Idempotency-Key: <up-to-255-chars>` to make
        // the request safely retryable. The first call creates a session and stores
        // the key→sessionId mapping; subsequent calls with the same key (within 24h)
        // return the *original* session without creating a new one. Scoped per
        // merchant. Stripe-shaped: payment APIs need this to make double-clicks safe.
        group.MapPost("/sessions", async (
            CreateSessionRequest body,
            HttpRequest http,
            DigiPayDbContext db,
            DigiDollarNodeClient ddNode,
            SessionOptionService options) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null)
                return Results.Unauthorized();
            var merchant = caller.Merchant;
            // A store-bound POS tablet only sells for its own store; no storeId means that store.
            if (!string.IsNullOrWhiteSpace(body.StoreId) && !caller.CanUseStore(body.StoreId))
                return ApiCaller.OutsideStore();
            var requestedStoreId = string.IsNullOrWhiteSpace(body.StoreId) && caller.IsStoreScoped
                ? caller.KeyStoreId
                : body.StoreId;

            var currency = string.IsNullOrWhiteSpace(body.Currency)
                ? "DGB"
                : body.Currency.Trim().ToUpperInvariant();
            if (currency is not ("DGB" or "DD"))
                return Results.BadRequest(new { error = "currency must be DGB or DD" });

            // Amount validation branches by currency. DGB uses the existing
            // decimal-DGB amount; DD uses integer USD cents.
            if (currency == "DGB")
            {
                if (body.Amount is null || body.Amount <= 0)
                    return Results.BadRequest(new { error = "amount (DGB) must be > 0" });
            }
            else // DD
            {
                if (body.AmountCentsDd is null or <= 0)
                    return Results.BadRequest(new { error = "amountCentsDd (USD cents) must be > 0 for DD sessions" });
                // Consensus caps: $1 dust floor, $100k per-tx ceiling.
                if (body.AmountCentsDd < 100)
                    return Results.BadRequest(new { error = "amountCentsDd below $1 dust floor" });
                if (body.AmountCentsDd > 10_000_000)
                    return Results.BadRequest(new { error = "amountCentsDd exceeds $100,000 per-transaction maximum" });
            }

            // Optional returnUrl validation. Mirrors the WebhookUrl rules —
            // HTTPS in production, HTTP allowed only for localhost (so a dev
            // running WC on port 8080 can still test the round-trip).
            string? returnUrl = null;
            if (!string.IsNullOrWhiteSpace(body.ReturnUrl))
            {
                var raw = body.ReturnUrl.Trim();
                if (raw.Length > 2048)
                    return Results.BadRequest(new { error = "returnUrl must be 2048 chars or fewer" });
                if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return Results.BadRequest(new { error = "returnUrl must be an absolute http(s) URL" });
                var isLocalhost = uri.Host is "localhost" or "127.0.0.1" or "::1"
                    || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase);
                if (uri.Scheme == Uri.UriSchemeHttp && !isLocalhost)
                    return Results.BadRequest(new { error = "returnUrl must use https outside of localhost" });
                returnUrl = uri.ToString();
            }

            // Idempotency replay short-circuit. Looked up before any expensive work
            // (store resolution, address derivation) so a retry costs one indexed
            // SELECT. The 24h cutoff matches what Stripe documents publicly.
            var idempotencyKey = http.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = "";
            else idempotencyKey = idempotencyKey.Trim();
            if (idempotencyKey.Length > 255)
                return Results.BadRequest(new { error = "Idempotency-Key must be 255 chars or fewer" });

            if (idempotencyKey.Length > 0)
            {
                var cutoff = DateTime.UtcNow.AddHours(-24);
                var existing = await db.IdempotencyRecords.AsNoTracking()
                    .Where(r => r.MerchantId == merchant.Id && r.Key == idempotencyKey && r.CreatedAt >= cutoff)
                    .FirstOrDefaultAsync();
                if (existing is not null)
                {
                    var prior = await db.Sessions.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == existing.SessionId);
                    if (prior is not null)
                    {
                        if (!caller.CanUseStore(prior.StoreId)) return ApiCaller.OutsideStore();
                        var checkoutBase0 = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
                        return Results.Ok(ToDto(prior, $"{checkoutBase0}/pay/{prior.Id}"));
                    }
                    // Mapping exists but session was deleted (rare: demo cleanup).
                    // Fall through and create fresh; we'll overwrite the record.
                }
            }

            // Resolve target store. Explicit storeId wins; otherwise fall back to the
            // merchant's first (default) store so older integrations keep working.
            PayStore? store;
            if (!string.IsNullOrWhiteSpace(requestedStoreId))
            {
                store = await db.Stores.FirstOrDefaultAsync(s => s.Id == requestedStoreId && s.MerchantId == merchant.Id);
                if (store is null) return Results.BadRequest(new { error = "store not found for this merchant" });
            }
            else
            {
                store = await db.Stores.Where(s => s.MerchantId == merchant.Id)
                    .OrderBy(s => s.CreatedAt).FirstOrDefaultAsync();
                if (store is null) return Results.BadRequest(new { error = "merchant has no stores" });
            }

            // Resolve the receive address:
            // - DD sessions: generate a fresh DD address from the node RPC
            //   (no xpub-based derivation exists for DD; the node's DD wallet
            //   owns the keys). Requires the store to opt in via
            //   DigiDollarEnabled.
            // - Xpub mode: allocate next index, derive m/.../0/index (per-session privacy).
            // - Address mode: reuse the single stored address (no derivation).
            int index;
            string address;
            if (currency == "DD")
            {
                if (!store.DigiDollarEnabled)
                    return Results.BadRequest(new { error = "digidollar_not_enabled_for_store" });
                try
                {
                    address = await ddNode.GenerateAddressAsync();
                }
                catch (Exception ex)
                {
                    return Results.Json(
                        new { error = "digidollar_node_unavailable", detail = ex.Message },
                        statusCode: 503);
                }
                index = 0;
            }
            else if (!string.IsNullOrEmpty(store.Xpub))
            {
                index = store.NextAddressIndex;
                store.NextAddressIndex = index + 1;
                address = MerchantAddressService.DeriveAddress(store.Xpub, store.Network, index);
            }
            else if (!string.IsNullOrEmpty(store.ReceiveAddress))
            {
                index = 0;
                address = store.ReceiveAddress;
            }
            else
            {
                return Results.BadRequest(new { error = "store has no receive address or xpub configured — PATCH /v1/pay/stores/{id} first" });
            }
            // AmountSatoshis is 0 for DD sessions (amount lives in AmountCentsDd).
            var amountSats = currency == "DGB"
                ? (long)Math.Round(body.Amount!.Value * 100_000_000m)
                : 0L;

            // Stamp the session's origin when it was created by a POS tablet key
            // ("pos" or "pos:Front counter"). Lets the dashboard separate countertop
            // sales from online/SDK orders without a second table.
            var source = caller.SessionSource;
            // specs/033 research R5: only honoured when every condition holds; otherwise ignored without an error.
            var buyerChoice = options.Allowed(body, store, currency);

            var session = new PaySession
            {
                Id = $"ses_{RandomId(16)}",
                MerchantId = merchant.Id,
                StoreId = store.Id,
                AddressIndex = index,
                Address = address,
                AmountSatoshis = amountSats,
                // Upper-case so "php" and "PHP" total together in shift reports and the dashboard.
                FiatCurrency = string.IsNullOrWhiteSpace(body.FiatCurrency) ? null : body.FiatCurrency.Trim().ToUpperInvariant(),
                FiatAmount = body.FiatAmount,
                DgbPriceAtCreation = body.DgbPriceAtCreation,
                Label = body.Label,
                Memo = body.Memo,
                ExpiresAt = DateTime.UtcNow.Add(ResolveExpiry(body.ExpiresInSeconds, store)),
                Source = source,
                ReturnUrl = returnUrl,
                Currency = currency,
                AmountCentsDd = currency == "DD" ? body.AmountCentsDd : null,
                BuyerChoice = buyerChoice,
            };

            db.Sessions.Add(session);
            var sessionOptions = new List<PaySessionOption>();
            if (buyerChoice)
            {
                var dgbOption = SessionOptionService.DgbOption(session);
                db.SessionOptions.Add(dgbOption);
                sessionOptions.Add(dgbOption);
                // A store whose default is DD starts its buyers on DD (FR-021); on any node trouble it starts on DGB.
                if (store.DefaultCurrency == "DD"
                    && await options.TryCreateAtCreationAsync(session, store) is { } ddOption)
                {
                    sessionOptions.Add(ddOption);
                    SessionOptionService.StartOn(session, ddOption);
                }
            }
            // Every DD address DigiPay creates is recorded with its owner, so payouts can attribute the DD (specs/034 R4).
            if (session.Currency == "DD")
                await DigiByte.Pay.Api.Services.Payouts.DdAddressRegistry.RegisterAsync(db, session.Address, merchant.Id, store.Id,
                    DigiByte.Pay.Api.Data.DdAddress.KindPayment, session.Id, store.Network);
            // Persist the idempotency mapping in the same SaveChanges so a
            // partial failure can't leave the session without its mapping (or
            // vice-versa). The unique (MerchantId, Key) index guards against
            // a parallel double-submit racing past the lookup above.
            if (idempotencyKey.Length > 0)
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Id = $"idem_{RandomId(16)}",
                    MerchantId = merchant.Id,
                    Key = idempotencyKey,
                    SessionId = session.Id,
                });
            }
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (idempotencyKey.Length > 0)
            {
                // Race: a concurrent request inserted the same key between our
                // SELECT and INSERT. The other request's session is the
                // canonical one — return that instead of erroring out.
                db.ChangeTracker.Clear();
                var winner = await db.IdempotencyRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MerchantId == merchant.Id && r.Key == idempotencyKey);
                if (winner is not null)
                {
                    var winnerSession = await db.Sessions.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == winner.SessionId);
                    if (winnerSession is not null)
                    {
                        var winBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
                        return Results.Ok(ToDto(winnerSession, $"{winBase}/pay/{winnerSession.Id}"));
                    }
                }
                throw;
            }

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(ToDto(session, $"{checkoutBase}/pay/{session.Id}", sessionOptions));
        }).AllowPosKey();

        // GET /v1/pay/sessions — list sessions for the authenticated merchant.
        // Filters: ?status=pending|seen|paid|confirmed|expired|underpaid
        // Pagination: ?take (1-100, default 25), ?skip (default 0).
        // Bookkeeping export: ?format=csv overrides pagination (caps at 10 000
        // rows for memory safety) and returns a downloadable spreadsheet.
        group.MapGet("/sessions", async (
            HttpRequest http,
            DigiPayDbContext db,
            string? status = null,
            string? storeId = null,
            int take = 25,
            int skip = 0,
            string? format = null) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var merchant = caller.Merchant;
            // A store-bound POS tablet only ever sees its own store's sessions.
            if (caller.IsStoreScoped)
            {
                if (!string.IsNullOrWhiteSpace(storeId) && !caller.CanUseStore(storeId)) return ApiCaller.OutsideStore();
                storeId = caller.KeyStoreId;
            }

            var wantsCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            take = wantsCsv ? Math.Clamp(take, 1, 10_000) : Math.Clamp(take, 1, 100);
            skip = Math.Max(skip, 0);

            var query = db.Sessions.AsNoTracking().Where(s => s.MerchantId == merchant.Id);
            if (!string.IsNullOrWhiteSpace(storeId))
                query = query.Where(s => s.StoreId == storeId);
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<PaySessionStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(s => s.Status == parsedStatus);
            }

            if (wantsCsv)
            {
                // CSV path projects only the columns it needs so the DB doesn't
                // ship every PaySession field (xpub-derived address bytes,
                // refund metadata, etc.) for a 10k-row export.
                var csvRows = await query.OrderByDescending(s => s.CreatedAt)
                    .Take(take)
                    .Select(s => new
                    {
                        s.Id, s.StoreId, s.Status, s.Address, s.AmountSatoshis,
                        s.FiatCurrency, s.FiatAmount, s.Label, s.Memo,
                        s.ReceivedSatoshis, s.Confirmations, s.PaidTxid,
                        s.CreatedAt, s.ExpiresAt, s.SeenAt, s.PaidAt, s.Source,
                        s.Currency, s.AmountCentsDd,
                    })
                    .ToListAsync();
                var sb = new System.Text.StringBuilder(64 + csvRows.Count * 256);
                CsvWriter.WriteRow(sb, new object?[]
                {
                    "id", "storeId", "status", "address", "amountDgb", "fiatCurrency",
                    "fiatAmount", "label", "memo", "receivedDgb", "confirmations",
                    "paidTxid", "createdAt", "expiresAt", "seenAt", "paidAt", "source",
                    // specs/033: the session's own currency and amounts in it (DD in dollars). Appended so existing
                    // column positions don't move; amountDgb and receivedDgb are 0 for DigiDollar sessions.
                    "currency", "amount", "received",
                });
                foreach (var s in csvRows)
                {
                    var isDd = s.Currency == "DD";
                    CsvWriter.WriteRow(sb, new object?[]
                    {
                        s.Id, s.StoreId, s.Status.ToString().ToLowerInvariant(),
                        s.Address, s.AmountSatoshis / 100_000_000m,
                        s.FiatCurrency, s.FiatAmount, s.Label, s.Memo,
                        isDd ? 0m : s.ReceivedSatoshis / 100_000_000m, s.Confirmations, s.PaidTxid,
                        s.CreatedAt, s.ExpiresAt, s.SeenAt, s.PaidAt, s.Source,
                        s.Currency,
                        isDd ? (s.AmountCentsDd ?? 0) / 100m : s.AmountSatoshis / 100_000_000m,
                        isDd ? s.ReceivedSatoshis / 100m : s.ReceivedSatoshis / 100_000_000m,
                    });
                }
                var filename = $"digipay-sessions-{DateTime.UtcNow:yyyyMMdd}.csv";
                return Results.File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                    contentType: "text/csv; charset=utf-8",
                    fileDownloadName: filename);
            }

            var total = await query.CountAsync();
            var rows = await query.OrderByDescending(s => s.CreatedAt)
                .Skip(skip).Take(take).ToListAsync();

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            // Payout status for DD rows (specs/034 FR-028): merchant-only, so it's added here and not on the public read.
            var payoutInfo = await DigiByte.Pay.Api.Services.Payouts.SessionPayoutInfo.LoadAsync(db, rows);
            return Results.Ok(new
            {
                total,
                take,
                skip,
                sessions = rows.Select(s => DigiByte.Pay.Api.Services.Payouts.SessionPayoutInfo.Attach(ToDto(s, $"{checkoutBase}/pay/{s.Id}"), s.Id, payoutInfo)),
            });
        }).AllowPosKey();

        // GET /v1/pay/sessions/{id} — public read of session status.
        // Safe to expose without auth because id is random and contains no secret.
        // Used by the hosted checkout page for initial load.
        group.MapGet("/sessions/{id}", async (string id, HttpRequest http, DigiPayDbContext db, SessionOptionService optionService, DdAvailability availability) =>
        {
            var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (session is null) return Results.NotFound();

            var merchant = await db.Merchants.AsNoTracking().FirstOrDefaultAsync(m => m.Id == session.MerchantId);
            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');

            // specs/034 T047: the store's own merchant also sees whether the DD was paid out. Never anyone else: this
            // read is public.
            async Task<object> ForCaller(object dto)
            {
                if (session.Currency != "DD" && !session.BuyerChoice) return dto;
                var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
                if (caller is null || caller.Merchant.Id != session.MerchantId) return dto;
                var info = await DigiByte.Pay.Api.Services.Payouts.SessionPayoutInfo.LoadAsync(db, [session]);
                return DigiByte.Pay.Api.Services.Payouts.SessionPayoutInfo.Attach(dto, session.Id, info);
            }

            if (!session.BuyerChoice)
            {
                return Results.Ok(new
                {
                    Session = await ForCaller(ToDto(session, $"{checkoutBase}/pay/{session.Id}")),
                    MerchantName = merchant?.DisplayName,
                });
            }

            // specs/033 contract C1: whether the buyer can still pick DD. False when the store or the DD surfaces
            // switch turned it off (even after a DD option was made), or when the node is down.
            var sessionOptions = await db.SessionOptions.AsNoTracking().Where(o => o.SessionId == session.Id).ToListAsync();
            var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId);
            string? ddReason = store is null || !optionService.DdOffered(store) ? "dd_disabled"
                : !await availability.IsAvailableAsync() ? "unavailable"
                : null;
            return Results.Ok(new
            {
                Session = await ForCaller(ToDto(session, $"{checkoutBase}/pay/{session.Id}", sessionOptions)),
                MerchantName = merchant?.DisplayName,
                DdAvailable = ddReason is null,
                DdReason = ddReason,
            });
        }).AllowPosKey(); // public read

        // POST /v1/pay/sessions/{id}/options/dd — the buyer's first switch to DD on a buyer-choice payment
        // (specs/033 contract C1). Anonymous like the checkout page; at most one DD option per session.
        group.MapPost("/sessions/{id}/options/dd", async (string id, SessionOptionService optionService) =>
        {
            var result = await optionService.GetOrCreateDdOptionAsync(id, DateTime.UtcNow);
            return result.Failure switch
            {
                SessionOptionService.Failure.None => Results.Ok(OptionDto(result.Option!, null, null)),
                SessionOptionService.Failure.NotFound => Results.NotFound(new { error = "not_found" }),
                SessionOptionService.Failure.NoChoice => Results.Conflict(new { error = "dd_option_not_allowed", reason = "no_choice" }),
                SessionOptionService.Failure.Locked => Results.Conflict(new { error = "dd_option_not_allowed", reason = "locked" }),
                SessionOptionService.Failure.NotPending => Results.Conflict(new { error = "dd_option_not_allowed", reason = "not_pending" }),
                SessionOptionService.Failure.Expired => Results.Conflict(new { error = "dd_option_not_allowed", reason = "expired" }),
                SessionOptionService.Failure.DdDisabled => Results.Conflict(new { error = "dd_option_unavailable", reason = "dd_disabled" }),
                SessionOptionService.Failure.NoRate => Results.Conflict(new { error = "dd_option_unavailable", reason = "no_rate" }),
                SessionOptionService.Failure.OutOfRange => Results.Conflict(new
                {
                    error = "dd_option_unavailable",
                    reason = "out_of_range",
                    minCents = SessionOptionService.MinCents,
                    maxCents = SessionOptionService.MaxCents,
                }),
                _ => Results.Json(new { error = "digidollar_node_unavailable" }, statusCode: 503),
            };
        }).RequireRateLimiting("checkout-option").AllowAnonymous();

        // POST /v1/pay/sessions/{id}/refund — record an off-platform refund.
        //
        // Crypto refunds aren't automatic reversals — the merchant sends DGB
        // back to the customer from their own wallet and then stamps the txid
        // here so the dashboard, webhooks, and receipts stay in sync. Only
        // Paid / Confirmed sessions are refundable; already-refunded sessions
        // are idempotent no-ops (returns 409 so the UI can surface it).
        group.MapPost("/sessions/{id}/refund", async (
            string id,
            HttpRequest http,
            DigiPayDbContext db,
            WebhookDispatcher dispatcher,
            CheckoutNotifier notifier,
            AuditLogger audit,
            RefundRequest body) =>
        {
            var merchant = await AuthenticateAsync(http, db);
            if (merchant is null) return Results.Unauthorized();

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchant.Id);
            if (session is null) return Results.NotFound();

            if (session.Status is not (PaySessionStatus.Paid or PaySessionStatus.Confirmed))
                return Results.Conflict(new { error = $"session is {session.Status.ToString().ToLowerInvariant()} and cannot be refunded" });

            var txid = body.Txid?.Trim();
            if (string.IsNullOrWhiteSpace(txid) || txid.Length is < 32 or > 128)
                return Results.BadRequest(new { error = "txid is required (32–128 chars)" });

            session.Status = PaySessionStatus.Refunded;
            session.RefundTxid = txid;
            session.RefundedAt = DateTime.UtcNow;
            session.RefundNote = string.IsNullOrWhiteSpace(body.Note) ? null
                : (body.Note.Length > 512 ? body.Note[..512] : body.Note);
            await db.SaveChangesAsync();

            await audit.LogAsync(merchant.Id, "session.refund", "Session", session.Id,
                summary: $"Refunded session {session.Id} ({session.AmountSatoshis / 100_000_000m:0.########} DGB)",
                metadata: new { txid, note = session.RefundNote, amountSatoshis = session.AmountSatoshis });

            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == session.StoreId);
            if (store is not null) await dispatcher.DispatchAsync(store, session, "session.refunded");
            await notifier.PublishAsync(new CheckoutStatusUpdate
            {
                SessionId = session.Id,
                Status = session.Status.ToString().ToLowerInvariant(),
                ReceivedSatoshis = session.ReceivedSatoshis,
                Confirmations = session.Confirmations,
                Txid = session.PaidTxid,
            });

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(ToDto(session, $"{checkoutBase}/pay/{session.Id}"));
        });

        // POST /v1/pay/sessions/{id}/void — cancel an unpaid session.
        //
        // Only valid for Pending / Seen sessions that never reached Paid.
        // Terminal-negative: clients should stop polling and the checkout
        // page shows "cancelled by merchant". We also shrink ExpiresAt so
        // the InvoiceMonitor grace window doesn't resurrect it.
        group.MapPost("/sessions/{id}/void", async (
            string id,
            HttpRequest http,
            DigiPayDbContext db,
            WebhookDispatcher dispatcher,
            CheckoutNotifier notifier,
            AuditLogger audit,
            VoidRequest? body) =>
        {
            var caller = await MerchantAuthenticator.GetCallerAsync(http, db);
            if (caller is null) return Results.Unauthorized();
            var merchant = caller.Merchant;

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchant.Id);
            if (session is null) return Results.NotFound();
            if (!caller.CanUseStore(session.StoreId)) return ApiCaller.OutsideStore();

            if (session.Status is not (PaySessionStatus.Pending or PaySessionStatus.Seen))
                return Results.Conflict(new { error = $"session is {session.Status.ToString().ToLowerInvariant()} and cannot be voided" });

            session.Status = PaySessionStatus.Voided;
            session.RefundedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(body?.Note))
                session.RefundNote = body.Note.Length > 512 ? body.Note[..512] : body.Note;
            // Collapse the expiry so the monitor stops polling the chain for this one.
            if (session.ExpiresAt > DateTime.UtcNow) session.ExpiresAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await audit.LogAsync(merchant.Id, "session.void", "Session", session.Id,
                summary: $"Voided session {session.Id}",
                metadata: new { note = session.RefundNote });

            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == session.StoreId);
            if (store is not null) await dispatcher.DispatchAsync(store, session, "session.voided");
            await notifier.PublishAsync(new CheckoutStatusUpdate
            {
                SessionId = session.Id,
                Status = session.Status.ToString().ToLowerInvariant(),
                ReceivedSatoshis = session.ReceivedSatoshis,
                Confirmations = session.Confirmations,
                Txid = session.PaidTxid,
            });

            var checkoutBase = (config["ClientWebUrl"] ?? "https://dgbwallet.app").TrimEnd('/');
            return Results.Ok(ToDto(session, $"{checkoutBase}/pay/{session.Id}"));
        }).AllowPosKey();

        return group;
    }

    private static TimeSpan ResolveExpiry(int? requestedSeconds, PayStore store)
    {
        // Explicit per-session override wins, clamped to avoid abuse.
        if (requestedSeconds is > 0 and <= 24 * 3600)
            return TimeSpan.FromSeconds(requestedSeconds.Value);
        // Store's preferred default, clamped 1 min – 24 h.
        if (store.DefaultSessionExpiryMinutes is int mins and > 0 and <= 24 * 60)
            return TimeSpan.FromMinutes(mins);
        return DefaultExpiry;
    }

    // Shared across endpoints — supports both dgp_ (API key) and dps_ (session) tokens.
    private static Task<PayMerchant?> AuthenticateAsync(HttpRequest http, DigiPayDbContext db) =>
        MerchantAuthenticator.AuthenticateAsync(http, db);

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

    private static object ToDto(PaySession s, string checkoutUrl, IReadOnlyList<PaySessionOption>? options = null) => new
    {
        s.Id,
        s.StoreId,
        s.Address,
        s.AmountSatoshis,
        Amount = s.AmountSatoshis / 100_000_000m,
        s.FiatCurrency,
        s.FiatAmount,
        // Exposed so the hosted checkout can warn the buyer if the live DGB
        // price has drifted meaningfully from the quote — volatility guard
        // is a pure client-side compare (see Checkout.razor). Null for
        // pure-DGB sessions where no fiat quote was ever made.
        s.DgbPriceAtCreation,
        s.Label,
        s.Memo,
        Status = s.Status.ToString().ToLowerInvariant(),
        s.CreatedAt,
        s.ExpiresAt,
        s.SeenAt,
        s.PaidAt,
        s.ReceivedSatoshis,
        s.Confirmations,
        s.PaidTxid,
        s.Source,
        // Refund/void audit. Nulls on non-refunded sessions; fields get set
        // together by POST /sessions/{id}/refund. Void doesn't populate txid,
        // only RefundNote + RefundedAt (we reuse the same columns to avoid a
        // parallel set of Voided* fields — Status disambiguates).
        s.RefundTxid,
        s.RefundedAt,
        s.RefundNote,
        // Optional merchant-supplied URL the hosted checkout sends the buyer
        // back to after payment confirms. Null for SDK / direct-API merchants
        // who own their own success page.
        s.ReturnUrl,
        // DigiDollar. Currency = "DGB" | "DD". For DD sessions,
        // AmountCentsDd is the requested USD amount in cents; AmountSatoshis
        // stays at 0. ReceivedSatoshis is repurposed to hold received DD in
        // cents (unit implied by Currency).
        s.Currency,
        s.AmountCentsDd,
        Uri = BuildBip21(s),
        CheckoutUrl = checkoutUrl,
        // Buyer choice (specs/033 contract C1). The fields above describe the currency in effect; options lists every
        // currency the payment can be paid in (empty without buyer choice).
        s.BuyerChoice,
        s.LockedAt,
        Options = (options ?? []).OrderBy(o => o.Currency == "DGB" ? 0 : 1).Select(o => OptionDto(o, s.Label, s.Memo)).ToList(),
        Extra = s.ExtraCurrency is null ? null : new { Currency = s.ExtraCurrency, Amount = s.ExtraAmount, Txid = s.ExtraTxid, SeenAt = s.ExtraSeenAt },
    };

    private static object OptionDto(PaySessionOption o, string? label, string? memo) => new
    {
        o.Currency,
        o.Address,
        o.AmountSatoshis,
        o.AmountCentsDd,
        o.UsdRate,
        o.RateAt,
        Uri = BuildBip21(o.Address, o.Currency, o.AmountSatoshis, o.AmountCentsDd, label, memo),
    };

    private static string BuildBip21(PaySession s) =>
        BuildBip21(s.Address, s.Currency, s.AmountSatoshis, s.AmountCentsDd, s.Label, s.Memo);

    private static string BuildBip21(string address, string currency, long amountSatoshis, long? amountCentsDd, string? label, string? memo)
    {
        var sb = new StringBuilder("digibyte:").Append(address);
        // DGB sessions carry an amount in DGB; DD sessions carry USD cents
        // with a currency hint so wallets that understand DigiDollar can
        // prompt for the right thing. Non-DD-aware wallets will show the
        // amount as DGB — the DD address prefix (DD/TD/RD) makes the mistake
        // hard to complete anyway.
        if (currency == "DD" && amountCentsDd is > 0)
        {
            var usd = (amountCentsDd.Value / 100m).ToString(
                "0.00##", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("?amount=").Append(usd).Append("&currency=USD");
        }
        else
        {
            var amount = (amountSatoshis / 100_000_000m).ToString(
                "0.########", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append("?amount=").Append(amount);
        }
        if (!string.IsNullOrWhiteSpace(label))
            sb.Append("&label=").Append(Uri.EscapeDataString(label));
        if (!string.IsNullOrWhiteSpace(memo))
            sb.Append("&message=").Append(Uri.EscapeDataString(memo));
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
    string? StoreId,
    string? FiatCurrency,
    decimal? FiatAmount,
    decimal? DgbPriceAtCreation,
    string? Label,
    string? Memo,
    int? ExpiresInSeconds,
    string? ReturnUrl,
    // DigiDollar support. Currency defaults to "DGB" (existing behaviour) when
    // null. When Currency = "DD", AmountCentsDd carries the requested USD
    // amount in cents (10000 = $100.00) and Amount / FiatAmount are ignored.
    string? Currency = null,
    long? AmountCentsDd = null,
    // specs/033 FR-028: ask for the buyer's DGB/DD choice at checkout. Honoured only for a fiat-priced DGB payment at
    // a store with DigiDollar and buyer choice on (and the DD surfaces switch on); ignored otherwise.
    bool? BuyerChoice = null);

public record RefundRequest(string? Txid, string? Note);
public record VoidRequest(string? Note);

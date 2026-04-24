using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Endpoints;
using DigiByte.Pay.Api.Hubs;
using DigiByte.Pay.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Railway sets PORT; mirror the wallet's convention.
var port = Environment.GetEnvironmentVariable("PORT");
if (port is not null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// OpenAPI — customise document info + register a Bearer security scheme
// so Scalar shows a proper title/description and an Authorize button that
// injects Authorization: Bearer {prefix}_{secret} on every authenticated
// endpoint. The sandbox and public groups opt out via a per-operation
// transformer so their lock icons don't show up.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "DigiPay API",
            Version = "v1",
            Description =
                "REST API for DigiByte payments. Merchants register once, create checkout sessions " +
                "per customer, and receive webhook notifications when payment state changes.\n\n" +
                "**Auth.** Every endpoint outside `public/*` and `test/*` expects " +
                "`Authorization: Bearer {prefix}_{secret}` where `{prefix}` is `dgp_` (long-lived " +
                "API key) or `dps_` (browser session token). Only SHA-256 hashes are stored " +
                "server-side — secrets are shown once on creation.",
            Contact = new OpenApiContact
            {
                Name = "DigiByte Pay",
                Url = new Uri("https://pay.dgbwallet.app"),
            },
            License = new OpenApiLicense
            {
                Name = "MIT",
                Url = new Uri("https://github.com/DennisPitallano/digibyte-wallet/blob/main/LICENSE"),
            },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "dgp_… or dps_…",
            Description = "API key (dgp_…) for server-side integrations, or session token (dps_…) minted by Digi-ID sign-in.",
        };

        // Apply Bearer as the default requirement; public/sandbox endpoints
        // clear it below in the operation transformer.
        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document, null)] = new List<string>(),
        });
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var path = context.Description.RelativePath ?? string.Empty;
        var isUnauth = path.StartsWith("v1/pay/public", StringComparison.Ordinal)
            || path.StartsWith("v1/pay/test", StringComparison.Ordinal)
            || path == "v1/pay/merchants";

        // These endpoints are documented as unauthenticated — Register (self-
        // serve account creation), Public stats, and the Sandbox.
        if (isUnauth)
        {
            operation.Security = new List<OpenApiSecurityRequirement>();
        }

        // Canonical error responses. Every endpoint can 400 on malformed input;
        // authenticated endpoints can also 401 (bad key) and 404 (resource
        // belongs to a different merchant → we return 404 over 403 to avoid
        // leaking existence). Spelled out here so Scalar shows them without
        // having to decorate every individual endpoint.
        operation.Responses ??= new OpenApiResponses();
        if (!operation.Responses.ContainsKey("400"))
            operation.Responses["400"] = new OpenApiResponse { Description = "Bad request — validation failed or malformed input." };
        if (!isUnauth)
        {
            if (!operation.Responses.ContainsKey("401"))
                operation.Responses["401"] = new OpenApiResponse { Description = "Missing or invalid Bearer token." };
            if (!operation.Responses.ContainsKey("404"))
                operation.Responses["404"] = new OpenApiResponse { Description = "Resource not found (or not owned by the authenticated merchant)." };
        }
        // Sandbox is rate-limited per IP; call that out in the doc.
        if (path.StartsWith("v1/pay/test", StringComparison.Ordinal)
            && !operation.Responses.ContainsKey("429"))
        {
            operation.Responses["429"] = new OpenApiResponse { Description = "Rate limit exceeded — sandbox is capped at 20 requests/minute per IP." };
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();

// Rate limiter — layered policies keyed to the threat model:
//   sandbox  (20/min  per IP) — unauthenticated demo/test endpoints, prevents
//                                DB-growth drive-bys from the public embed.
//   auth     (10/min  per IP) — Digi-ID challenge/verify; slow online guessing.
//   register ( 5/min  per IP) — self-serve merchant signup; caps account spam.
//   claim    (20/min  per IP) — POS pair-code claim; 6-char codes expire in 5min
//                                but we still want to blunt sustained guessing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static System.Threading.RateLimiting.RateLimitPartition<string> PerIp(
        HttpContext ctx, int permit, TimeSpan window)
    {
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = window,
                QueueLimit = 0,
            });
    }
    options.AddPolicy("sandbox",  ctx => PerIp(ctx, 20, TimeSpan.FromMinutes(1)));
    options.AddPolicy("auth",     ctx => PerIp(ctx, 10, TimeSpan.FromMinutes(1)));
    options.AddPolicy("register", ctx => PerIp(ctx,  5, TimeSpan.FromMinutes(1)));
    options.AddPolicy("claim",    ctx => PerIp(ctx, 20, TimeSpan.FromMinutes(1)));
});

// Database provider:
//   - DigiPay:ConnectionString (Postgres)  — production / deploy target
//   - DigiPay:DbPath (SQLite file)         — dev convenience when Postgres
//     isn't available locally
// Provider chosen by whether the value looks like a connection string.
var pgConnectionString = builder.Configuration["DigiPay:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");
builder.Services.AddDbContext<DigiPayDbContext>(opt =>
{
    if (!string.IsNullOrWhiteSpace(pgConnectionString))
    {
        opt.UseNpgsql(NormalisePostgresUrl(pgConnectionString));
    }
    else
    {
        var sqlitePath = builder.Configuration["DigiPay:DbPath"] ?? "digipay.db";
        opt.UseSqlite($"Data Source={sqlitePath}");
    }
});

// Railway & many PaaS expose Postgres as a URL (postgres://user:pass@host:port/db)
// but Npgsql expects key=value form. Translate on the fly when needed.
static string NormalisePostgresUrl(string value)
{
    if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return value;
    var uri = new Uri(value);
    var userInfo = uri.UserInfo.Split(':', 2);
    var db = uri.AbsolutePath.TrimStart('/');
    var sslMode = uri.Query.Contains("sslmode=", StringComparison.OrdinalIgnoreCase) ? "" : "SSL Mode=Require;Trust Server Certificate=true;";
    return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={db};Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.Length > 1 ? userInfo[1] : "")};{sslMode}";
}

builder.Services.AddHttpClient("DigiPayChain", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiPay/0.1");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("DigiPayWebhook", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DigiPay-Webhook/0.1");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<CheckoutNotifier>();
builder.Services.AddSingleton<AuthChallengeStore>();
builder.Services.AddScoped<WebhookDispatcher>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddHostedService<InvoiceMonitor>();
builder.Services.AddHostedService<DemoDataJanitor>();
builder.Services.AddHostedService<WebhookRetrier>();

// CORS scoped to the Pay.Web origin (and whatever else gets added via ClientOrigin).
builder.Services.AddCors(options =>
{
    options.AddPolicy("PayWebClient", policy =>
    {
        var origins = (builder.Configuration.GetValue<string>("ClientOrigin") ?? "http://localhost:5252")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();

    // Schema strategy:
    //   Postgres (prod) → EF migrations. On first run against an existing DB
    //     that was created via EnsureCreated (before migrations were added),
    //     we baseline by inserting the InitialCreate row into the migrations
    //     history so Migrate() skips tables that already exist.
    //   SQLite (dev fallback) → EnsureCreated. Migrations are Npgsql-scoped;
    //     SQLite devs just get the current model every run.
    if (db.Database.IsNpgsql())
    {
        // Baseline: if the Sessions table exists but __EFMigrationsHistory is
        // empty (or missing), this DB came from EnsureCreated. Stamp the
        // initial migration as applied so Migrate() treats it as a no-op.
        try
        {
            var sessionsExists = (await db.Database
                .SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_tables WHERE tablename = 'Sessions') AS \"Value\"")
                .ToListAsync()).FirstOrDefault();
            if (sessionsExists)
            {
                var applied = await db.Database.GetAppliedMigrationsAsync();
                if (!applied.Any())
                {
                    var initial = (await db.Database.GetPendingMigrationsAsync()).FirstOrDefault();
                    if (initial is not null)
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" VARCHAR(150) PRIMARY KEY, \"ProductVersion\" VARCHAR(32) NOT NULL)");
                        await db.Database.ExecuteSqlRawAsync(
                            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
                            initial, "10.0.5");
                    }
                }
            }
        }
        catch
        {
            // Baseline is best-effort; if it fails Migrate() will surface a
            // clear error on the next line.
        }
        await db.Database.MigrateAsync();
    }
    else
    {
        db.Database.EnsureCreated();
    }

    // Legacy schema shims — safety net for dev DBs that were created before
    // the corresponding column was added AND haven't been dropped. On a DB
    // created from migrations these are all no-ops. Remove once no dev has
    // a pre-migration DB lying around.
    foreach (var col in new[]
    {
        "ALTER TABLE \"WebhookDeliveries\" ADD COLUMN \"NextRetryAt\" TIMESTAMP NULL",
        "ALTER TABLE \"Sessions\" ADD COLUMN \"Source\" VARCHAR(32) NULL",
        "ALTER TABLE \"Sessions\" ADD COLUMN \"RefundTxid\" VARCHAR(128) NULL",
        "ALTER TABLE \"Sessions\" ADD COLUMN \"RefundedAt\" TIMESTAMP NULL",
        "ALTER TABLE \"Sessions\" ADD COLUMN \"RefundNote\" VARCHAR(512) NULL",
    })
    {
        try { await db.Database.ExecuteSqlRawAsync(col); } catch { }
    }

    // Backfill: legacy merchants had their API key on PayMerchant.ApiKeyPrefix/Hash.
    // Seed a PayApiKey row for any merchant that doesn't yet have one, so existing
    // SDK integrations keep working after the auth lookup moved to PayApiKeys.
    // Skips placeholder rows created by post-refactor Digi-ID sign-ins (prefix like
    // "legacy_unused_*") — those merchants deliberately have no API key yet.
    var backfillNeeded = await db.Merchants
        .Where(m => !m.ApiKeyPrefix.StartsWith("legacy_unused_"))
        .Where(m => !db.ApiKeys.Any(k => k.Prefix == m.ApiKeyPrefix))
        .ToListAsync();
    foreach (var m in backfillNeeded)
    {
        db.ApiKeys.Add(new DigiByte.Pay.Api.Data.PayApiKey
        {
            Id = $"key_bfill{m.Id[^10..]}",
            MerchantId = m.Id,
            Prefix = m.ApiKeyPrefix,
            Hash = m.ApiKeyHash,
            Label = "Initial key (backfilled)",
        });
    }
    if (backfillNeeded.Count > 0) await db.SaveChangesAsync();
}

// OpenAPI + Scalar reference — exposed in all environments so merchants
// have a live, interactive reference for the REST API at /scalar (and
// the raw OpenAPI doc at /openapi/v1.json).
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "DigiPay API";
    options.Theme = ScalarTheme.BluePlanet;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    // Public URL of the Pay.Web favicon so the Scalar tab icon matches the
    // rest of the DigiPay surface. Falls back to the relative path if the
    // public URL isn't configured (fine in dev, Railway-internal in prod
    // won't resolve from the browser — but the tab still gets a sensible
    // default from Scalar either way).
    var favicon = app.Configuration["DigiPay:FaviconUrl"]
        ?? "https://pay.dgbwallet.app/favicon.svg";
    options.Favicon = favicon;
});

app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
});

app.UseCors("PayWebClient");
app.UseRateLimiter();

// Operational liveness probe. Excluded from the OpenAPI description — not
// something integrators build against.
app.MapGet("/api/health", () => Results.Ok(new
{
    service = "DigiByte.Pay.Api",
    status = "healthy",
    timestamp = DateTime.UtcNow,
}))
.ExcludeFromDescription();

// The OpenAPI description is the integrator reference surfaced on Scalar.
// Endpoint groups that only make sense to the dashboard or the Digi-ID
// browser flow are hidden so the doc stays focused on server-to-server use.
//
//   PUBLIC in docs (server integrators):
//     /v1/pay/*           — register + sessions (the core flow)
//     /v1/pay/stores/*    — store CRUD + webhook test/replay
//     /v1/pay/public/*    — unauthenticated stats
//     /v1/pay/test/*      — sandbox endpoints so devs can try end-to-end
//
//   HIDDEN from docs (browser/dashboard only):
//     /v1/pay/auth/*      — Digi-ID sign-in flow, only meaningful in-browser
//     /v1/pay/me          — self lookup for the signed-in merchant session
//     /v1/pay/api-keys/*  — managed via the dashboard, not integrators
// Tags drive Scalar's left-side navigation grouping. The default behaviour
// lumps everything under the assembly name — naming each group puts the
// integrator-facing surface into meaningful sections.
app.MapGroup("/v1/pay").MapPaymentsEndpoints(app.Configuration).WithTags("Sessions");
app.MapGroup("/v1/pay/auth").MapAuthEndpoints(app.Configuration).ExcludeFromDescription().RequireRateLimiting("auth");
app.MapGroup("/v1/pay/me").MapMerchantMeEndpoints().ExcludeFromDescription();
app.MapGroup("/v1/pay/stores").MapStoresEndpoints().WithTags("Stores & webhooks");
app.MapGroup("/v1/pay/api-keys").MapApiKeysEndpoints().ExcludeFromDescription();
// POS device pairing. /claim is unauthed (it's how a new tablet bootstraps)
// but rate-limited to blunt brute-force on the 6-char pairing code; the
// other routes require a merchant-authed token and are management-only.
app.MapGroup("/v1/pay/pos").MapPosEndpoints().ExcludeFromDescription().RequireRateLimiting("claim");
// Dev-only: lets the POS kiosk exercise its confirmed / expired / underpaid
// result screens without a real chain transaction. Merchant-authed so it
// can only flip the caller's own sessions. Never registered in Production.
if (app.Environment.IsDevelopment())
{
    app.MapGroup("/v1/pay/pos").MapPosDevOnlyEndpoints().ExcludeFromDescription();
    app.MapGroup("/v1/pay/auth").MapDevOnlyAuthEndpoints().ExcludeFromDescription();
}
app.MapGroup("/v1/pay/public").MapPublicEndpoints().WithTags("Public");
// Sandbox/demo endpoints. These are unauthenticated on purpose (they back
// the /embed/demo.html harness so visitors can see the checkout flow end-to-end).
// Safety comes from the endpoints themselves: /advance refuses any id that
// isn't a ses_demo_* session, so real merchant sessions can't be flipped.
app.MapGroup("/v1/pay/test").MapTestEndpoints().WithTags("Sandbox").RequireRateLimiting("sandbox");
app.MapHub<CheckoutHub>("/hubs/checkout");

app.Run();

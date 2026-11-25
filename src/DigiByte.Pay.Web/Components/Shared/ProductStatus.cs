namespace DigiByte.Pay.Web.Components.Shared;

public enum RoadmapStage { Shipped, Next, Planned }

/// <summary>One roadmap entry. <see cref="Title"/> is the short name; <see cref="Description"/> one plain sentence.</summary>
public sealed record RoadmapItem(string Title, string Description, RoadmapStage Stage);

/// <summary>
/// The one place DigiPay's release stage and roadmap are written down. The full list renders on
/// /features#roadmap; the home page shows the newest shipped items and links there; the About page
/// shows <see cref="Stage"/>. Change status here only, so the pages can't disagree.
/// Keep dates out: a "Q3" label goes stale the day the quarter ends.
/// </summary>
public static class ProductStatus
{
    /// <summary>Release stage shown as a badge (About page).</summary>
    public const string Stage = "v1 · developer preview";

    /// <summary>Where the full roadmap lives.</summary>
    public const string RoadmapHref = "/features#roadmap";

    /// <summary>Newest first within each stage.</summary>
    public static readonly RoadmapItem[] Items =
    [
        new("WooCommerce plugin", "Drop-in WordPress plugin: Blocks checkout, fiat-priced orders, signed webhooks with a replay window, and a redirect back to the thank-you page.", RoadmapStage.Shipped),
        new("Idempotency keys + Postman collection", "Send an Idempotency-Key header and a retried create returns the same session for 24 hours. A curated Postman 2.1 collection covers every endpoint.", RoadmapStage.Shipped),
        new("Payment links (no code)", "Mint a shareable checkout link from the dashboard, priced in DGB or fiat, with a QR code and email, SMS and share buttons.", RoadmapStage.Shipped),
        new("Sample stores", "Single-file mini-stores for Express, Flask and ASP.NET Core: checkout plus a verified webhook.", RoadmapStage.Shipped),
        new("Automatic webhook retries", "Failed deliveries retry after 1 min, 5 min, 30 min, 2 h and 6 h, then stop and wait for a manual replay.", RoadmapStage.Shipped),
        new("Refunds and voids", "Record an off-platform refund (you send the DGB, DigiPay stores the txid) or void an unpaid session. Both send webhooks.", RoadmapStage.Shipped),
        new("Audit log", "Append-only record of refunds, voids, store edits and API key changes, with the actor and IP address.", RoadmapStage.Shipped),
        new("Dashboard analytics", "KPI tiles, a daily outcome chart and a DGB volume chart over 7, 30 or 90 days.", RoadmapStage.Shipped),
        new("POS mode", "Pair a tablet with a 6-character code; amount pad, tip presets and shift-close reports.", RoadmapStage.Shipped),
        new("Official SDKs", "Node (npm), Python (PyPI) and .NET (NuGet) clients with the same surface, the same webhook verifier and the same error type.", RoadmapStage.Shipped),
        new("CSV export", "Download sessions and webhook deliveries as CSV (?format=csv on both list endpoints, and a button in the dashboard).", RoadmapStage.Shipped),
        new("API test suite", "Automated tests for webhook delivery, registration and rate limits, run in CI.", RoadmapStage.Shipped),
        new("Production hardening", "Postgres deployment with migrations, rate limits, OpenAPI 3.1 with a Scalar explorer, and a daily sweep of sandbox data.", RoadmapStage.Shipped),
        new("Core gateway", "Digi-ID sign-in, many stores per account, xpub or single-address receive, hosted checkout with live status, signed webhooks with a delivery log, API keys, donation button, JS embed and 3-day late-payment reconciliation.", RoadmapStage.Shipped),

        new("Email receipts", "Optional customer email on session.confirmed with the txid and amount, switched on per store.", RoadmapStage.Next),
        new("More CSV exports", "Shift reports, analytics windows and the audit log as downloads.", RoadmapStage.Next),
        new("Per-key scopes", "Limit an API key to reading sessions, or to creating them.", RoadmapStage.Next),
        new("Telegram payment notifications", "Per-store bot and chat, fed by the existing webhook dispatcher.", RoadmapStage.Next),
        new("Invoice activity log", "Per-invoice history: who viewed it, when it was paid, and which webhooks were delivered.", RoadmapStage.Next),
        new("Refund helper","Generate the outgoing payment request for a refund; you still sign it in your own wallet.", RoadmapStage.Next),

        new("digipay listen", "A local tunnel that forwards verified webhook events to localhost, shipped through npm, PyPI and NuGet.", RoadmapStage.Planned),
        new("POS offline queue", "The tablet queues sales while the API is unreachable and sends them on reconnect.", RoadmapStage.Planned),
        new("Translations", "Localised text across the website, checkout and POS.", RoadmapStage.Planned),
        new("Payout sweeps", "Consolidate per-invoice addresses into one treasury address.", RoadmapStage.Planned),
        new("Shopify app", "A Shopify payment integration alongside the WooCommerce plugin.", RoadmapStage.Planned),
        new("Subscriptions", "Recurring invoices with re-authorisation.", RoadmapStage.Planned),
        new("DigiAssets payments", "Payment flows for DigiAssets.", RoadmapStage.Planned),
        new("Telegram Mini App", "Manage sessions and invoices from inside Telegram.", RoadmapStage.Planned),
    ];

    public static IEnumerable<RoadmapItem> InStage(RoadmapStage stage) => Items.Where(i => i.Stage == stage);
}

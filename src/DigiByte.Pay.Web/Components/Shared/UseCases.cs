namespace DigiByte.Pay.Web.Components.Shared;

/// <summary>A link to the part of DigiPay a use case relies on.</summary>
public sealed record UseCaseTool(string Label, string Href);

/// <summary>One way to use DigiPay, shown on the home page (short) and on /use-cases (in full).</summary>
/// <param name="Key">Anchor id on /use-cases.</param>
/// <param name="Audience">Who it is for, in a few words.</param>
/// <param name="Summary">One sentence.</param>
/// <param name="Points">What DigiPay does for them. Only things the product does today.</param>
public sealed record UseCase(
    string Key, string Icon, string Title, string Audience, string Summary,
    string[] Points, UseCaseTool[] Tools, string CtaLabel, string CtaHref);

/// <summary>The use cases, in display order. One list, so the home page and /use-cases can't disagree.</summary>
public static class UseCases
{
    public static readonly IReadOnlyList<UseCase> All =
    [
        new("online-stores", "package", "Online stores", "Shops, marketplaces, checkouts",
            "Add DigiByte at checkout with a hosted payment page, a pop-up on your own site, or the WooCommerce plugin.",
            [
                "Every order gets a fresh address from your wallet's xpub, and the money goes straight to your wallet.",
                "Customers pay on a hosted page or in a pop-up, and watch their payment confirm block by block.",
                "Signed webhooks tell your shop the moment an order is paid, with automatic retries.",
                "Price in your shop's own currency; it is converted to DGB at the live rate.",
            ],
            [new("WooCommerce plugin", "/docs/woocommerce"), new("Hosted checkout", "/docs#checkout"), new("Webhooks", "/docs#webhooks")],
            "Set up your store", "/merchant/signin"),

        new("cafes-and-shops", "store", "Cafés, shops and restaurants", "Counters and tills",
            "Turn any tablet or phone into a till that takes DigiByte at the counter.",
            [
                "Pair a tablet with a 6-character code from your dashboard; there's no sign-in at opening time.",
                "Ring up in pesos, euros, naira or any of about 80 currencies; the till converts to DGB live.",
                "Tips, quick amounts, a receipt the customer can keep, and a shift report at closing.",
                "Each tablet can only take payments for its own store.",
            ],
            [new("POS guide", "/docs/pos"), new("Features tour", "/features")],
            "Read the POS guide", "/docs/pos"),

        new("freelancers", "link", "Freelancers and invoices", "Designers, developers, consultants",
            "Send a payment link for an invoice and get paid in DGB, priced in your own currency.",
            [
                "Make a payment link in your currency and share it by chat, email or QR code.",
                "Your client pays from any DigiByte wallet; you see it arrive in your dashboard.",
                "A receipt with a downloadable PDF, for your client and for your books.",
                "A payment that arrives up to 3 days after the link expired is still matched to it.",
            ],
            [new("Payment links", "/merchant/links"), new("Receipts", "/docs#checkout")],
            "Create a payment link", "/merchant/links"),

        new("creators", "gift", "Creators and nonprofits", "Streamers, writers, open source, causes",
            "Put a tip or donation button on your site, stream or link page.",
            [
                "Supporters need no account: they scan or tap and pay from any DigiByte wallet.",
                "Suggest an amount or let supporters choose their own.",
                "Every tip goes straight to your wallet. DigiPay never holds it.",
            ],
            [new("Tip button", "/docs#donation-button")],
            "Get a tip button", "/docs#donation-button"),

        new("apps", "code", "Apps, SaaS and games", "Developers and platforms",
            "Take DigiByte payments from your own backend with a plain REST API.",
            [
                "Create a payment from your server, send the customer to the checkout or embed it, and confirm on a signed webhook.",
                "SDKs for Node, Python and .NET, and an OpenAPI reference.",
                "Test the whole flow on testnet: move a payment along and receive real webhooks, without spending DGB.",
                "Separate stores and API keys per product, an audit log and CSV exports.",
            ],
            [new("Quickstart", "/docs#quickstart"), new("API reference", "/docs/api"), new("SDKs", "/sdks"), new("Testing", "/docs#testing")],
            "Start the quickstart", "/docs#quickstart"),

        new("events", "users", "Markets, events and pop-ups", "Stalls, ticket desks, fundraisers",
            "Take DigiByte at a stall or an event with phones you already have.",
            [
                "Run several stores from one account (a merch table, a ticket desk, a tip jar), each with its own address and webhook.",
                "Use a phone as a till with the compact layout, or show customers a full-screen QR code.",
                "A new till in a minute: generate a code, type it in, start selling.",
                "Totals per store at the end of the day.",
            ],
            [new("POS guide", "/docs/pos"), new("Stores", "/docs#stores")],
            "Read the POS guide", "/docs/pos"),
    ];
}

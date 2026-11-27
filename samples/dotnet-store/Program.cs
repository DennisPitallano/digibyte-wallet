// End-to-end DigiPay sample: a one-product mini-store on ASP.NET Core.
//
// Flow:
//   GET  /                   → browse the product, click "Buy"
//   POST /buy                → create a DigiPay session, redirect to its CheckoutUrl
//   POST /digipay-webhook    → verified webhook receiver; flips the in-memory order
//   GET  /orders/{id}        → "your order" page that polls until the webhook fires
//
// Single-file Minimal API for clarity. In a real app you'd use MVC views or
// Razor Pages, persist orders in a DB, and add idempotency keys.
//
// Run with:
//   set DIGIPAY_KEY=dgp_…
//   set DIGIPAY_SECRET=…
//   set PUBLIC_URL=http://localhost:3000
//   dotnet run

using System.Collections.Concurrent;
using DigiPay;
using static HtmlLayout;

var KEY = Environment.GetEnvironmentVariable("DIGIPAY_KEY")
    ?? throw new InvalidOperationException("Set DIGIPAY_KEY (see README.md)");
var SECRET = Environment.GetEnvironmentVariable("DIGIPAY_SECRET")
    ?? throw new InvalidOperationException("Set DIGIPAY_SECRET (see README.md)");
var PUBLIC_URL = (Environment.GetEnvironmentVariable("PUBLIC_URL") ?? "http://localhost:3000").TrimEnd('/');

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => new DigiPayClient(apiKey: KEY));
// Concurrent so the webhook handler can mutate while the order page reads.
builder.Services.AddSingleton<ConcurrentDictionary<string, Order>>();

var app = builder.Build();

// One product. In a real store this would come from a DB.
var product = new { Id = "sku-tee", Name = "DigiByte tee", Price = 12.5m }; // 12.5 DGB

app.MapGet("/", () => Results.Content(Layout($"""
    <h1>{product.Name}</h1>
    <p class="price">{product.Price} DGB</p>
    <form method="POST" action="/buy">
        <button type="submit">Buy with DigiByte</button>
    </form>
"""), "text/html"));

app.MapPost("/buy", async (DigiPayClient digipay, ConcurrentDictionary<string, Order> orders) =>
{
    try
    {
        var session = await digipay.Sessions.CreateAsync(new CreateSessionRequest
        {
            Amount = product.Price,
            Label = product.Name,
            Memo = $"sku={product.Id}",
        });
        // Track our local order keyed by sessionId — the webhook will look it up.
        orders[session.Id] = new Order(session.Id, "pending", null);
        // Redirect the customer to DigiPay's hosted checkout — they pay there.
        return Results.Redirect(session.CheckoutUrl);
    }
    catch (DigiPayError ex)
    {
        var status = ex.Status > 0 ? ex.Status : 500;
        return Results.Text($"DigiPay rejected the session: {ex.Message}", statusCode: status);
    }
});

app.MapGet("/orders/{id}", (string id, ConcurrentDictionary<string, Order> orders) =>
{
    if (!orders.TryGetValue(id, out var order)) return Results.NotFound("order not found");

    var txidLine = order.Txid is null ? "" : $"<p>Tx: <code>{order.Txid}</code></p>";
    var idJson = System.Text.Json.JsonSerializer.Serialize(id);
    // $$"""…""" — interpolation uses {{ }} so single { } stay literal for the
    // inline JS below. Saves doubling every JS brace.
    return Results.Content(Layout($$"""
        <h1>Order {{id[^8..]}}</h1>
        <p>Status: <b id="status">{{order.Status}}</b></p>
        {{txidLine}}
        <script>
            // Poll the local order endpoint until terminal — saves wiring SignalR.
            const id = {{idJson}};
            setInterval(async () => {
                const r = await fetch('/orders/' + id + '.json');
                const j = await r.json();
                document.getElementById('status').textContent = j.status;
                if (['paid','confirmed','expired','underpaid'].includes(j.status)) location.reload();
            }, 2000);
        </script>
    """), "text/html");
});

app.MapGet("/orders/{id}.json", (string id, ConcurrentDictionary<string, Order> orders) =>
    orders.TryGetValue(id, out var o) ? Results.Json(new { status = o.Status, txid = o.Txid }) : Results.NotFound());

// CRITICAL: webhook needs the *raw bytes*, not a parsed JsonDocument, because
// the HMAC covers the bytes as the server sent them. We read the body stream
// straight into a buffer; ASP.NET's default model binders would normalise
// whitespace and break verification.
app.MapPost("/digipay-webhook", async (HttpRequest req, ConcurrentDictionary<string, Order> orders, ILogger<Program> log) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var rawBody = ms.ToArray();

    WebhookEvent evt;
    try
    {
        evt = WebhookVerifier.Verify(rawBody, req.Headers["X-DigiPay-Signature"], SECRET);
    }
    catch (DigiPayError ex)
    {
        // 401 on bad signature, 400 on malformed body. No detail leaked.
        return Results.StatusCode(ex.Status > 0 ? ex.Status : 400);
    }

    if (!orders.TryGetValue(evt.Session.Id, out var order))
    {
        // Webhook for an order we don't know — ack to stop retries, log for debugging.
        log.LogWarning("webhook for unknown session {SessionId}", evt.Session.Id);
        return Results.Ok();
    }

    // Map DigiPay event names to your local status. Unknown events ack 200 so
    // forward-compatible events don't trigger delivery failures.
    var newStatus = evt.Event switch
    {
        "session.paid" => "paid",
        "session.confirmed" => "confirmed",
        "session.expired" => "expired",
        "session.underpaid" => "underpaid",
        _ => null,
    };
    if (newStatus is not null)
    {
        orders[evt.Session.Id] = order with { Status = newStatus, Txid = evt.Session.PaidTxid };
        log.LogInformation("{Status}: {Amount} DGB for {SessionId}",
            newStatus, evt.Session.Amount, evt.Session.Id);
    }

    return Results.Ok();
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"mini-store on {PUBLIC_URL}");
    Console.WriteLine("Webhook URL to register on the DigiPay store:");
    Console.WriteLine($"  {PUBLIC_URL}/digipay-webhook");
});

app.Run();

// ---------- types & helpers ----------

public record Order(string SessionId, string Status, string? Txid);

internal static class HtmlLayout
{
    public static string Layout(string body) => $$"""
        <!doctype html>
        <meta charset="utf-8">
        <title>DigiByte tee — sample store</title>
        <style>
            body { font-family: system-ui, sans-serif; max-width: 32rem; margin: 4rem auto; padding: 0 1rem; }
            h1 { font-size: 1.5rem; }
            .price { font-size: 2rem; font-weight: bold; color: #0062cc; }
            button { padding: .75rem 1.25rem; background: #0062cc; color: white; border: 0;
                     border-radius: .5rem; font-size: 1rem; font-weight: bold; cursor: pointer; }
            code { background: #f3f4f6; padding: .125rem .375rem; border-radius: .25rem; font-size: .875rem; }
        </style>
        {{body}}
        """;
}

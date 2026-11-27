# DigiPay

Official .NET SDK for **[DigiPay](https://pay.dgbwallet.app)** — accept DigiByte payments on your site without holding any funds.

- **Non-custodial.** Payments land directly in your wallet (single address or BIP84 xpub).
- **Zero external dependencies.** Just `HttpClient` + `System.Security.Cryptography` — both in-box on .NET 8+.
- **Fully typed.** `record` DTOs with nullable reference types, `TreatWarningsAsErrors` on.
- **Multi-target.** `net8.0` (LTS through Nov 2026) + `net9.0`.

```bash
dotnet add package DigiPay
```

## Quickstart

```csharp
using DigiPay;

using var dp = new DigiPayClient(Environment.GetEnvironmentVariable("DIGIPAY_KEY")!);

var session = await dp.Sessions.CreateAsync(new CreateSessionRequest
{
    Amount = 5m,
    Label = "Order #1234",
});

Console.WriteLine(session.CheckoutUrl); // → https://pay.dgbwallet.app/pay/ses_…
```

## Self-serve registration

If you don't have an API key yet, register a brand-new merchant + first store + initial key in one call:

```csharp
var merchant = await DigiPayClient.RegisterAsync(new RegisterMerchantRequest(
    DisplayName: "My Shop",
    AddressOrXpub: "dgb1q…") // or a BIP84 xpub
{
    WebhookUrl = "https://my-shop.example/digipay-webhook",
});

Console.WriteLine(merchant.ApiKey);        // dgp_… (shown once)
Console.WriteLine(merchant.WebhookSecret); // store for WebhookVerifier.Verify
```

## Webhook verification (ASP.NET Core)

DigiPay POSTs signed JSON to your `webhookUrl` on every state change. Signature is HMAC-SHA256 of the raw body, hex-encoded, in `X-DigiPay-Signature` (prefixed `sha256=`).

```csharp
using DigiPay;

app.MapPost("/digipay-webhook", async (HttpContext ctx) =>
{
    // Crucial: read the RAW bytes before any JSON model binding.
    using var ms = new MemoryStream();
    await ctx.Request.Body.CopyToAsync(ms);
    var rawBody = ms.ToArray();

    try
    {
        var evt = WebhookVerifier.Verify(
            rawBody,
            ctx.Request.Headers["X-DigiPay-Signature"].FirstOrDefault(),
            Environment.GetEnvironmentVariable("DIGIPAY_SECRET")!);

        switch (evt.Event)
        {
            case "session.paid":
                // evt.Session.Id, .Amount, .PaidTxid, etc.
                break;
            case "session.confirmed":
                // 6+ confirmations — treat as irreversible
                break;
            // Unknown event types: ack 200 and ignore so forward-compatible
            // events don't trigger delivery failures in the dashboard.
        }
        return Results.Ok();
    }
    catch (DigiPayError err)
    {
        return Results.StatusCode(err.Status);
    }
});
```

**Critical:** verify against the **raw bytes** before any JSON model binding. Every reserialisation changes the signature.

## Resources

### Sessions

```csharp
await dp.Sessions.CreateAsync(new() { Amount = 5m, Label, Memo, FiatCurrency, FiatAmount });
await dp.Sessions.GetAsync("ses_abc");
await dp.Sessions.ListAsync(new ListSessionsOptions { Status = "paid", Take = 50 });
await dp.Sessions.ExportCsvAsync();   // returns CSV text
```

### Stores

```csharp
await dp.Stores.ListAsync();
await dp.Stores.GetAsync("sto_abc");
await dp.Stores.CreateAsync(new CreateStoreRequest("Side hustle"));
await dp.Stores.UpdateAsync("sto_abc", new UpdateStoreRequest { WebhookUrl = "…" });
await dp.Stores.DeleteAsync("sto_abc");

// Webhook tooling
await dp.Stores.SendTestWebhookAsync("sto_abc");
await dp.Stores.ListDeliveriesAsync("sto_abc", take: 100);
await dp.Stores.ReplayDeliveryAsync("sto_abc", "wdel_…");
await dp.Stores.ExportDeliveriesCsvAsync("sto_abc");
```

## Errors

Every failure raises `DigiPayError` with the HTTP status preserved:

```csharp
try
{
    await dp.Sessions.CreateAsync(new CreateSessionRequest { Amount = 0m });
}
catch (DigiPayError err)
{
    Console.WriteLine(err.Status); // 400
    Console.WriteLine(err.Body);   // JsonElement: { "error": "amount (DGB) must be > 0" }
}
```

| `err.Status` | Meaning |
|---|---|
| `0` | Network / DNS / TLS / timeout |
| `400` | Validation failure — see `err.Body` |
| `401` | Missing or invalid API key / webhook signature |
| `404` | Resource not found, or not owned by this merchant |
| `429` | Rate-limited (sandbox endpoints only) |
| `>= 500` | Server-side; safe to retry with backoff |

## Configuration

```csharp
new DigiPayClient(
    apiKey: "dgp_…",
    baseUrl: "https://api.pay.dgbwallet.app",         // default
    timeout: TimeSpan.FromSeconds(15));               // default
```

For staging or self-hosted, pass an alternate `baseUrl`. For DI scenarios (`IHttpClientFactory`), use the `new DigiPayClient(HttpClient)` overload and configure auth on the supplied client yourself.

## License

MIT — see [LICENSE](../../LICENSE).

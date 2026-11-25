using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The shopper's receipt: outcome, the facts slip, the transaction proof and a Blockchair PDF
// receipt like the wallet's.
public class ReceiptRenderTests : BunitContext
{
    private const string Txid = "518485f7939bdbb8bb1332a30712c9ddb48e2f56b0e84e513aecde7fbf4b8d35";

    private void UseSession(string status, int confirmations, string? paidAt = "2026-11-25T21:42:00Z",
        string currency = "DGB", string amountCentsDd = "null", long received = 200000000)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(new StubFactory($$"""
            {"session":{"id":"ses_4jarxgkthx5hk86s","address":"dgb1qexample","amountSatoshis":{{(currency == "DD" ? 0 : 200000000)}},"amount":2,
             "fiatCurrency":null,"fiatAmount":null,"label":"Fee","memo":null,
             "status":"{{status}}","createdAt":"2026-11-25T21:30:00Z","expiresAt":"2026-11-25T22:00:00Z",
             "receivedSatoshis":{{received}},"confirmations":{{confirmations}},"paidTxid":"{{Txid}}",
             "paidAt":{{(paidAt is null ? "null" : $"\"{paidAt}\"")}},
             "uri":"digibyte:dgb1qexample?amount=2","checkoutUrl":"http://x/pay/ses_4jarxgkthx5hk86s",
             "currency":"{{currency}}","amountCentsDd":{{amountCentsDd}}},
             "merchantName":"DigiPay"}
            """));
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        AddBunitPersistentComponentState();
    }

    [Fact]
    public void A_confirmed_receipt_links_to_the_blockchair_pdf_like_the_wallet()
    {
        UseSession("confirmed", 6);
        var cut = Render<Receipt>(p => p.Add(x => x.SessionId, "ses_4jarxgkthx5hk86s"));
        cut.WaitForAssertion(() => Assert.Contains("Payment confirmed", cut.Markup));

        Assert.NotNull(cut.Find($"a[href='https://blockchair.com/digibyte/transaction/{Txid}.pdf']"));
        Assert.NotNull(cut.Find($"a[href='https://blockchair.com/digibyte/transaction/{Txid}']"));
        Assert.DoesNotContain("digiexplorer", cut.Markup);
        Assert.Equal(6, cut.FindAll(".ui-receipt-dots .is-done").Count);
        Assert.Contains("Final", cut.Find(".ui-receipt-status").TextContent);
    }

    [Fact]
    public void Paid_at_is_when_the_payment_landed_in_utc_not_when_it_was_requested()
    {
        UseSession("confirmed", 6);
        var cut = Render<Receipt>(p => p.Add(x => x.SessionId, "ses_4jarxgkthx5hk86s"));
        cut.WaitForAssertion(() => Assert.Contains("25 Nov 2026, 21:42 UTC", cut.Markup));
        Assert.DoesNotContain("21:30", cut.Markup);
    }

    [Fact]
    public void A_receipt_still_confirming_shows_its_progress()
    {
        UseSession("paid", 3);
        var cut = Render<Receipt>(p => p.Add(x => x.SessionId, "ses_4jarxgkthx5hk86s"));
        cut.WaitForAssertion(() => Assert.Contains("Payment received", cut.Markup));
        Assert.Equal(3, cut.FindAll(".ui-receipt-dots .is-done").Count);
        Assert.Contains("Confirming · 3/6", cut.Find(".ui-receipt-status").TextContent);
    }

    [Fact]
    public void An_underpaid_receipt_says_so_and_shows_what_arrived()
    {
        UseSession("underpaid", 6, received: 100000000);
        var cut = Render<Receipt>(p => p.Add(x => x.SessionId, "ses_4jarxgkthx5hk86s"));
        cut.WaitForAssertion(() => Assert.Contains("Partial payment received", cut.Markup));
        Assert.Contains("1 DGB", cut.Markup);
        Assert.DoesNotContain(">Payment received<", cut.Markup);
    }

    [Fact]
    public void A_digidollar_receipt_shows_the_usd_amount_not_zero_dgb()
    {
        UseSession("confirmed", 6, currency: "DD", amountCentsDd: "1250");
        var cut = Render<Receipt>(p => p.Add(x => x.SessionId, "ses_4jarxgkthx5hk86s"));
        cut.WaitForAssertion(() => Assert.Contains("12.50", cut.Markup));
        Assert.Contains("Paid in DigiDollar", cut.Markup);
        Assert.DoesNotContain(">0 <", cut.Markup);
    }

    private sealed class StubFactory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new JsonHandler(json)) { BaseAddress = new Uri("http://pay-api.test") };
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}

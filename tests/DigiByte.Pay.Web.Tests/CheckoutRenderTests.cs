using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// US1: the checkout uses the shared components, copy buttons copy plain values, a shop-set theme
// reaches the receipt link, and shopper pages never show Baku (clarification 2).
public class CheckoutRenderTests : BunitContext
{
    private void UseSession(string status, string? txid = null, string? returnUrl = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        // An unroutable host: the live connection fails fast and the page falls back to polling.
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton<IHttpClientFactory>(new StubFactory(Json(status, txid, returnUrl)));
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        AddBunitPersistentComponentState();
    }

    private static string Json(string status, string? txid, string? returnUrl) => $$"""
        {"session":{"id":"ses_abc","address":"dgb1qexampleaddress0000000000000000000000","amountSatoshis":1234500000000,"amount":12345,
         "fiatCurrency":null,"fiatAmount":null,"dgbPriceAtCreation":null,"label":"Order 1001","memo":null,
         "status":"{{status}}","createdAt":"2026-11-25T00:00:00Z","expiresAt":"2030-01-01T00:00:00Z",
         "receivedSatoshis":0,"confirmations":0,"paidTxid":{{(txid is null ? "null" : $"\"{txid}\"")}},
         "uri":"digibyte:dgb1qexampleaddress0000000000000000000000?amount=12345","checkoutUrl":"http://x/pay/ses_abc",
         "returnUrl":{{(returnUrl is null ? "null" : $"\"{returnUrl}\"")}},"currency":"DGB","amountCentsDd":null},
         "merchantName":"Demo Store"}
        """;

    [Fact]
    public void A_waiting_checkout_has_the_shared_qr_frame_and_copy_buttons_with_plain_values()
    {
        UseSession("pending");
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".qr-frame img[alt='Scan to pay']")));

        Assert.NotNull(cut.Find("a.btn-primary[href^='digibyte:']"));
        var copyAddress = cut.Find("button[aria-label='Copy address']");
        var copyAmount = cut.Find("button[aria-label='Copy amount']");
        Assert.NotNull(copyAddress);
        Assert.NotNull(copyAmount);

        copyAmount.Click();
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "digipayClipboard.copy");
        Assert.Equal("12345", call.Arguments[0]); // no separators, no unit

        // "Reconnecting…" once the (unreachable) live connection fails; either is correct here.
        Assert.Contains(cut.Find(".ui-badge").TextContent.Trim(), new[] { "Waiting for payment", "Reconnecting…" });
    }

    [Fact]
    public void Live_updates_are_started_once_the_session_has_loaded()
    {
        // Regression: the page used to try only on its first render, which happens while the
        // session is still loading, so live updates never started. With an unreachable hub the
        // attempt shows up as the Reconnecting state (FR-011) and is announced.
        UseSession("pending");
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.Equal("Reconnecting…", cut.Find(".ui-badge").TextContent.Trim()), TimeSpan.FromSeconds(10));
        Assert.Contains("Your payment is still being watched", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".qr-frame")); // still payable: never a final state from a dropped connection
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("confirmed")]
    [InlineData("expired")]
    public void Shopper_pages_never_show_baku(string status)
    {
        UseSession(status, txid: status == "confirmed" ? "ab12" : null);
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ui-badge")));
        Assert.DoesNotContain("mascot/poses", cut.Markup);
        Assert.DoesNotContain("baku", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_expired_checkout_shows_no_qr_code_or_address_and_offers_the_way_back()
    {
        UseSession("expired", returnUrl: "https://shop.example/cart");
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.Contains("This price has expired", cut.Markup));

        Assert.Empty(cut.FindAll(".qr-frame"));
        Assert.DoesNotContain("dgb1qexampleaddress", cut.Markup);
        Assert.NotNull(cut.Find("a.btn-primary[href='https://shop.example/cart']"));
    }

    [Fact]
    public void A_shop_set_theme_is_carried_into_the_receipt_link()
    {
        UseSession("confirmed", txid: "ab12cd34");
        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo("/pay/ses_abc?theme=Light");
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.Contains("Payment confirmed", cut.Markup));

        Assert.NotNull(cut.Find("a[href='/pay/ses_abc/receipt?theme=light']"));
    }

    [Fact]
    public void The_status_uses_no_glyph_icons()
    {
        UseSession("underpaid");
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));
        cut.WaitForAssertion(() => Assert.Contains("Too little was received", cut.Markup));
        foreach (var glyph in new[] { "✓", "⏱", "⊘", "↩", "⚠" })
            Assert.DoesNotContain(glyph, cut.Markup);
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

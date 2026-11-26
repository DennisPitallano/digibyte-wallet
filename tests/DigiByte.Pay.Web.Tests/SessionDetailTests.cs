using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The merchant's payment page: amounts in DGB, underpaid next steps, the right explorer, and
// refund/cancel errors inside the dialog in plain words.
public class SessionDetailTests : BunitContext
{
    private string status = "paid";
    private string network = "mainnet";
    private string? txid = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private long received = 100_000_000;
    private HttpStatusCode voidStatus = HttpStatusCode.OK;
    private bool twoStores;

    private IRenderedComponent<SessionDetail> RenderPage(string? back = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("localStorage.getItem", "digipay-token").SetResult("dps_test");
        Services.AddLogging();
        Services.AddScoped<ToastService>();
        Services.AddSingleton<IHttpClientFactory>(new Stub(this));
        if (back is not null)
        {
            var nav = Services.GetRequiredService<NavigationManager>();
            nav.NavigateTo(nav.GetUriWithQueryParameter("back", back));
        }
        return Render<SessionDetail>(p => p.Add(x => x.SessionId, "ses_abc"));
    }

    // ----- Inside the dashboard navigation -----

    [Fact]
    public void The_payment_page_shows_the_dashboard_navigation_with_payments_current()
    {
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));

        Assert.NotNull(cut.Find(".dp-shell .dp-rail"));
        var current = cut.FindAll(".dp-rail a[aria-current='page']");
        Assert.Equal("Payments", Assert.Single(current).TextContent.Trim());
        var bottom = cut.FindAll(".dp-bottom a[aria-current='page']");
        Assert.Equal("Payments", Assert.Single(bottom).TextContent.Trim());
        Assert.Contains("Test Shop", cut.Find(".dp-rail").TextContent);
        // The payment itself sits in the shell's content column.
        Assert.Contains("ses_abc", cut.Find(".dp-main").TextContent);
    }

    [Fact]
    public void With_several_stores_the_navigation_keeps_the_payments_store()
    {
        twoStores = true;
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));

        Assert.NotNull(cut.Find(".dp-rail a[href='/merchant/sessions?store=sto_1']"));
        Assert.Equal("sto_1", cut.Find("#store-switch-rail").GetAttribute("data-store-id"));
        Assert.Contains("Corner Café", cut.Find(".dash-topbar-store").TextContent);
    }

    [Fact]
    public void The_back_link_returns_to_the_list_it_came_from()
    {
        var cut = RenderPage(back: "/merchant/sessions?status=paid");
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));
        Assert.NotNull(cut.Find("a.back-btn[href='/merchant/sessions?status=paid']"));
    }

    [Fact]
    public void A_foreign_back_address_falls_back_to_payments()
    {
        var cut = RenderPage(back: "https://evil.example/merchant");
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));
        Assert.NotNull(cut.Find("a.back-btn[href='/merchant/sessions']"));
    }

    [Fact]
    public void An_underpaid_payment_says_how_much_is_missing_and_what_to_do()
    {
        status = "underpaid";
        received = 150_000_000;
        txid = null;
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("0.5 DGB is missing", cut.Markup));

        Assert.Contains("Received 1.5 DGB", cut.Markup);
        Assert.Contains("0.5 DGB short", cut.Markup);
        Assert.DoesNotContain("sats", cut.Markup);
        Assert.DoesNotContain("No actions available", cut.Markup);
        Assert.NotNull(cut.Find("a[href='/merchant/new']"));
    }

    [Fact]
    public void A_mainnet_transaction_links_to_blockchair()
    {
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));
        var link = cut.Find("a[href^='https://blockchair.com/digibyte/transaction/']");
        Assert.EndsWith(txid!, link.GetAttribute("href"));
        Assert.DoesNotContain("digiexplorer", cut.Markup);
    }

    [Fact]
    public void A_testnet_transaction_has_no_mainnet_explorer_link()
    {
        network = "testnet";
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Corner Café", cut.Markup));
        Assert.Empty(cut.FindAll("a[href*='blockchair']"));
    }

    [Fact]
    public void The_confirmed_step_never_shows_the_paid_time()
    {
        status = "confirmed";
        var cut = RenderPage();
        cut.WaitForAssertion(() => Assert.Contains("Confirmed (6 confirmations)", cut.Markup));
        var confirmed = cut.FindAll("li").Single(li => li.TextContent.Contains("Confirmed (6 confirmations)"));
        Assert.DoesNotContain("2026", confirmed.TextContent);
    }

    [Fact]
    public void A_failed_cancel_explains_itself_inside_the_dialog()
    {
        status = "pending";
        txid = null;
        received = 0;
        voidStatus = HttpStatusCode.Conflict;
        var cut = RenderPage();
        // Wait until the page has fully loaded (payment and the menu's account data), so a late re-render
        // can't replace the button between finding and clicking it.
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".dp-rail"));
            Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel payment");
        });

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel payment").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ui-dialog .ui-dialog-actions .btn-danger")));
        cut.Find(".ui-dialog .ui-dialog-actions .btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("may have just been paid", cut.Find(".ui-dialog").TextContent));
        Assert.DoesNotContain("409", cut.Markup);
        Assert.DoesNotContain("cannot be voided", cut.Markup);
    }

    private sealed class Stub(SessionDetailTests t) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method.Method;
            if (method == "GET" && path == "/v1/pay/sessions/ses_abc")
                return Task.FromResult(Json($$"""
                    {"session":{"id":"ses_abc","storeId":"sto_1","address":"dgb1qexample","amountSatoshis":200000000,"amount":2,
                     "fiatCurrency":null,"fiatAmount":null,"label":null,"memo":null,"status":"{{t.status}}",
                     "createdAt":"2026-11-25T10:00:00Z","expiresAt":"2026-11-25T10:30:00Z","seenAt":"2026-11-25T10:01:00Z",
                     "paidAt":"2026-11-25T10:02:00Z","receivedSatoshis":{{t.received}},"confirmations":{{(t.status == "confirmed" ? 6 : 1)}},
                     "paidTxid":{{(t.txid is null ? "null" : $"\"{t.txid}\"")}},"uri":"digibyte:dgb1qexample?amount=2",
                     "checkoutUrl":"http://pay.test/pay/ses_abc"},"merchantName":"Test Shop"}
                    """));
            if (method == "GET" && path == "/v1/pay/me/")
                return Task.FromResult(Json($$"""
                    {"id":"mer_1","displayName":"Test Shop","digiIdAddress":null,"stores":[
                     {"id":"sto_1","name":"Corner Café","network":"{{t.network}}"}{{(t.twoStores ? ",{\"id\":\"sto_2\",\"name\":\"Market Stall\",\"network\":\"mainnet\"}" : "")}}]}
                    """));
            if (method == "GET" && path == "/v1/pay/stores/sto_1")
                return Task.FromResult(Json($$"""{"id":"sto_1","name":"Corner Café","network":"{{t.network}}"}"""));
            if (path.StartsWith("/v1/pay/stores/sto_1/webhook-deliveries"))
                return Task.FromResult(Json("[]"));
            if (method == "POST" && path == "/v1/pay/sessions/ses_abc/void")
                return Task.FromResult(new HttpResponseMessage(t.voidStatus)
                {
                    Content = new StringContent("""{"error":"session is paid and cannot be voided"}""", Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

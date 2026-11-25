using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The POS till: the confirming view once paid, final states from CheckoutStates (voided too),
// cancelling voids the session, a dropped live connection keeps watching, and the currency picker.
public class PosRenderTests : BunitContext
{
    private readonly PayApi api = new();

    private void UsePairedTill()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<Pos.PairedDevice?>("digipayPos.load")
            .SetResult(new Pos.PairedDevice("dgp_test_secret", "sto_1", "Corner Shop", "Demo Merchant", "POS — Till 1"));
        Services.AddLogging();
        // An unroutable host: the live connection fails fast and the till falls back to polling.
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
    }

    private IRenderedComponent<Pos> RenderKeypad()
    {
        UsePairedTill();
        var cut = Render<Pos>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.dp-pos-charge")));
        return cut;
    }

    private static void Type(IRenderedComponent<Pos> cut, string digits)
    {
        foreach (var d in digits)
            cut.FindAll("button.dp-pos-key").First(b => b.TextContent.Trim() == d.ToString()).Click();
    }

    private static IRenderedComponent<Pos> Charge(IRenderedComponent<Pos> cut, string digits = "12")
    {
        Type(cut, digits);
        cut.Find("button.dp-pos-charge").Click();
        return cut;
    }

    [Fact]
    public void A_paid_sale_replaces_the_qr_code_with_confirmation_progress()
    {
        api.Status = "paid";
        api.Confirmations = 2;
        api.Txid = "ab12cd34";
        var cut = Charge(RenderKeypad());

        cut.WaitForAssertion(() => Assert.Contains("Confirming your payment", cut.Markup), TimeSpan.FromSeconds(10));
        Assert.Empty(cut.FindAll(".qr-frame"));
        Assert.Contains("2 of 6", cut.Find(".ui-confirm-panel-value").TextContent);
        Assert.Empty(cut.FindAll("[data-digipay-expires]"));
        // Paid: the money is the merchant's now, so the sale can't be cancelled from the till.
        Assert.Empty(cut.FindAll("button.dp-pos-cancel"));
    }

    [Fact]
    public void A_waiting_sale_says_amount_due_and_lets_the_countdown_script_replace_the_expiry_line()
    {
        var cut = Charge(RenderKeypad());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-frame")));
        Assert.Contains("Amount due", cut.Markup);
        Assert.DoesNotContain("Asking", cut.Markup);
        Assert.NotNull(cut.Find("[data-digipay-countdown] [data-digipay-expires]"));
        Assert.Equal("Waiting for payment", cut.Find(".ui-badge").TextContent.Trim());
    }

    [Fact]
    public void A_dropped_live_connection_shows_reconnecting_and_keeps_the_sale_open()
    {
        var cut = Charge(RenderKeypad());

        cut.WaitForAssertion(() => Assert.Contains("Reconnecting… payment is still being watched.", cut.Markup), TimeSpan.FromSeconds(10));
        Assert.NotEmpty(cut.FindAll(".qr-frame"));
        Assert.Empty(cut.FindAll(".dp-pos-result"));
    }

    [Theory]
    [InlineData("voided", "Cancelled by the merchant")]
    [InlineData("refunded", "Payment refunded")]
    [InlineData("expired", "This price has expired")]
    public void Every_final_status_ends_the_sale_with_its_heading(string status, string heading)
    {
        api.Status = status;
        var cut = Charge(RenderKeypad());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dp-pos-result")), TimeSpan.FromSeconds(10));
        Assert.Equal(heading, cut.Find(".dp-pos-result h2").TextContent.Trim());
        Assert.Empty(cut.FindAll(".qr-frame"));
        Assert.Equal(status != "refunded", cut.FindAll("button.dp-pos-again").Count == 1);
    }

    [Fact]
    public void An_underpaid_sale_shows_received_against_expected()
    {
        api.Status = "underpaid";
        api.Received = 500_000_000;
        var cut = Charge(RenderKeypad());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dp-pos-result .ui-terminal-amounts")), TimeSpan.FromSeconds(10));
        var amounts = cut.Find(".ui-terminal-amounts").TextContent;
        Assert.Contains("Received", amounts);
        Assert.Contains("5 DGB", amounts);
        Assert.Contains("12 DGB", amounts);
        Assert.Contains("Charge again", cut.Markup);
    }

    [Fact]
    public void Cancel_sale_voids_the_session_with_the_device_key()
    {
        var cut = Charge(RenderKeypad());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.dp-pos-cancel")));

        cut.Find("button.dp-pos-cancel").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.dp-pos-charge")));
        var call = Assert.Single(api.Voids);
        Assert.Equal("/v1/pay/sessions/ses_pos/void", call.Path);
        Assert.Equal("Bearer dgp_test_secret", call.Auth);
    }

    [Fact]
    public void Cancelling_a_detected_payment_asks_first()
    {
        api.Status = "seen";
        var cut = Charge(RenderKeypad());
        cut.WaitForAssertion(() => Assert.Equal("Payment detected", cut.Find(".dp-pos-status").TextContent.Trim()), TimeSpan.FromSeconds(10));
        // Detected but not yet in a block: still cancellable, and the waiting view is the confirming one.
        Assert.NotEmpty(cut.FindAll(".ui-confirm"));

        cut.Find("button.dp-pos-cancel").Click();

        cut.WaitForAssertion(() => Assert.Contains("Cancel this sale?", cut.Markup));
        Assert.Empty(api.Voids);
    }

    [Fact]
    public void The_currency_button_opens_a_picker_and_keeps_an_amount_that_fits()
    {
        var cut = RenderKeypad();
        Type(cut, "12");
        var button = cut.Find("button.dp-pos-currency");
        Assert.Equal("Currency: DGB, change", button.GetAttribute("aria-label"));

        button.Click();
        Assert.Equal(11, cut.FindAll(".dp-pos-currency-option").Count);
        cut.FindAll(".dp-pos-currency-option").First(b => b.TextContent.Contains("EUR")).Click();

        Assert.Empty(cut.FindAll(".dp-pos-currency-option"));
        Assert.Equal("Currency: EUR, change", cut.Find("button.dp-pos-currency").GetAttribute("aria-label"));
        Assert.Contains("12", cut.Find(".dp-pos-amount").TextContent);
    }

    [Fact]
    public void A_currency_that_cannot_hold_the_amount_asks_before_clearing_it()
    {
        var cut = RenderKeypad();
        cut.FindAll("button.dp-pos-key").First(b => b.GetAttribute("aria-label") == "Decimal point").Click();
        Type(cut, "5");

        cut.Find("button.dp-pos-currency").Click();
        cut.FindAll(".dp-pos-currency-option").First(b => b.TextContent.Contains("JPY")).Click();

        Assert.Contains("Switching clears the amount", cut.Markup);
        Assert.Contains("0.5", cut.Find(".dp-pos-amount").TextContent);
    }

    [Fact]
    public void Tip_buttons_report_whether_they_are_pressed()
    {
        var cut = RenderKeypad();
        Type(cut, "10");
        var tip = cut.FindAll("button.dp-pos-tip").First(b => b.TextContent.Trim() == "10%");
        Assert.Equal("false", tip.GetAttribute("aria-pressed"));
        tip.Click();
        Assert.Equal("true", cut.FindAll("button.dp-pos-tip").First(b => b.TextContent.Trim() == "10%").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Pairing_fields_have_real_labels()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        var cut = Render<Pos>();

        Assert.NotNull(cut.Find("label[for='pos-pair-code']"));
        Assert.Null(cut.Find("#pos-pair-code").GetAttribute("aria-label"));
    }

    // A stand-in for the Pay API: creates a 12 DGB session and reports it with the configured status.
    private sealed class PayApi : IHttpClientFactory
    {
        public string Status { get; set; } = "pending";
        public int Confirmations { get; set; }
        public long Received { get; set; }
        public string? Txid { get; set; }
        public List<(string Path, string? Auth)> Voids { get; } = new();

        public HttpClient CreateClient(string name) => new(new Handler(this)) { BaseAddress = new Uri("http://pay-api.test") };

        private string Session(string status) => $$"""
            {"id":"ses_pos","address":"dgb1qtilladdress000000000000000000000000","amountSatoshis":1200000000,"amount":12,
             "fiatCurrency":null,"fiatAmount":null,"label":null,"memo":null,"status":"{{status}}",
             "createdAt":"2026-11-26T00:00:00Z","expiresAt":"2030-01-01T00:00:00Z",
             "receivedSatoshis":{{Received}},"confirmations":{{Confirmations}},"paidTxid":{{(Txid is null ? "null" : $"\"{Txid}\"")}},
             "uri":"digibyte:dgb1qtilladdress000000000000000000000000?amount=12","checkoutUrl":"http://x/pay/ses_pos"}
            """;

        private sealed class Handler(PayApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                string json;
                if (request.Method == HttpMethod.Post && path == "/v1/pay/sessions")
                    json = api.Session("pending");
                else if (request.Method == HttpMethod.Post && path.EndsWith("/void"))
                {
                    lock (api.Voids) api.Voids.Add((path, request.Headers.Authorization?.ToString()));
                    json = api.Session("voided");
                }
                else if (request.Method == HttpMethod.Get && path == "/v1/pay/sessions/ses_pos")
                    json = $$"""{"session":{{api.Session(api.Status)}},"merchantName":"Demo Merchant"}""";
                else
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }
    }
}

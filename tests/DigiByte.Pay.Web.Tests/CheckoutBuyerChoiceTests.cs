using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// specs/033 US3 (T040): the buyer's DGB/DigiDollar switch on the hosted checkout.
public class CheckoutBuyerChoiceTests : BunitContext
{
    private readonly ChoiceApi api = new();

    private IRenderedComponent<Checkout> Render()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        AddBunitPersistentComponentState();
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_choice"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".qr-frame")));
        return cut;
    }

    private static IEnumerable<AngleSharp.Dom.IElement> SwitchButton(IRenderedComponent<Checkout> cut, string text) =>
        cut.FindAll("[data-pay-switch] button").Where(b => b.TextContent.Trim() == text);

    private static string Address(IRenderedComponent<Checkout> cut) => cut.Find("#checkout-address").TextContent.Trim();

    [Fact]
    public void The_buyer_can_switch_to_DigiDollar_and_back_and_the_DD_address_is_made_once()
    {
        var cut = Render();
        Assert.Equal(ChoiceApi.DgbAddress, Address(cut));

        SwitchButton(cut, "DigiDollar").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(ChoiceApi.DdAddress, Address(cut)));
        Assert.Contains("25.00", cut.Find("#checkout-amount").TextContent);
        Assert.Contains("DigiDollar payment", cut.Markup);

        SwitchButton(cut, "DGB").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(ChoiceApi.DgbAddress, Address(cut)));
        SwitchButton(cut, "DigiDollar").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(ChoiceApi.DdAddress, Address(cut)));

        Assert.Equal(1, api.OptionPosts);
    }

    [Fact]
    public void A_converted_DD_amount_shows_the_rate_it_was_worked_out_at()
    {
        api.FiatCurrency = "PHP";
        api.FiatAmount = 1250m;
        api.DdCents = 2148;
        api.UsdRate = 58.2m;
        var cut = Render();

        SwitchButton(cut, "DigiDollar").Single().Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-rate-line]")));
        Assert.Equal($"{Fiat.Format(1250m, "PHP")} at {Fiat.Format(58.2m, "PHP")} per US dollar", cut.Find("[data-rate-line]").TextContent.Trim());
        Assert.Contains("21.48", cut.Find("#checkout-amount").TextContent);
    }

    [Fact]
    public void A_refused_switch_stays_on_DGB_with_a_plain_message()
    {
        api.OptionError = (HttpStatusCode.Conflict, """{"error":"dd_option_unavailable","reason":"no_rate"}""");
        var cut = Render();

        SwitchButton(cut, "DigiDollar").Single().Click();

        cut.WaitForAssertion(() => Assert.Contains("DigiDollar isn't available for this payment right now.", cut.Find("[data-switch-message]").TextContent));
        Assert.Equal(ChoiceApi.DgbAddress, Address(cut));
    }

    [Fact]
    public void DigiDollar_is_not_offered_while_it_is_unavailable()
    {
        api.DdAvailable = false;
        var cut = Render();

        Assert.Single(SwitchButton(cut, "DGB"));
        Assert.Empty(SwitchButton(cut, "DigiDollar"));
    }

    [Fact]
    public void A_buyer_on_DD_goes_back_to_DGB_when_DigiDollar_is_turned_off_before_paying()
    {
        api.StartedOnDd = true; // a DD-default store: the payment starts on DD, unlocked
        api.DdAvailable = false;
        var cut = Render();

        Assert.Equal(ChoiceApi.DgbAddress, Address(cut));
        Assert.Contains("back to DGB", cut.Find("[data-switch-message]").TextContent);
        Assert.Empty(SwitchButton(cut, "DigiDollar"));
    }

    [Fact]
    public void A_locked_payment_shows_its_currency_with_no_switch()
    {
        api.LockedToDd = true;
        var cut = Render();

        Assert.Empty(cut.FindAll("[data-pay-switch]"));
        Assert.Equal(ChoiceApi.DdAddress, Address(cut));
    }

    // A Pay API that answers GET /v1/pay/sessions/ses_choice and POST …/options/dd.
    private sealed class ChoiceApi : HttpMessageHandler, IHttpClientFactory
    {
        public const string DgbAddress = "dgb1qchoicedgbaddress00000000000000000000";
        public const string DdAddress = "DDchoice0000001";

        public string FiatCurrency = "USD";
        public decimal FiatAmount = 25m;
        public long DdCents = 2500;
        public decimal UsdRate = 1m;
        public bool DdAvailable = true;
        public bool StartedOnDd;
        public bool LockedToDd;
        public (HttpStatusCode Status, string Body)? OptionError;
        public int OptionPosts;
        private bool ddMade;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        private string DgbOption => $$"""{"currency":"DGB","address":"{{DgbAddress}}","amountSatoshis":250000000000,"amountCentsDd":null,"usdRate":null,"rateAt":null,"uri":"digibyte:{{DgbAddress}}?amount=2500"}""";

        private string DdOption => $$"""{"currency":"DD","address":"{{DdAddress}}","amountSatoshis":0,"amountCentsDd":{{DdCents}},"usdRate":{{UsdRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"rateAt":"2026-11-27T00:00:00Z","uri":"digibyte:{{DdAddress}}?amount=25.00&currency=USD"}""";

        private string Session()
        {
            var dd = StartedOnDd || LockedToDd || ddMade;
            var onDd = StartedOnDd || LockedToDd;
            var options = dd ? $"[{DgbOption},{DdOption}]" : $"[{DgbOption}]";
            return $$"""
                {"session":{"id":"ses_choice","address":"{{(onDd ? DdAddress : DgbAddress)}}","amountSatoshis":{{(onDd ? 0 : 250000000000)}},"amount":2500,
                 "fiatCurrency":"{{FiatCurrency}}","fiatAmount":{{FiatAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"dgbPriceAtCreation":0.01,
                 "label":null,"memo":null,"status":"pending","createdAt":"2026-11-27T00:00:00Z","expiresAt":"2030-01-01T00:00:00Z",
                 "receivedSatoshis":0,"confirmations":0,"paidTxid":null,
                 "uri":"digibyte:{{(onDd ? DdAddress : DgbAddress)}}?amount=1","checkoutUrl":"http://x/pay/ses_choice","returnUrl":null,
                 "currency":"{{(onDd ? "DD" : "DGB")}}","amountCentsDd":{{(onDd ? DdCents.ToString() : "null")}},
                 "buyerChoice":true,"lockedAt":{{(LockedToDd ? "\"2026-11-27T00:01:00Z\"" : "null")}},"options":{{options}}},
                 "merchantName":"Demo Store","ddAvailable":{{(DdAvailable ? "true" : "false")}},"ddReason":{{(DdAvailable ? "null" : "\"dd_disabled\"")}}}
                """;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/v1/pay/sessions/ses_choice/options/dd")
            {
                Interlocked.Increment(ref OptionPosts);
                if (OptionError is { } e) return Task.FromResult(new HttpResponseMessage(e.Status) { Content = new StringContent(e.Body, Encoding.UTF8, "application/json") });
                ddMade = true;
                return Ok(DdOption);
            }
            if (request.Method == HttpMethod.Get && path == "/v1/pay/sessions/ses_choice") return Ok(Session());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Ok(string json) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}

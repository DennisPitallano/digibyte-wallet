using System.Net;
using System.Text;
using DigiByte.Crypto.Addresses;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// specs/033 US4 (T061): the tip page offers DigiDollar from a store's donation options (contract C4).
public class DonateTests : BunitContext
{
    private const string Dgb = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa";
    private static readonly byte[] DdKey = new NBitcoin.Key().PubKey.TaprootInternalKey.ToBytes();
    private static readonly string Dd = DigiDollarAddressValidator.Encode(DdKey, "mainnet");

    private readonly OptionsApi api = new();

    private IRenderedComponent<Donate> Render(string query)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton(new WalletAppUrl("https://wallet.test/"));
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/pay/button?" + query);
        return Render<Donate>();
    }

    private static string Address(IRenderedComponent<Donate> cut) => cut.Find("#donate-address").TextContent.Trim();

    [Fact]
    public void Without_a_store_the_tip_page_is_DGB_only_as_before()
    {
        var cut = Render($"to={Dgb}&amount=5");

        Assert.Equal(Dgb, Address(cut));
        Assert.Empty(cut.FindAll("[data-tip-switch]"));
        Assert.Contains("DGB", cut.Find(".ui-checkout-unit").TextContent);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public void With_a_store_offering_DD_the_page_shows_a_switch_and_can_open_on_DD()
    {
        api.DdAddress = Dd;
        var cut = Render($"to={Dgb}&store=sto_1&currency=DD&ddAmount=5&amount=300");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-tip-switch]")));
        Assert.Equal(Dd, Address(cut));
        Assert.Equal("5.00", cut.Find("#donate-amount").TextContent.Trim());
        Assert.Equal("DD", cut.Find(".ui-checkout-unit").TextContent.Trim());
        Assert.DoesNotContain("300", cut.Find(".ui-checkout-amount").TextContent); // the DGB amount is never shown as DD
    }

    [Fact]
    public void On_DGB_the_page_shows_the_DGB_address_and_amount_with_a_link_to_DD()
    {
        api.DdAddress = Dd;
        var cut = Render($"to={Dgb}&store=sto_1&amount=300");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-tip-switch] a")));
        Assert.Equal(Dgb, Address(cut));
        Assert.Equal("300", cut.Find("#donate-amount").TextContent.Trim());
        var ddLink = cut.FindAll("[data-tip-switch] a").Single(a => a.TextContent.Trim() == "DigiDollar");
        Assert.Contains("currency=DD", ddLink.GetAttribute("href"));
    }

    [Fact]
    public void A_down_node_disables_the_DD_tab_and_DGB_still_works()
    {
        api.DdAddress = Dd;
        api.DdAvailable = false;
        var cut = Render($"to={Dgb}&store=sto_1&currency=DD");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-dd-unavailable]")));
        Assert.Equal(Dgb, Address(cut));
        Assert.Contains("DigiDollar is temporarily unavailable", cut.Markup);
    }

    [Fact]
    public void DD_turned_off_for_the_store_leaves_a_DGB_only_page()
    {
        api.DdAddress = null;
        var cut = Render($"to={Dgb}&store=sto_1&currency=DD");

        Assert.Equal(Dgb, Address(cut));
        Assert.Empty(cut.FindAll("[data-tip-switch]"));
    }

    [Theory]
    [InlineData("testnet")]  // right encoding, wrong network
    [InlineData("dgb")]      // a DGB address where DD belongs
    [InlineData("short")]    // truncated
    public void An_invalid_DD_address_is_never_shown(string kind)
    {
        api.DdAddress = kind switch
        {
            "testnet" => DigiDollarAddressValidator.Encode(DdKey, "testnet"),
            "dgb" => Dgb,
            _ => Dd[..^4],
        };
        var cut = Render($"to={Dgb}&store=sto_1&currency=DD");

        cut.WaitForAssertion(() => Assert.True(api.Calls > 0));
        cut.WaitForAssertion(() => Assert.Equal(Dgb, Address(cut)));
        Assert.Empty(cut.FindAll("[data-tip-switch]"));
        if (kind != "dgb") Assert.DoesNotContain(api.DdAddress!, cut.Markup);
    }

    private sealed class OptionsApi : HttpMessageHandler, IHttpClientFactory
    {
        public string? DdAddress;
        public bool DdAvailable = true;
        public int Calls;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (request.RequestUri!.AbsolutePath != "/v1/pay/public/stores/sto_1/donation-options")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var dd = DdAddress is null ? "null" : $"\"{DdAddress}\"";
            var json = $$"""{"storeName":"Shop","network":"mainnet","dgbAddress":null,"ddAddress":{{dd}},"ddAvailable":{{(DdAddress is not null && DdAvailable ? "true" : "false")}},"ddReason":null}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}

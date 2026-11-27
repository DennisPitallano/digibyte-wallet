using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The till's fiat handling (wallet currencies, per-currency decimals, conversion on the satoshi grid, stale and
// missing prices) and its per-tablet display settings (layout, keypad, customer display, quick amounts).
public class PosTillTests : BunitContext
{
    private readonly TillApi api = new();

    private IRenderedComponent<Pos> RenderTill(string? currency = "DGB", PosPrefs? prefs = null, string[]? recent = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<Pos.PairedDevice?>("digipayPos.load")
            .SetResult(new Pos.PairedDevice("dgp_test_secret", "sto_1", "Corner Shop", "Demo Merchant", "POS — Till 1"));
        JSInterop.Setup<string?>("digipayPos.loadCurrency").SetResult(currency);
        if (prefs is not null) JSInterop.Setup<PosPrefs>("digipayPos.loadPrefs").SetResult(prefs);
        if (recent is not null) JSInterop.Setup<string[]>("digipayPos.loadRecent").SetResult(recent);
        Services.AddLogging();
        Services.AddSingleton(new PayApiUrl("http://127.0.0.1:1"));
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        var cut = Render<Pos>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.dp-pos-charge")));
        return cut;
    }

    private static void Type(IRenderedComponent<Pos> cut, string keys)
    {
        foreach (var k in keys)
        {
            if (k == '.') cut.FindAll("button.dp-pos-key").First(b => b.GetAttribute("aria-label") == "Decimal point").Click();
            else cut.FindAll("button.dp-pos-key").First(b => b.TextContent.Trim() == k.ToString()).Click();
        }
    }

    private static string Readout(IRenderedComponent<Pos> cut) =>
        new(cut.Find(".dp-pos-amount").TextContent.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string RootAttr(IRenderedComponent<Pos> cut, string name) => cut.Find(".dp-pos").GetAttribute(name)!;

    private static void OpenSettings(IRenderedComponent<Pos> cut)
    {
        cut.Find("button[aria-label='Till menu']").Click();
        cut.FindAll(".dp-pos-menu-item").First(b => b.TextContent.Contains("Tablet settings")).Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#pos-settings-dialog")));
    }

    private static void Choose(IRenderedComponent<Pos> cut, string group, string value) =>
        cut.Find($"[aria-labelledby='{group}'] button[data-value='{value}']").Click();

    // ----- Store default currency -------------------------------------------------------

    [Fact]
    public void A_tablet_with_no_saved_currency_starts_in_the_stores_default()
    {
        api.StoreDefault = "PHP";
        api.Price = new("PHP", 0.5m, "coingecko");
        var cut = RenderTill(currency: null);

        cut.WaitForAssertion(() => Assert.Contains("PHP", cut.Find("button.dp-pos-currency").TextContent));
        // Read with the tablet's own key.
        Assert.Contains("dgp_test_secret", api.StoreReads);
        cut.WaitForAssertion(() => Assert.Contains("1 DGB = " + Fiat.Rate(0.5m, "PHP"), cut.Find(".dp-pos-rate").TextContent));
    }

    [Fact]
    public void A_currency_saved_on_the_tablet_wins_over_the_store_default()
    {
        api.StoreDefault = "PHP";
        api.Price = new("JPY", 2m, "coingecko");
        var cut = RenderTill(currency: "JPY");

        Assert.Contains("JPY", cut.Find("button.dp-pos-currency").TextContent);
        Assert.Empty(api.StoreReads);
    }

    [Fact]
    public void A_tablet_stays_in_dgb_when_the_store_has_no_default()
    {
        api.StoreDefault = null;
        var cut = RenderTill(currency: null);

        cut.WaitForAssertion(() => Assert.NotEmpty(api.StoreReads));
        Assert.Contains("DGB", cut.Find("button.dp-pos-currency").TextContent);
    }

    // ----- Amount entry -----------------------------------------------------------------

    [Fact]
    public void A_kuwaiti_dinar_till_takes_three_decimals()
    {
        var cut = RenderTill("KWD");

        Type(cut, "1.2345");

        Assert.Equal("KWD1.234", Readout(cut));
    }

    [Fact]
    public void A_yen_till_takes_no_decimals()
    {
        var cut = RenderTill("JPY");
        Type(cut, "1");

        Assert.True(cut.FindAll("button.dp-pos-key").First(b => b.GetAttribute("aria-label") == "Decimal point").HasAttribute("disabled"));
        cut.InvokeAsync(() => cut.Instance.OnKeypadKey(".")); // a hardware "." is ignored too
        Type(cut, "500");

        Assert.Equal("¥1,500", Readout(cut));
    }

    [Fact]
    public void The_readout_groups_the_amount_and_keeps_a_trailing_point_while_typing()
    {
        var cut = RenderTill("PHP");

        Type(cut, "1250.");

        Assert.Equal("₱1,250.", Readout(cut));
        Assert.Contains("is-empty", ClearAndReadClass(cut));
    }

    private static string ClearAndReadClass(IRenderedComponent<Pos> cut)
    {
        cut.Find("button.dp-pos-clear").Click();
        return cut.Find(".dp-pos-amount").ClassName;
    }

    [Fact]
    public void Holding_backspace_clears_the_amount()
    {
        var cut = RenderTill();
        Type(cut, "42");

        cut.Find("button[aria-label^='Delete last digit']").PointerDown();

        cut.WaitForAssertion(() => Assert.Equal("0DGB", Readout(cut)), TimeSpan.FromSeconds(3));
    }

    // ----- Conversion ---------------------------------------------------------------------

    [Fact]
    public void A_shilling_charge_sends_the_dgb_on_the_satoshi_grid_with_the_fiat_context()
    {
        api.Price = new("KES", 1.2345m, "coingecko+fx");
        var cut = RenderTill("KES");
        cut.WaitForAssertion(() => Assert.Contains("via USD", cut.Find(".dp-pos-rate").TextContent));
        Assert.Contains("1 DGB = " + Fiat.Rate(1.2345m, "KES"), cut.Find(".dp-pos-rate").TextContent);
        Assert.Contains("updated", cut.Find(".dp-pos-rate").TextContent);

        Type(cut, "1000");
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-frame")));
        using var body = JsonDocument.Parse(Assert.Single(api.Charges));
        var root = body.RootElement;
        var expected = Fiat.ToDgb(1000m, 1.2345m);
        Assert.Equal(Math.Round(1000m / 1.2345m, 8, MidpointRounding.AwayFromZero), expected);
        Assert.Equal(expected, root.GetProperty("amount").GetDecimal());
        Assert.Equal("KES", root.GetProperty("fiatCurrency").GetString());
        Assert.Equal(1000m, root.GetProperty("fiatAmount").GetDecimal());
        Assert.Equal(1.2345m, root.GetProperty("dgbPriceAtCreation").GetDecimal());
        // The waiting screen leads with the fiat amount, written the wallet's way.
        Assert.Equal(Fiat.Format(1000m, "KES"), cut.Find(".dp-pos-amount-due").TextContent.Trim());
        Assert.NotNull(cut.Find(".qr-frame .qr-frame-logo"));
        Assert.Contains("Scan with any DigiByte wallet", cut.Markup);
    }

    [Fact]
    public void The_charge_button_shows_the_formatted_total_and_its_dgb()
    {
        api.Price = new("PHP", 0.5m, "coingecko");
        var cut = RenderTill("PHP");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dp-pos-rate")));

        Type(cut, "1250");

        var charge = cut.Find("button.dp-pos-charge");
        Assert.Equal("Charge ₱1,250.00", charge.QuerySelector(".dp-pos-charge-main")!.TextContent.Trim());
        Assert.Equal("≈ 2500 DGB", charge.QuerySelector(".dp-pos-charge-sub")!.TextContent.Trim());
        Assert.DoesNotContain("via USD", cut.Markup);
    }

    [Fact]
    public void A_tip_is_rounded_to_the_currency()
    {
        api.Price = new("JPY", 2m, "coingecko");
        var cut = RenderTill("JPY");
        Type(cut, "155");

        cut.FindAll("button.dp-pos-tip").First(b => b.TextContent.Trim() == "10%").Click();

        // 10% of ¥155 is ¥15.5, rounded half away from zero to ¥16.
        Assert.Equal("¥171", Readout(cut));
        Assert.Contains("¥155 + ¥16 tip (10%)", cut.Find(".dp-pos-breakdown").TextContent);
    }

    [Fact]
    public void A_stale_price_says_so_but_still_charges()
    {
        api.Price = new("EUR", 0.01m, "coingecko") { Stale = true, Age = TimeSpan.FromMinutes(15) };
        var cut = RenderTill("EUR");

        cut.WaitForAssertion(() => Assert.Contains("Last known price, 15 min ago", cut.Find(".dp-pos-note").TextContent));
        Type(cut, "10");
        Assert.False(cut.Find("button.dp-pos-charge").HasAttribute("disabled"));
    }

    [Fact]
    public void No_price_disables_charge_until_a_retry_finds_one()
    {
        var cut = RenderTill("EUR");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dp-pos-noprice")));
        Type(cut, "10");

        Assert.True(cut.Find("button.dp-pos-charge").HasAttribute("disabled"));
        Assert.Contains("No DGB price for EUR right now", cut.Find(".dp-pos-noprice").TextContent);

        api.Price = new("EUR", 0.01m, "coingecko");
        cut.Find("button.dp-pos-retry").Click();

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.dp-pos-charge").HasAttribute("disabled")));
        Assert.Empty(cut.FindAll(".dp-pos-noprice"));
    }

    // ----- Currency picker ----------------------------------------------------------------

    [Fact]
    public void Picking_a_currency_persists_it_and_puts_it_first_in_recent()
    {
        var cut = RenderTill(recent: ["USD"]);
        cut.Find("button.dp-pos-currency").Click();
        Assert.Equal("Recent", cut.Find(".ui-picker-group").TextContent.Trim());

        PosRenderTests.PickCurrency(cut, "PHP");

        Assert.Equal("PHP", JSInterop.VerifyInvoke("digipayPos.saveCurrency").Arguments[0]);
        var saved = Assert.IsAssignableFrom<IEnumerable<string>>(JSInterop.VerifyInvoke("digipayPos.saveRecent").Arguments[0]);
        Assert.Equal(["PHP", "USD"], saved);

        cut.Find("button.dp-pos-currency").Click();
        var firstGroup = cut.Find(".ui-picker-list");
        Assert.Equal(["DGB", "PHP", "USD"], firstGroup.QuerySelectorAll(".ui-picker-code").Select(e => e.TextContent.Trim()));
    }

    [Fact]
    public void A_stored_currency_that_is_not_in_the_wallet_list_falls_back_to_dgb()
    {
        var cut = RenderTill("XYZ");
        Assert.Equal("Currency: DGB, change", cut.Find("button.dp-pos-currency").GetAttribute("aria-label"));
    }

    // ----- Display settings ---------------------------------------------------------------

    [Fact]
    public void Layout_and_keypad_settings_apply_to_the_root_and_are_saved()
    {
        var cut = RenderTill();
        Assert.Equal("auto", RootAttr(cut, "data-layout"));
        Assert.Equal("phone", RootAttr(cut, "data-keypad"));
        Assert.Equal("1", cut.FindAll("button.dp-pos-key")[0].TextContent.Trim());

        OpenSettings(cut);
        Choose(cut, "pos-set-layout", "split");
        Choose(cut, "pos-set-keypad", "calculator");
        Choose(cut, "pos-set-side", "left");
        Choose(cut, "pos-set-waiting", "fullscreen");

        Assert.Equal("split", RootAttr(cut, "data-layout"));
        Assert.Equal("calculator", RootAttr(cut, "data-keypad"));
        Assert.Equal("left", RootAttr(cut, "data-side"));
        Assert.Equal("fullscreen", RootAttr(cut, "data-waiting"));
        Assert.Equal("true", cut.Find("[aria-labelledby='pos-set-layout'] button[data-value='split']").GetAttribute("aria-pressed"));
        Assert.Equal("7", cut.FindAll("button.dp-pos-key")[0].TextContent.Trim());

        var saves = JSInterop.Invocations["digipayPos.savePrefs"];
        var last = Assert.IsType<PosPrefs>(saves[^1].Arguments[0]);
        Assert.Equal(("split", "calculator", "left", "fullscreen"), (last.Layout, last.Keypad, last.Side, last.Waiting));
    }

    [Fact]
    public void Saved_settings_are_applied_and_quick_amounts_fill_the_amount()
    {
        var prefs = new PosPrefs
        {
            Layout = "compact", Keypad = "calculator", Side = "LEFT", Waiting = "bogus", DoubleZero = true, Haptics = true,
            QuickAmounts = new() { ["php"] = [100m, 250m, -1m] },
        };
        api.Price = new("PHP", 0.5m, "coingecko");
        var cut = RenderTill("PHP", prefs);

        Assert.Equal("compact", RootAttr(cut, "data-layout"));
        Assert.Equal("left", RootAttr(cut, "data-side"));
        Assert.Equal("standard", RootAttr(cut, "data-waiting"));
        Assert.Equal("on", RootAttr(cut, "data-haptics"));
        Assert.Contains(cut.FindAll("button.dp-pos-key"), b => b.TextContent.Trim() == "00");
        var quick = cut.FindAll("button.dp-pos-quick-btn");
        Assert.Equal(["₱100.00", "₱250.00"], quick.Select(b => b.TextContent.Trim()));

        quick[1].Click();

        Assert.Equal("₱250", Readout(cut));
    }

    [Fact]
    public void Quick_amounts_are_edited_per_currency_in_settings()
    {
        var cut = RenderTill("PHP");
        OpenSettings(cut);

        cut.Find("#pos-set-quick").Change("50, 100 abc 100, 1000.555");

        var last = Assert.IsType<PosPrefs>(JSInterop.Invocations["digipayPos.savePrefs"][^1].Arguments[0]);
        Assert.Equal([50m, 100m, 1000.56m], last.QuickAmountsFor("PHP"));
        Assert.Equal(3, cut.FindAll("button.dp-pos-quick-btn").Count);
    }

    // ----- Shift report ------------------------------------------------------------------

    [Fact]
    public void The_shift_report_writes_fiat_the_wallet_way()
    {
        var cut = RenderTill();
        cut.Find("button[aria-label='Till menu']").Click();
        cut.FindAll(".dp-pos-menu-item").First(b => b.TextContent.Contains("Close shift")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Collected", cut.Find("#pos-shift-dialog").TextContent));
        var text = cut.Find("#pos-shift-dialog").TextContent;
        Assert.Contains(Fiat.Format(1250.5m, "PHP") + " PHP", text);
        Assert.Contains("¥1,200 JPY", text);
        Assert.Contains(Fiat.Format(3m, "USD") + " USD", text);
        Assert.Contains("123.45 DGB", text.Replace("\n", " ").Replace("  ", " "));
    }

    // ----- Header ------------------------------------------------------------------------

    [Fact]
    public void The_header_shows_the_store_initial_and_a_live_status()
    {
        var cut = RenderTill();
        Assert.Equal("C", cut.Find(".dp-pos-bar .dp-pos-avatar").TextContent.Trim());
        Assert.Contains("Live", cut.Find(".dp-pos-conn").TextContent);

        cut.InvokeAsync(() => cut.Instance.OnNetworkChanged(false));

        Assert.Contains("Offline", cut.Find(".dp-pos-conn").TextContent);
    }

    // ----- PosEntry ----------------------------------------------------------------------

    [Theory]
    [InlineData("", "0", 2, "0")]
    [InlineData("0", "7", 2, "7")]
    [InlineData("12.3", "4", 2, "12.34")]
    [InlineData("12.34", "5", 2, "12.34")]
    [InlineData("12", ".", 0, "12")]
    [InlineData("", ".", 2, "0.")]
    [InlineData("5", "00", 2, "500")]
    [InlineData("5.", "00", 2, "5.00")]
    [InlineData("5.1", "00", 2, "5.10")]
    [InlineData("123", "back", 2, "12")]
    [InlineData("123", "clear", 2, "")]
    public void Key_presses_follow_the_currency_decimals(string before, string key, int decimals, string after) =>
        Assert.Equal(after, PosEntry.Press(before, key, decimals));

    // ----- DigiDollar on the till (specs/033 US1) ------------------------------------------

    private static void OpenPicker(IRenderedComponent<Pos> cut)
    {
        cut.Find("button.dp-pos-currency").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#pos-currency-dialog")));
    }

    private static IEnumerable<AngleSharp.Dom.IElement> PickerRows(IRenderedComponent<Pos> cut, string code) =>
        cut.FindAll("#pos-currency-dialog .ui-picker-row").Where(r => r.QuerySelector(".ui-picker-code")?.TextContent.Trim() == code);

    private void AllowDd()
    {
        api.DdSurfaces = true;
        api.DigiDollarEnabled = true;
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]   // the server switch is off (no payouts yet)
    [InlineData(true, false, false)]   // the store hasn't turned DigiDollar on
    public void DD_is_offered_on_the_till_only_when_the_store_and_the_switch_allow_it(bool surfaces, bool ddOn, bool offered)
    {
        api.DdSurfaces = surfaces;
        api.DigiDollarEnabled = ddOn;
        var cut = RenderTill("DGB");

        if (offered)
            cut.WaitForAssertion(() => { OpenPicker(cut); Assert.NotEmpty(PickerRows(cut, "DD")); });
        else
        {
            OpenPicker(cut);
            Assert.Empty(PickerRows(cut, "DD"));
        }
    }

    [Fact]
    public void A_DD_till_charges_the_typed_dollars_in_cents_with_an_idempotency_key()
    {
        AllowDd();
        var cut = RenderTill("DD");
        Type(cut, "12.50");
        Assert.Equal("12.50DD", Readout(cut));
        Assert.Empty(cut.FindAll(".dp-pos-rate")); // no price lookup, no drift

        cut.WaitForAssertion(() => Assert.False(cut.Find("button.dp-pos-charge").HasAttribute("disabled")));
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-dd-due]")));
        Assert.Contains("12.50", cut.Find("[data-dd-due]").TextContent);
        using var body = JsonDocument.Parse(Assert.Single(api.Charges));
        Assert.Equal("DD", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(1250, body.RootElement.GetProperty("amountCentsDd").GetInt64());
        Assert.False(body.RootElement.TryGetProperty("amount", out _));
        Assert.False(string.IsNullOrEmpty(Assert.Single(api.IdempotencyKeys)));
    }

    [Fact]
    public void Every_charge_sends_an_idempotency_key()
    {
        var cut = RenderTill("DGB");
        Type(cut, "5");
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-frame")));
        Assert.False(string.IsNullOrEmpty(Assert.Single(api.IdempotencyKeys)));
    }

    [Fact]
    public void A_DD_amount_outside_the_limits_cannot_be_charged()
    {
        AllowDd();
        var cut = RenderTill("DD");
        Type(cut, "0.50");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-dd-range]")));
        Assert.True(cut.Find("button.dp-pos-charge").HasAttribute("disabled"));
    }

    [Fact]
    public void A_till_left_on_DD_falls_back_to_the_store_default_once_DD_is_off()
    {
        api.DdSurfaces = true;
        api.DigiDollarEnabled = false;
        api.StoreDefault = "PHP";
        api.Price = new("PHP", 0.5m, "coingecko");
        var cut = RenderTill("DD");

        cut.WaitForAssertion(() => Assert.Contains("PHP", cut.Find("button.dp-pos-currency").TextContent));
        Assert.Contains("isn't offered for this store any more", cut.Find("[data-dd-notice]").TextContent);
    }

    [Fact]
    public void When_the_DD_node_is_down_the_amount_stays_and_the_same_dollars_can_be_charged_in_DGB()
    {
        AllowDd();
        api.DdNodeDown = true;
        api.Price = new("USD", 0.01m, "coingecko");
        var cut = RenderTill("DD");
        Type(cut, "12.50");
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.dp-pos-charge").HasAttribute("disabled")));
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.Contains("DigiDollar is temporarily unavailable", cut.Find(".dp-pos-error").TextContent));
        Assert.Equal("12.50DD", Readout(cut));
        Assert.Empty(cut.FindAll(".qr-frame")); // no payment was made

        cut.Find("[data-dd-fallback]").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, api.Charges.Count));
        using var body = JsonDocument.Parse(api.Charges[1]);
        Assert.Equal("USD", body.RootElement.GetProperty("fiatCurrency").GetString());
        Assert.Equal(12.50m, body.RootElement.GetProperty("fiatAmount").GetDecimal());
        Assert.Equal(1250m, body.RootElement.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public void Switching_to_DD_keeps_a_USD_amount()
    {
        AllowDd();
        api.Price = new("USD", 0.01m, "coingecko");
        var cut = RenderTill("USD");
        Type(cut, "12.50");

        cut.WaitForAssertion(() => { OpenPicker(cut); Assert.NotEmpty(PickerRows(cut, "DD")); });
        PickerRows(cut, "DD").First().Click();

        cut.WaitForAssertion(() => Assert.Equal("12.50DD", Readout(cut)));
    }

    [Fact]
    public void Switching_to_DD_asks_before_clearing_an_amount_in_another_currency()
    {
        AllowDd();
        api.Price = new("PHP", 0.5m, "coingecko");
        var cut = RenderTill("PHP");
        Type(cut, "100");

        cut.WaitForAssertion(() => { OpenPicker(cut); Assert.NotEmpty(PickerRows(cut, "DD")); });
        PickerRows(cut, "DD").First().Click();

        cut.WaitForAssertion(() => Assert.Contains("isn't converted to DD", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Clear and switch").Click();
        cut.WaitForAssertion(() => Assert.Equal("0.00DD", Readout(cut)));
    }

    [Fact]
    public void A_fiat_sale_lets_the_customer_switch_the_code_between_DGB_and_DD()
    {
        AllowDd();
        api.Price = new("USD", 0.001m, "coingecko");
        var cut = RenderTill("USD");
        Type(cut, "25");
        cut.WaitForAssertion(() => Assert.False(cut.Find("button.dp-pos-charge").HasAttribute("disabled")));
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-pay-switch]")));
        using (var body = JsonDocument.Parse(Assert.Single(api.Charges)))
            Assert.True(body.RootElement.GetProperty("buyerChoice").GetBoolean());
        Assert.Contains("dgb1qtilladdress", cut.Find(".dp-pos-address").TextContent);

        cut.FindAll("[data-pay-switch] button").Single(b => b.TextContent.Trim() == "DigiDollar").Click();
        cut.WaitForAssertion(() => Assert.Contains("DDtillchoice001", cut.Find(".dp-pos-address").TextContent));
        Assert.Contains("25.00", cut.Find("[data-dd-due]").TextContent);

        cut.FindAll("[data-pay-switch] button").Single(b => b.TextContent.Trim() == "DGB").Click();
        cut.WaitForAssertion(() => Assert.Contains("dgb1qtilladdress", cut.Find(".dp-pos-address").TextContent));
        cut.FindAll("[data-pay-switch] button").Single(b => b.TextContent.Trim() == "DigiDollar").Click();
        cut.WaitForAssertion(() => Assert.Contains("DDtillchoice001", cut.Find(".dp-pos-address").TextContent));
        Assert.Equal(1, api.OptionPosts); // the same DD address both times
    }

    [Fact]
    public void A_sale_typed_in_DGB_offers_no_switch()
    {
        AllowDd();
        var cut = RenderTill("DGB");
        Type(cut, "5");
        cut.Find("button.dp-pos-charge").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-frame")));
        Assert.Empty(cut.FindAll("[data-pay-switch]"));
    }

    // A stand-in for the Pay API: price quotes, session creation (echoing the fiat context) and the shift report.
    private sealed class TillApi : IHttpClientFactory
    {
        public sealed record Quote(string Currency, decimal DgbPrice, string Source)
        {
            public bool Stale { get; init; }
            public TimeSpan Age { get; init; }
        }

        public Quote? Price { get; set; }
        public List<string> Charges { get; } = new();
        /// <summary>The store's defaultCurrency on GET /v1/pay/stores/sto_1.</summary>
        public string? StoreDefault { get; set; }
        public List<string> StoreReads { get; } = new();
        /// <summary>GET /v1/pay/me: the server's DD surfaces switch and whether the store has DigiDollar on.</summary>
        public bool DdSurfaces { get; set; }
        public bool DigiDollarEnabled { get; set; }
        /// <summary>POST /v1/pay/sessions with currency DD answers 503 digidollar_node_unavailable.</summary>
        public bool DdNodeDown { get; set; }
        public List<string?> IdempotencyKeys { get; } = new();
        public int OptionPosts;

        private string StoreEffectiveDefault =>
            StoreDefault == "DD" && !(DdSurfaces && DigiDollarEnabled) ? "USD" : StoreDefault ?? "";

        private string DdSession(long cents) => $$"""
            {"id":"ses_pos","address":"DDtilladdress0001","amountSatoshis":0,"amount":0,
             "fiatCurrency":null,"fiatAmount":null,"label":null,"memo":null,"status":"pending",
             "createdAt":"2026-11-26T00:00:00Z","expiresAt":"2030-01-01T00:00:00Z",
             "receivedSatoshis":0,"confirmations":0,"paidTxid":null,
             "uri":"digibyte:DDtilladdress0001?amount=1&currency=USD","checkoutUrl":"http://x/pay/ses_pos",
             "currency":"DD","amountCentsDd":{{cents}}}
            """;

        public HttpClient CreateClient(string name) => new(new Handler(this)) { BaseAddress = new Uri("http://pay-api.test") };

        private static string Num(decimal? d) => d is null ? "null" : d.Value.ToString(CultureInfo.InvariantCulture);

        private const string DgbOptionJson = """{"currency":"DGB","address":"dgb1qtilladdress000000000000000000000000","amountSatoshis":2500000000000,"amountCentsDd":null,"usdRate":null,"rateAt":null,"uri":"digibyte:dgb1qtilladdress000000000000000000000000?amount=25000"}""";
        private const string DdOptionJson = """{"currency":"DD","address":"DDtillchoice001","amountSatoshis":0,"amountCentsDd":2500,"usdRate":1,"rateAt":"2026-11-27T00:00:00Z","uri":"digibyte:DDtillchoice001?amount=25.00&currency=USD"}""";

        private string Session(string? fiatCurrency, decimal? fiatAmount, decimal amount, bool choice) =>
            Session(fiatCurrency, fiatAmount, amount).TrimEnd().TrimEnd('}') + (choice ? $$""","buyerChoice":true,"lockedAt":null,"options":[{{DgbOptionJson}}]}""" : "}");

        private string Session(string? fiatCurrency, decimal? fiatAmount, decimal amount) => $$"""
            {"id":"ses_pos","address":"dgb1qtilladdress000000000000000000000000","amountSatoshis":{{(long)(amount * 100_000_000m)}},"amount":{{Num(amount)}},
             "fiatCurrency":{{(fiatCurrency is null ? "null" : $"\"{fiatCurrency}\"")}},"fiatAmount":{{Num(fiatAmount)}},"label":null,"memo":null,"status":"pending",
             "createdAt":"2026-11-26T00:00:00Z","expiresAt":"2030-01-01T00:00:00Z",
             "receivedSatoshis":0,"confirmations":0,"paidTxid":null,
             "uri":"digibyte:dgb1qtilladdress000000000000000000000000?amount=1","checkoutUrl":"http://x/pay/ses_pos"}
            """;

        private string? lastSession;

        private sealed class Handler(TillApi api) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                string json;
                if (request.Method == HttpMethod.Get && path == "/v1/pay/public/price")
                {
                    if (api.Price is not { } q) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                    var at = DateTime.UtcNow - q.Age;
                    json = $$"""
                        {"currency":"{{q.Currency}}","dgbPrice":{{Num(q.DgbPrice)}},"fetchedAt":"{{at.ToString("o", CultureInfo.InvariantCulture)}}",
                         "source":"{{q.Source}}","decimals":2,"symbol":"x","stale":{{(q.Stale ? "true" : "false")}}}
                        """;
                }
                else if (request.Method == HttpMethod.Post && path == "/v1/pay/sessions")
                {
                    var body = await request.Content!.ReadAsStringAsync(ct);
                    lock (api.Charges) api.Charges.Add(body);
                    lock (api.IdempotencyKeys)
                        api.IdempotencyKeys.Add(request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null);
                    using var doc = JsonDocument.Parse(body);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("currency", out var cur) && cur.GetString() == "DD")
                    {
                        if (api.DdNodeDown)
                            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                            {
                                Content = new StringContent("""{"error":"digidollar_node_unavailable","detail":"down"}""", Encoding.UTF8, "application/json"),
                            };
                        json = api.lastSession = api.DdSession(r.GetProperty("amountCentsDd").GetInt64());
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                    }
                    var fc = r.GetProperty("fiatCurrency").ValueKind == JsonValueKind.String ? r.GetProperty("fiatCurrency").GetString() : null;
                    decimal? fa = r.GetProperty("fiatAmount").ValueKind == JsonValueKind.Number ? r.GetProperty("fiatAmount").GetDecimal() : null;
                    var choice = r.TryGetProperty("buyerChoice", out var bc) && bc.ValueKind == JsonValueKind.True && api.DdSurfaces && api.DigiDollarEnabled;
                    json = api.lastSession = api.Session(fc, fa, r.GetProperty("amount").GetDecimal(), choice);
                }
                else if (request.Method == HttpMethod.Get && path == "/v1/pay/stores/sto_1")
                {
                    lock (api.StoreReads) api.StoreReads.Add(request.Headers.Authorization?.Parameter ?? "");
                    json = $$"""{"id":"sto_1","name":"Corner Shop","defaultCurrency":{{(api.StoreDefault is null ? "null" : $"\"{api.StoreDefault}\"")}},"defaultCurrencyEffective":{{(api.StoreDefault is null ? "null" : $"\"{api.StoreEffectiveDefault}\"")}},"digiDollarEnabled":{{(api.DigiDollarEnabled ? "true" : "false")}}}""";
                }
                else if (request.Method == HttpMethod.Post && path == "/v1/pay/sessions/ses_pos/options/dd")
                {
                    Interlocked.Increment(ref api.OptionPosts);
                    json = DdOptionJson;
                }
                else if (request.Method == HttpMethod.Get && path == "/v1/pay/me")
                {
                    json = $$"""
                        {"id":"mer_1","displayName":"Demo Merchant","ddSurfacesEnabled":{{(api.DdSurfaces ? "true" : "false")}},
                         "stores":[{"id":"sto_1","name":"Corner Shop","defaultCurrency":{{(api.StoreDefault is null ? "null" : $"\"{api.StoreDefault}\"")}},
                                    "defaultCurrencyEffective":{{(api.StoreDefault is null ? "null" : $"\"{api.StoreEffectiveDefault}\"")}},
                                    "digiDollarEnabled":{{(api.DigiDollarEnabled ? "true" : "false")}}}]}
                        """;
                }
                else if (request.Method == HttpMethod.Get && path == "/v1/pay/sessions/ses_pos")
                    json = $$"""{"session":{{api.lastSession ?? api.Session(null, null, 12)}},"merchantName":"Demo Merchant"}""";
                else if (request.Method == HttpMethod.Get && path == "/v1/pay/pos/shift-report")
                    json = """
                        {"from":"2026-11-26T08:00:00Z","to":"2026-11-26T17:00:00Z","storeId":"sto_1",
                         "totals":{"collectedCount":3,"collectedDgb":123.45,"fiat":{"PHP":1250.5,"JPY":1200},"tips":{"PHP":50}},
                         "missed":{"expired":{"count":1,"dgb":10,"fiat":{"USD":3}},"underpaid":{"count":0,"dgb":0,"fiat":null}},
                         "outcomes":{"collected":3,"expired":1,"underpaid":0,"pending":0,"total":4}}
                        """;
                else
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }
    }
}

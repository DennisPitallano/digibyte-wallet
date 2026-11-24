using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Services;
using DigiByte.Web.Layout;
using DigiByte.Web.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The app-wide Scan button: a payment code opens Send, a Digi-ID code opens Digi-ID, and anything else opens
/// nothing. A code is never pasted into Send unless it is a real DigiByte address.
/// </summary>
public class ScanRouterTests
{
    private static readonly string Address =
        new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();
    private static readonly string LegacyAddress =
        new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString();

    [Fact]
    public void A_bare_address_opens_Send_with_it_filled_in()
    {
        var route = ScanRouter.Route(Address);

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal("/send?address=" + Address, route.Url);
    }

    [Fact]
    public void A_legacy_address_is_a_payment_too()
    {
        Assert.Equal(ScanTarget.Payment, ScanRouter.Route(LegacyAddress).Kind);
    }

    [Fact]
    public void A_payment_request_carries_its_amount()
    {
        var route = ScanRouter.Route($"digibyte:{Address}?amount=12.5");

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal($"/send?address={Address}&amount=12.5", route.Url);
    }

    [Fact]
    public void A_payment_request_without_an_amount_opens_Send_with_the_address_only()
    {
        Assert.Equal("/send?address=" + Address, ScanRouter.Route($"digibyte:{Address}").Url);
    }

    [Fact]
    public void Surrounding_white_space_is_ignored()
    {
        Assert.Equal("/send?address=" + Address, ScanRouter.Route($"  {Address}\n").Url);
    }

    [Theory]
    [InlineData("digiid://example.com/callback?x=abc123")]
    [InlineData("DIGIID://example.com/callback?x=abc123")]
    public void A_DigiId_login_code_opens_DigiId_not_Send(string code)
    {
        var route = ScanRouter.Route(code);

        Assert.Equal(ScanTarget.DigiId, route.Kind);
        Assert.Equal("/identity?uri=" + Uri.EscapeDataString(code), route.Url);
    }

    [Fact]
    public void The_installed_apps_link_form_of_DigiId_is_understood()
    {
        var route = ScanRouter.Route("web+digiid://example.com/callback?x=abc123");

        Assert.Equal(ScanTarget.DigiId, route.Kind);
        Assert.Equal("/identity?uri=" + Uri.EscapeDataString("digiid://example.com/callback?x=abc123"), route.Url);
    }

    private const string App = "https://dgbwallet.app/";

    [Fact]
    public void A_multisig_proposal_is_handed_over_not_put_in_an_address()
    {
        var route = ScanRouter.Route("  cHNidP8BAHECAAAAAbcd  ");

        Assert.Equal(ScanTarget.MultisigProposal, route.Kind);
        Assert.Null(route.Url);
        Assert.Equal("cHNidP8BAHECAAAAAbcd", route.Payload);
    }

    [Fact]
    public void A_multisig_redeem_script_opens_Import_with_the_script_handed_over()
    {
        var script = "5221" + new string('a', 66) + "21" + new string('B', 66) + "52ae";

        var route = ScanRouter.Route(script);

        Assert.Equal(ScanTarget.MultisigRedeemScript, route.Kind);
        Assert.Null(route.Url);
        Assert.Equal(script.ToLowerInvariant(), route.Payload);
    }

    [Theory]
    [InlineData("https://dgbwallet.app/multisig/room?code=ab12cd", "/multisig/room?code=AB12CD")]
    [InlineData("https://dgbwallet.app/multisig/sign-room?code=XY98ZT", "/multisig/sign-room?code=XY98ZT")]
    [InlineData("https://DGBWALLET.app/multisig/room/?code=ab12cd", "/multisig/room?code=AB12CD")]
    public void This_wallets_own_room_invite_opens_the_room(string link, string expected)
    {
        var route = ScanRouter.Route(link, App);

        Assert.Equal(ScanTarget.RoomInvite, route.Kind);
        Assert.Equal(expected, route.Url);
    }

    [Theory]
    [InlineData("https://dgbwallet.app.evil.example/multisig/room?code=ab12cd")]  // a look-alike site
    [InlineData("http://dgbwallet.app/multisig/room?code=ab12cd")]                // not the same scheme
    [InlineData("https://dgbwallet.app:8443/multisig/room?code=ab12cd")]          // not the same port
    [InlineData("https://dgbwallet.app/settings?code=ab12cd")]                    // not a room page
    [InlineData("https://dgbwallet.app/multisig/room")]                           // no code
    [InlineData("https://dgbwallet.app/multisig/room?code=ab%2F..%2Fsend")]       // not letters and digits
    [InlineData("https://dgbwallet.app/multisig/room?code=aaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Any_other_web_link_is_not_followed(string link)
    {
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route(link, App).Kind);
    }

    [Fact]
    public void A_room_invite_is_not_recognised_without_knowing_this_wallets_own_address()
    {
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route("https://dgbwallet.app/multisig/room?code=ab12cd").Kind);
    }

    [Fact]
    public void An_address_is_a_payment_even_though_a_redeem_script_is_any_run_of_hex()
    {
        // Checked in this order on purpose: the most specific kinds first, "plausible hex" last.
        Assert.Equal(ScanTarget.Payment, ScanRouter.Route(Address).Kind);
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route("abcdef").Kind);       // hex, but too short to be a script
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route(new string('a', 41)).Kind); // odd length
    }

    // ─── Scanning while a shared wallet is open: a payment starts from that shared wallet (spec 026, FR-002a) ───

    [Fact]
    public void Inside_a_shared_wallet_an_address_opens_that_wallets_Send()
    {
        var route = ScanRouter.Route(Address, App, "ms1");

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal("/multisig/ms1/send?address=" + Address, route.Url);
    }

    [Fact]
    public void Inside_a_shared_wallet_a_payment_request_keeps_its_amount()
    {
        var route = ScanRouter.Route($"digibyte:{Address}?amount=12.5", App, "ms1");

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal($"/multisig/ms1/send?address={Address}&amount=12.5", route.Url);
    }

    [Fact]
    public void The_amount_is_written_the_same_way_in_every_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            var route = ScanRouter.Route($"digibyte:{Address}?amount=12.5", App, "ms1");
            Assert.Equal($"/multisig/ms1/send?address={Address}&amount=12.5", route.Url);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_shared_wallet_id_is_escaped_in_the_address()
    {
        var route = ScanRouter.Route(Address, App, "a b/c");

        Assert.Equal("/multisig/a%20b%2Fc/send?address=" + Address, route.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_shared_wallet_a_payment_opens_the_personal_Send(string? sharedWalletId)
    {
        Assert.Equal("/send?address=" + Address, ScanRouter.Route(Address, App, sharedWalletId).Url);
    }

    [Fact]
    public void Inside_a_shared_wallet_every_other_code_goes_where_it_always_goes()
    {
        var script = "5221" + new string('a', 66) + "21" + new string('b', 66) + "52ae";
        string?[] codes =
        [
            "digiid://example.com/callback?x=abc123",
            "cHNidP8BAHECAAAAAbcd",
            script,
            "https://dgbwallet.app/multisig/sign-room?code=XY98ZT",
            "https://dgbwallet.app/multisig/room?code=ab12cd",
            "hello world",
            null,
        ];

        foreach (var code in codes)
            Assert.Equal(ScanRouter.Route(code, App), ScanRouter.Route(code, App, "ms1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/")]
    [InlineData("hello world")]
    [InlineData("bitcoin:bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq")]
    [InlineData("digibyte:not-an-address?amount=5")]
    public void Anything_else_opens_nothing(string? code)
    {
        var route = ScanRouter.Route(code);

        Assert.Equal(ScanTarget.Unknown, route.Kind);
        Assert.Null(route.Url);
    }

    // ----- Pasted text routes exactly like scanned text (specs/030-desktop-paste-scan, FR-010, SC-003) -----

    public static TheoryData<string> EveryKindOfCode()
    {
        var script = "5221" + new string('a', 66) + "21" + new string('b', 66) + "52ae";
        return new TheoryData<string>
        {
            Address,
            LegacyAddress,
            $"digibyte:{Address}?amount=12.5",
            "digiid://example.com/callback?x=abc123",
            "web+digiid://example.com/callback?x=abc123",
            "https://dgbwallet.app/multisig/room?code=ab12cd",
            "https://dgbwallet.app/multisig/sign-room?code=XY98ZT",
            "cHNidP8BAHECAAAAAbcd",
            script,
            "hello world",
        };
    }

    [Theory]
    [MemberData(nameof(EveryKindOfCode))]
    public void Pasted_text_with_quotes_and_line_breaks_routes_like_the_scanned_code(string code)
    {
        var pasted = PastedCode.Clean("  \"" + code + "\"\r\n");

        Assert.Equal(ScanRouter.Route(code, App), ScanRouter.Route(pasted, App));
    }

    [Theory]
    [MemberData(nameof(EveryKindOfCode))]
    public void Pasted_text_routes_like_the_scanned_code_inside_a_shared_wallet_too(string code)
    {
        var pasted = PastedCode.Clean("\t'" + code + "'  ");

        Assert.Equal(ScanRouter.Route(code, App, "ms1"), ScanRouter.Route(pasted, App, "ms1"));
    }

    // ----- This wallet's own payment links (FR-024) -----

    [Fact]
    public void This_wallets_payment_link_opens_Send()
    {
        var route = ScanRouter.Route($"https://dgbwallet.app/pay#digibyte:{Address}?amount=2", App);

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal($"/send?address={Address}&amount=2", route.Url);
    }

    [Fact]
    public void Inside_a_shared_wallet_a_payment_link_pays_from_that_wallet()
    {
        var route = ScanRouter.Route($"https://dgbwallet.app/pay#digibyte:{Address}?amount=2", App, "ms1");

        Assert.Equal($"/multisig/ms1/send?address={Address}&amount=2", route.Url);
    }

    [Theory]
    [InlineData("https://dgbwallet.app.evil.example/pay#")]
    [InlineData("https://evil.example/pay#")]
    [InlineData("http://dgbwallet.app/pay#")]
    public void A_look_alike_payment_link_opens_nothing(string prefix)
    {
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route(prefix + $"digibyte:{Address}?amount=2", App).Kind);
    }

    [Fact]
    public void A_payment_link_with_a_bad_address_opens_nothing()
    {
        Assert.Equal(ScanTarget.Unknown, ScanRouter.Route("https://dgbwallet.app/pay#digibyte:not-an-address?amount=2", App).Kind);
    }

    [Fact]
    public void TryOwnPaymentLink_reads_only_this_wallets_web_links()
    {
        Assert.True(ScanRouter.TryOwnPaymentLink($"https://dgbwallet.app/pay#digibyte:{Address}?amount=2", App, out var link));
        Assert.Equal(Address, link.Address);
        Assert.Equal(2m, link.AmountDgb);

        Assert.False(ScanRouter.TryOwnPaymentLink($"digibyte:{Address}?amount=2", App, out _));
        Assert.False(ScanRouter.TryOwnPaymentLink($"https://evil.example/pay#digibyte:{Address}", App, out _));
        Assert.False(ScanRouter.TryOwnPaymentLink($"https://dgbwallet.app/pay#digibyte:{Address}", null, out _));
        Assert.False(ScanRouter.TryOwnPaymentLink(null, App, out _));
    }

    [Fact]
    public void A_bare_digibyte_request_still_routes_as_before()
    {
        var route = ScanRouter.Route($"digibyte:{Address}?amount=2", App);

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal($"/send?address={Address}&amount=2", route.Url);
    }

    [Fact]
    public void A_pasted_payment_inside_a_shared_wallet_starts_a_payment_from_that_wallet()
    {
        var route = ScanRouter.Route(PastedCode.Clean(" \"" + Address + "\" "), App, "ms1");

        Assert.Equal(ScanTarget.Payment, route.Kind);
        Assert.Equal("/multisig/ms1/send?address=" + Address, route.Url);
    }
}

/// <summary>
/// The More list: every row leads to a page that exists, and the More tab knows which pages are its own.
/// </summary>
public class MoreMenuTests
{
    private static string PagesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DigiByte.Web", "Pages");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find src/DigiByte.Web/Pages above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Every_row_leads_to_a_page_that_exists()
    {
        var routes = Directory.GetFiles(PagesDirectory(), "*.razor")
            .SelectMany(File.ReadAllLines)
            .Where(line => line.StartsWith("@page \"", StringComparison.Ordinal))
            .Select(line => line.Split('"')[1].Trim('/'))
            .ToHashSet();

        foreach (var item in MoreMenu.Items)
            Assert.True(routes.Contains(item.Href), $"More row '{item.Label}' leads to '/{item.Href}', which no page declares");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Every_row_has_a_translated_label_and_description(string locale)
    {
        var file = Path.Combine(PagesDirectory(), "..", "wwwroot", "locales", locale + ".json");
        var strings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;

        foreach (var item in MoreMenu.Items)
        {
            Assert.True(strings.ContainsKey($"more.item.{item.Href}.label"), $"{locale} has no label for the More row '{item.Href}'");
            Assert.True(strings.ContainsKey($"more.item.{item.Href}.desc"), $"{locale} has no description for the More row '{item.Href}'");
        }
    }

    [Fact]
    public void The_English_fallback_in_code_matches_the_English_strings()
    {
        var file = Path.Combine(PagesDirectory(), "..", "wwwroot", "locales", "en.json");
        var strings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;

        foreach (var item in MoreMenu.Items)
        {
            Assert.Equal(item.Label, strings[$"more.item.{item.Href}.label"]);
            Assert.Equal(item.Description, strings[$"more.item.{item.Href}.desc"]);
        }
    }

    [Fact]
    public void DigiId_and_Settings_come_first_because_they_used_to_be_tabs()
    {
        Assert.Equal("identity", MoreMenu.Items[0].Href);
        Assert.Equal("settings", MoreMenu.Items[1].Href);
    }

    [Theory]
    [InlineData("settings", true)]
    [InlineData("/settings/spending-limits", true)]
    [InlineData("identity", true)]
    [InlineData("multisig/abc/pending", true)]
    [InlineData("contacts", true)]
    [InlineData("", false)]
    [InlineData("transactions", false)]
    [InlineData("payments", false)]
    [InlineData("send", false)]
    [InlineData("settingsx", false)]
    public void The_More_tab_is_active_on_its_own_pages_only(string path, bool owned)
    {
        Assert.Equal(owned, MoreMenu.Owns(path));
    }

    [Fact]
    public void No_row_repeats_a_tab()
    {
        string[] tabs = ["", "transactions", "payments"];
        Assert.DoesNotContain(MoreMenu.Items, item => tabs.Contains(item.Href));
        Assert.Equal(MoreMenu.Items.Length, MoreMenu.Items.Select(i => i.Href).Distinct().Count());
    }
}

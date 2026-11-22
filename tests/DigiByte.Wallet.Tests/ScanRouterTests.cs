using DigiByte.Crypto.Networks;
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

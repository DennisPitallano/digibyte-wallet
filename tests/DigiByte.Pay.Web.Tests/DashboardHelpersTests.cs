using DigiByte.Pay.Web.Components.Pages.Merchant;
using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// Shared dashboard helpers: plain-language API errors, the browser-zone clock, menu groups.
public class DashboardHelpersTests
{
    [Theory]
    [InlineData(400, "webhookUrl: must use https", "That webhook address can't be used. It must be a public https:// address.")]
    [InlineData(400, "cannot delete your only store", "You need at least one store, so your only store can't be deleted.")]
    [InlineData(409, "session is confirmed and cannot be voided", "This payment can't be cancelled any more. It may have just been paid. Refresh to see its latest status.")]
    [InlineData(400, "Expected a DigiByte address or a BIP84 account-level xpub.", "That isn't a DigiByte address or BIP84 xpub this store can use. Check it is for the store's network.")]
    [InlineData(400, "something new and technical", "Check what you entered and try again.")]
    [InlineData(500, null, "DigiPay couldn't do that right now. Try again in a moment.")]
    [InlineData(429, null, "Too many attempts. Wait a minute and try again.")]
    [InlineData(400, "defaultCurrency must be DGB or a supported fiat currency code (e.g. USD, EUR, PHP)",
        "Choose DGB or one of the listed currencies (such as USD, EUR or PHP), or clear the default.")]
    [InlineData(400, "defaultCurrency must be a string or null",
        "Choose DGB or one of the listed currencies (such as USD, EUR or PHP), or clear the default.")]
    public void Api_errors_map_to_plain_sentences(int status, string? error, string expected) =>
        Assert.Equal(expected, ApiError.Plain(status, error));

    // A store's DGB xpub refused by the strict check: the API's `reason` picks the sentence.
    [Theory]
    [InlineData("private_key", "That's a private key. Never share it. Paste the account xpub (public key) from your wallet.")]
    [InlineData("wrong_network", "That xpub is for the other network. Use one from a wallet on this store's network.")]
    [InlineData("malformed", "That isn't a DigiByte address or BIP84 xpub this store can use. Check it is for the store's network.")]
    [InlineData(null, "That isn't a DigiByte address or BIP84 xpub this store can use. Check it is for the store's network.")]
    public void Store_xpub_refusals_map_to_plain_sentences(string? reason, string expected)
    {
        var error = reason switch
        {
            "private_key" => "That is an extended private key (xprv/tprv). DigiPay only takes an account-level xpub; never share a private key.",
            "wrong_network" => "The xpub is for a different network than this store.",
            _ => "Expected a DigiByte address or a BIP84 account-level xpub.",
        };
        Assert.Equal(expected, ApiError.Plain(400, error, reason));
    }

    [Fact]
    public async Task Raw_bodies_are_never_returned()
    {
        using var html = new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway) { Content = new StringContent("<html>nginx stack trace</html>") };
        var text = await ApiError.ReadAsync(html);
        Assert.DoesNotContain("nginx", text);
        Assert.Equal(ApiError.Plain(502, null), text);
    }

    [Fact]
    public void The_clock_converts_to_the_browser_zone_in_one_format()
    {
        var clock = new LocalClock();
        clock.Use("Europe/Amsterdam");
        var utc = new DateTime(2026, 11, 26, 13, 5, 0, DateTimeKind.Utc);
        Assert.Equal("26 Nov 2026, 14:05", clock.Show(utc));
        // API times without a kind are UTC.
        Assert.Equal("26 Nov 2026, 14:05", clock.Show(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
    }

    [Theory]
    [InlineData("Europe/Amsterdam", 2026, 11, "26 Nov 2026, 14:05 UTC+1")]
    [InlineData("Europe/Amsterdam", 2026, 7, "26 Jul 2026, 15:05 UTC+2")] // summer time on that date
    [InlineData("America/New_York", 2026, 11, "26 Nov 2026, 08:05 UTC-5")]
    [InlineData("Asia/Kathmandu", 2026, 11, "26 Nov 2026, 18:50 UTC+5:45")]
    [InlineData("Etc/UTC", 2026, 11, "26 Nov 2026, 13:05 UTC")]
    [InlineData(null, 2026, 11, "26 Nov 2026, 13:05 UTC")]
    public void A_time_with_its_zone_names_the_offset_on_that_date(string? zone, int year, int month, string expected)
    {
        var clock = new LocalClock();
        clock.Use(zone);
        Assert.Equal(expected, clock.ShowWithZone(new DateTime(year, month, 26, 13, 5, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void An_unknown_zone_falls_back_to_utc_and_says_so(string? zone)
    {
        var clock = new LocalClock();
        clock.Use(zone);
        Assert.True(clock.IsUtc);
        Assert.Equal("26 Nov 2026, 13:05 UTC", clock.Show(new DateTime(2026, 11, 26, 13, 5, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Sections_are_grouped_in_menu_order()
    {
        var groups = DashboardSections.Grouped(DashboardSections.All);
        Assert.Equal(new[] { "Payments", "Insights", "Developers", "Account" }, groups.Select(g => g.Group));
        Assert.Equal(new[] { "overview", "new", "sessions", "links", "pos", "donation" }, groups[0].Sections.Select(s => s.Key));
        Assert.Equal(new[] { "apikeys", "webhook" }, groups[2].Sections.Select(s => s.Key));
        Assert.Equal(new[] { "stores", "settings", "audit" }, groups[3].Sections.Select(s => s.Key));
        // Renamed, same address.
        Assert.Equal("Payments", DashboardSections.ForSlug("sessions")!.Label);
        Assert.Equal("/merchant/sessions", DashboardSections.ForKey("sessions").Href);
    }

    [Fact]
    public void The_more_sheet_groups_leave_out_the_bottom_menu()
    {
        var groups = DashboardSections.Grouped(DashboardSections.More);
        Assert.Equal(new[] { "pos", "donation" }, groups[0].Sections.Select(s => s.Key));
        Assert.DoesNotContain(groups.SelectMany(g => g.Sections), s => s.BottomSlot > 0);
    }
}

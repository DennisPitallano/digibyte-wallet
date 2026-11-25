using System.Reflection;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Pages.Merchant;
using Microsoft.AspNetCore.Components;

namespace DigiByte.Pay.Web.Tests;

// contracts/dashboard-routes.md and data-model.md §3.
public class DashboardRouteTests
{
    [Theory]
    [InlineData("overview", "overview")]
    [InlineData("new", "new")]
    [InlineData("sessions", "sessions")]
    [InlineData("links", "links")]
    [InlineData("analytics", "analytics")]
    [InlineData("donation", "donation")]
    [InlineData("webhook", "webhook")]
    [InlineData("stores", "stores")]
    [InlineData("api-keys", "apikeys")]
    [InlineData("audit", "audit")]
    [InlineData("pos", "pos")]
    [InlineData("settings", "settings")]
    [InlineData("API-KEYS", "apikeys")]
    public void Every_section_address_resolves(string slug, string key) =>
        Assert.Equal(key, DashboardSections.ForSlug(slug)?.Key);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void The_bare_dashboard_address_is_overview(string? slug) =>
        Assert.Equal("overview", DashboardSections.ForSlug(slug)?.Key);

    [Theory]
    [InlineData("nope")]
    [InlineData("apikeys")] // the internal key is not an address; the slug is api-keys
    public void Unknown_addresses_resolve_to_nothing(string slug) =>
        Assert.Null(DashboardSections.ForSlug(slug));

    [Fact]
    public void The_bottom_menu_is_overview_sessions_new_payment_and_links()
    {
        Assert.Equal(new[] { "overview", "sessions", "new", "links" }, DashboardSections.BottomMenu.Select(s => s.Key));
        Assert.True(DashboardSections.BottomMenu.Single(s => s.Raised).Key == "new");
    }

    [Fact]
    public void Every_section_is_reachable_within_two_taps_on_a_phone()
    {
        // One tap for the four bottom items; More + one tap for the rest.
        var reachable = DashboardSections.BottomMenu.Concat(DashboardSections.More).Select(s => s.Key).ToHashSet();
        Assert.Equal(DashboardSections.All.Select(s => s.Key).ToHashSet(), reachable);
    }

    [Fact]
    public void Forms_use_the_narrow_column_and_lists_the_wide_area()
    {
        Assert.True(DashboardSections.ForKey("new").Narrow);
        Assert.True(DashboardSections.ForKey("settings").Narrow);
        Assert.False(DashboardSections.ForKey("sessions").Narrow);
        Assert.False(DashboardSections.ForKey("audit").Narrow);
    }

    [Fact]
    public void The_dashboard_answers_the_old_and_the_section_addresses()
    {
        var routes = typeof(Dashboard).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToArray();
        Assert.Contains("/merchant", routes);
        Assert.Contains("/merchant/{Section}", routes);
    }

    [Fact]
    public void Section_links_use_their_slugs()
    {
        Assert.Equal("/merchant", DashboardSections.ForKey("overview").Href);
        Assert.Equal("/merchant/api-keys", DashboardSections.ForKey("apikeys").Href);
    }
}

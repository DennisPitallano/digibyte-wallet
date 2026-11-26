using DigiByte.Pay.Web.Components.Shared;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The dashboard menu (rail) collapses to an icon strip and remembers that in this browser.
public class DashboardRailTests : BunitContext
{
    private string? picked;

    private IRenderedComponent<DashboardShell> RenderShell(bool storedCollapsed, string[]? folded = null, bool twoStores = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("/js/digipay-dashboard.js");
        module.Setup<bool>("railCollapsed").SetResult(storedCollapsed);
        module.SetupVoid("setRailCollapsed", _ => true).SetVoidResult();
        module.SetupVoid("bindRailTips", _ => true).SetVoidResult();
        module.Setup<string[]?>("foldedGroups").SetResult(folded);
        module.SetupVoid("setFoldedGroups", _ => true).SetVoidResult();
        Services.AddSingleton<IHttpClientFactory>(new NoHttp());
        Services.AddScoped<ToastService>();
        return Render<DashboardShell>(p => p
            .Add(x => x.ActiveKey, "overview")
            .Add(x => x.AccountName, "Dev merchant")
            .Add(x => x.Stores, twoStores
                ? new[] { new DashboardShell.StoreOption("sto_1", "Main store", "mainnet"), new DashboardShell.StoreOption("sto_2", "Pop-up", "testnet") }
                : new[] { new DashboardShell.StoreOption("sto_1", "Main store", "mainnet") })
            .Add(x => x.ActiveStoreId, "sto_1")
            .Add(x => x.OnStoreChanged, (string id) => picked = id));
    }

    [Fact]
    public void The_menu_starts_expanded_and_collapses_to_icons_with_labels_kept()
    {
        var cut = RenderShell(storedCollapsed: false);
        var toggle = cut.Find(".dp-rail-toggle");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Collapse menu", toggle.GetAttribute("aria-label"));

        toggle.Click();

        cut.WaitForAssertion(() => Assert.Contains("is-rail-collapsed", cut.Find(".dp-shell").ClassName));
        Assert.Equal("false", cut.Find(".dp-rail-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("Expand menu", cut.Find(".dp-rail-toggle").GetAttribute("aria-label"));
        // Every rail link keeps its name (screen readers) and a tooltip label.
        var overview = cut.FindAll(".dp-rail a.dp-rail-item").First(a => a.GetAttribute("data-label") == "Overview");
        Assert.Contains("Overview", overview.TextContent);
        // The store picker gives way to a store button that expands the menu again.
        Assert.Empty(cut.FindAll("#store-switch-rail"));
        cut.Find(".dp-rail-store").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("is-rail-collapsed", cut.Find(".dp-shell").ClassName));
    }

    [Fact]
    public void Collapsing_is_remembered()
    {
        var cut = RenderShell(storedCollapsed: false);
        cut.Find(".dp-rail-toggle").Click();
        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "setRailCollapsed" && (bool)i.Arguments[0]! == true));
    }

    [Fact]
    public void A_remembered_collapsed_menu_starts_collapsed()
    {
        var cut = RenderShell(storedCollapsed: true);
        cut.WaitForAssertion(() => Assert.Contains("is-rail-collapsed", cut.Find(".dp-shell").ClassName));
    }

    [Fact]
    public void A_group_folds_and_keeps_the_current_page_visible()
    {
        var cut = RenderShell(storedCollapsed: false);
        var payments = cut.Find("#nav-rail-payments");
        Assert.Equal("true", payments.GetAttribute("aria-expanded"));

        payments.Click();

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("#nav-rail-payments").GetAttribute("aria-expanded")));
        var group = cut.Find("#nav-rail-payments").ParentElement!;
        Assert.Contains("is-folded", group.ClassName);
        // Overview is the current page, so it stays marked to remain visible.
        Assert.Single(group.QuerySelectorAll("li.is-current"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "setFoldedGroups"
            && ((string[])i.Arguments[0]!).Contains("Payments"));
    }

    [Fact]
    public void Remembered_folded_groups_start_folded()
    {
        var cut = RenderShell(storedCollapsed: false, folded: ["Developers"]);
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("#nav-rail-developers").GetAttribute("aria-expanded")));
        Assert.Equal("true", cut.Find("#nav-rail-payments").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void The_store_picker_shows_the_store_and_switches_from_its_menu()
    {
        var cut = RenderShell(storedCollapsed: false, twoStores: true);
        var button = cut.Find("#store-switch-rail");
        Assert.Contains("Main store", button.TextContent);
        Assert.Contains("Mainnet · 2 stores", button.TextContent);
        Assert.Equal("false", button.GetAttribute("aria-expanded"));

        button.Click();

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#store-switch-rail").GetAttribute("aria-expanded")));
        var options = cut.FindAll("#store-menu-rail [role=menuitemradio]");
        Assert.Equal(2, options.Count);
        Assert.Equal("true", options[0].GetAttribute("aria-checked"));
        Assert.Contains("Testnet", options[1].TextContent);
        Assert.NotNull(cut.Find("#store-menu-rail a[href='/merchant/stores']"));

        options[1].Click();
        cut.WaitForAssertion(() => Assert.Equal("sto_2", picked));
        Assert.Empty(cut.FindAll("#store-menu-rail"));
    }

    [Fact]
    public void Escape_closes_the_store_menu()
    {
        var cut = RenderShell(storedCollapsed: false, twoStores: true);
        cut.Find("#store-switch-rail").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#store-menu-rail")));
        cut.Find(".dash-store").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#store-menu-rail")));
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri("http://127.0.0.1:1") };
    }
}

using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

public class CopyButtonTests : BunitContext
{
    private readonly ToastService toasts = new();

    public CopyButtonTests()
    {
        Services.AddSingleton(toasts);
        JSInterop.Mode = JSRuntimeMode.Strict;
    }

    [Fact]
    public void A_successful_copy_shows_a_success_notice()
    {
        JSInterop.Setup<bool>("digipayClipboard.copy", "dgb1qexample").SetResult(true);
        var cut = Render<CopyButton>(p => p
            .Add(x => x.Value, "dgb1qexample")
            .Add(x => x.Label, "Copy address")
            .Add(x => x.SuccessText, "Address copied"));

        cut.Find("button").Click();

        var notice = Assert.Single(toasts.Items);
        Assert.Equal(ToastTone.Success, notice.Tone);
        Assert.Equal("Address copied", notice.Text);
    }

    [Fact]
    public void A_blocked_copy_selects_the_value_and_explains_manual_copy()
    {
        JSInterop.Setup<bool>("digipayClipboard.copy", "12.5").SetResult(false);
        var select = JSInterop.Setup<bool>("digipayClipboard.select", "amount-value");
        select.SetResult(true);
        var cut = Render<CopyButton>(p => p
            .Add(x => x.Value, "12.5")
            .Add(x => x.Label, "Copy amount")
            .Add(x => x.TargetId, "amount-value"));

        cut.Find("button").Click();

        Assert.Single(select.Invocations);
        var notice = Assert.Single(toasts.Items);
        Assert.Equal(ToastTone.Info, notice.Tone);
        Assert.Contains("selected", notice.Text);
    }

    [Fact]
    public void The_button_has_an_accessible_name_in_both_variants()
    {
        var ghost = Render<CopyButton>(p => p.Add(x => x.Value, "x").Add(x => x.Label, "Copy transaction ID"));
        Assert.Equal("Copy transaction ID", ghost.Find("button").GetAttribute("aria-label"));

        var icon = Render<CopyButton>(p => p.Add(x => x.Value, "x").Add(x => x.Label, "Copy address").Add(x => x.Variant, "icon"));
        Assert.Equal("Copy address", icon.Find("button").GetAttribute("aria-label"));
        Assert.Contains("icon-btn", icon.Find("button").ClassList);
    }

    [Fact]
    public void Nothing_to_copy_disables_the_button()
    {
        var cut = Render<CopyButton>(p => p.Add(x => x.Value, "").Add(x => x.Label, "Copy"));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
    }
}

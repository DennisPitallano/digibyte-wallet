using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.AspNetCore.Components;

namespace DigiByte.Pay.Web.Tests;

public class ConfirmDialogTests : BunitContext
{
    public ConfirmDialogTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private IRenderedComponent<ConfirmDialog> RenderOpen(Action onConfirm, Action onCancel, bool danger = true) =>
        Render<ConfirmDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "Revoke this API key?")
            .Add(x => x.ConfirmLabel, "Revoke key")
            .Add(x => x.Danger, danger)
            .Add(x => x.OnConfirm, EventCallback.Factory.Create(this, onConfirm))
            .Add(x => x.OnCancel, EventCallback.Factory.Create(this, onCancel))
            .AddChildContent("<p>Apps using this key stop working. This can't be undone.</p>"));

    [Fact]
    public void It_is_announced_as_a_modal_dialog_named_by_its_title()
    {
        var cut = RenderOpen(() => { }, () => { });
        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        var titleId = dialog.GetAttribute("aria-labelledby");
        Assert.Equal("Revoke this API key?", cut.Find($"#{titleId}").TextContent);
        Assert.NotNull(dialog.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Cancel_and_escape_raise_OnCancel()
    {
        var cancels = 0;
        var cut = RenderOpen(() => { }, () => cancels++);

        cut.Find("button.btn-secondary").Click();
        cut.InvokeAsync(() => cut.Instance.OnEscape());

        Assert.Equal(2, cancels);
    }

    [Fact]
    public void Confirm_raises_OnConfirm_and_uses_the_danger_style()
    {
        var confirms = 0;
        var cut = RenderOpen(() => confirms++, () => { });

        var confirm = cut.Find("button.btn-danger");
        Assert.Equal("Revoke key", confirm.TextContent);
        confirm.Click();

        Assert.Equal(1, confirms);
    }

    [Fact]
    public void Focus_handling_is_handed_to_the_dialog_script_when_opened()
    {
        var cut = RenderOpen(() => { }, () => { });
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "digipayDialog.open");
    }

    [Fact]
    public void A_closed_dialog_renders_nothing()
    {
        var cut = Render<ConfirmDialog>(p => p.Add(x => x.Open, false).Add(x => x.Title, "x"));
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }
}

using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

public class ToastServiceTests
{
    [Fact]
    public void The_same_text_within_two_seconds_is_merged_into_one_notice()
    {
        using var toasts = new ToastService();
        toasts.Success("Copied");
        toasts.Success("Copied");
        Assert.Single(toasts.Items);
    }

    [Fact]
    public void Different_text_or_tone_stacks()
    {
        using var toasts = new ToastService();
        toasts.Success("Copied");
        toasts.Info("Copied");
        toasts.Success("Saved");
        Assert.Equal(3, toasts.Items.Count);
    }

    [Fact]
    public async Task Errors_stay_until_dismissed_or_acted_on()
    {
        using var toasts = new ToastService();
        var retried = false;
        var id = toasts.Show(ToastTone.Error, "Couldn't save settings.", "Retry", () => { retried = true; return Task.CompletedTask; });

        await Task.Delay(ToastService.AutoDismissAfter + TimeSpan.FromMilliseconds(300));
        Assert.Single(toasts.Items);

        await toasts.RunActionAsync(id);
        Assert.True(retried);
        Assert.Empty(toasts.Items);
    }

    [Fact]
    public async Task Success_notices_disappear_by_themselves()
    {
        using var toasts = new ToastService();
        toasts.Success("Saved");
        await Task.Delay(ToastService.AutoDismissAfter + TimeSpan.FromMilliseconds(500));
        Assert.Empty(toasts.Items);
    }

    [Fact]
    public void A_notice_needs_text() =>
        Assert.Throws<ArgumentException>(() => new ToastService().Show(ToastTone.Info, " "));
}

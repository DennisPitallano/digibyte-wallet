using System.Text.RegularExpressions;
using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// data-model.md §5: every state's badge text, tone, icon and announcement.
public class StatusBadgeTests : BunitContext
{
    public static TheoryData<string, string, BadgeTone, string, string?> Table => new()
    {
        { "pending",      "Waiting for payment",        BadgeTone.Neutral, "clock",        null },
        { "seen",         "Payment detected",           BadgeTone.Info,    "radar",        "Payment detected. Waiting for the network." },
        { "paid",         "Payment received",           BadgeTone.Success, "check",        "Payment received." },
        { "confirmed",    "Confirmed",                  BadgeTone.Success, "check-circle", "Payment confirmed." },
        { "underpaid",    "Underpaid",                  BadgeTone.Danger,  "alert",        "Too little was received." },
        { "expired",      "Price expired",              BadgeTone.Neutral, "timer-off",    "This price has expired." },
        { "refunded",     "Refunded",                   BadgeTone.Refund,  "undo",         "This payment was refunded." },
        { "voided",       "Cancelled by the merchant",  BadgeTone.Neutral, "ban",          "The merchant cancelled this payment." },
        { "reconnecting", "Reconnecting…",              BadgeTone.Neutral, "refresh",      "Reconnecting. Your payment is still being watched." },
        { "load-failed",  "Not available",              BadgeTone.Danger,  "alert",        "This payment page could not be loaded." },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_state_matches_the_table(string key, string badge, BadgeTone tone, string icon, string? announcement)
    {
        var s = CheckoutStates.For(key);
        Assert.Equal(badge, s.Badge);
        Assert.Equal(tone, s.Tone);
        Assert.Equal(icon, s.Icon);
        Assert.Equal(announcement, s.Announcement);
        Assert.False(string.IsNullOrWhiteSpace(s.Heading));
        Assert.False(string.IsNullOrWhiteSpace(s.Explanation));
    }

    [Fact]
    public void Every_api_status_has_a_row()
    {
        foreach (var status in CheckoutStates.ApiStatuses)
            Assert.True(CheckoutStates.IsKnown(status), status);
    }

    [Theory]
    [InlineData("confirmed", true)]
    [InlineData("underpaid", true)]
    [InlineData("expired", true)]
    [InlineData("refunded", true)]
    [InlineData("voided", true)]
    [InlineData("pending", false)]
    [InlineData("seen", false)]
    [InlineData("paid", false)]
    [InlineData("reconnecting", false)]
    public void Final_states_match_the_server(string status, bool final) =>
        Assert.Equal(final, CheckoutStates.IsFinal(status));

    [Fact]
    public void An_unknown_status_shows_as_waiting_with_no_announcement()
    {
        var s = CheckoutStates.For("some-new-status");
        Assert.Equal("Waiting for payment", s.Badge);
        Assert.Null(s.Announcement);
        Assert.False(CheckoutStates.IsFinal("some-new-status"));
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void The_badge_renders_an_icon_and_text_and_no_glyphs(string key, string badge, BadgeTone tone, string icon, string? announcement)
    {
        _ = icon; _ = announcement;
        var cut = Render<StatusBadge>(p => p.Add(x => x.Status, key));
        var root = cut.Find(".ui-badge");
        Assert.Contains($"ui-badge-{StatusBadge.ToneClass(tone)}", root.ClassList);
        Assert.NotNull(root.QuerySelector("svg"));
        Assert.Equal(badge, root.TextContent.Trim());
        Assert.DoesNotMatch(new Regex("[✓✔⏱⊘↩⚠•]"), cut.Markup);
    }
}

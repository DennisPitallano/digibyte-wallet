using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How a paid request is announced, in exactly one way (spec 036, FR-010 to FR-014b, contracts/request-watcher.md).
/// Locked comes first and never counts as the announcement, so the summary still follows an unlock.
/// </summary>
public class RequestAnnouncementRuleTests
{
    private static readonly DateTime T0 = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    private static PaymentRequest Paid(PaymentRequestStatus status = PaymentRequestStatus.Paid) => new()
    {
        Id = "r",
        Address = "dgb1qr",
        Tracked = true,
        AmountDgb = 1m,
        Status = status,
    };

    private static RequestViewState View(bool onScreen = false, bool visible = true, bool locked = false) =>
        new(RequestOnScreen: onScreen, Visible: visible, Locked: locked);

    private static RequestAnnouncement Decide(PaymentRequest r, RequestViewState v, bool first = false) =>
        RequestAnnouncementRule.Decide(r, v, first);

    [Theory]
    [InlineData(PaymentRequestStatus.Waiting)]
    [InlineData(PaymentRequestStatus.PaymentArriving)]
    [InlineData(PaymentRequestStatus.PartlyPaid)]
    [InlineData(PaymentRequestStatus.Expired)]
    [InlineData(PaymentRequestStatus.PartlyPaidExpired)]
    [InlineData(PaymentRequestStatus.Cancelled)]
    [InlineData(PaymentRequestStatus.NotTracked)]
    public void NotPaid_IsNeverAnnounced(PaymentRequestStatus status)
    {
        Assert.Equal(RequestAnnouncement.None, Decide(Paid(status), View(onScreen: true)));
        Assert.Equal(RequestAnnouncement.None, Decide(Paid(status), View(locked: true, visible: false)));
        Assert.Equal(RequestAnnouncement.None, Decide(Paid(status), View(), first: true));
    }

    [Fact]
    public void AlreadyAnnounced_IsNone()
    {
        var r = Paid();
        r.PaidAnnouncedAt = T0;

        Assert.Equal(RequestAnnouncement.None, Decide(r, View(onScreen: true)));
        Assert.Equal(RequestAnnouncement.None, Decide(r, View(locked: true)));
        Assert.Equal(RequestAnnouncement.None, Decide(r, View(), first: true));
    }

    [Theory]
    [InlineData(PaymentRequestStatus.Paid)]
    [InlineData(PaymentRequestStatus.Overpaid)]
    public void OnScreenAndUnlocked_IsTheOverlay(PaymentRequestStatus status)
    {
        Assert.Equal(RequestAnnouncement.Overlay, Decide(Paid(status), View(onScreen: true)));
    }

    [Fact]
    public void AnotherScreen_IsAToast()
    {
        Assert.Equal(RequestAnnouncement.Toast, Decide(Paid(), View()));
    }

    [Fact]
    public void Hidden_IsASystemNotification_EvenIfTheRequestIsTheOpenPage()
    {
        Assert.Equal(RequestAnnouncement.SystemNotification, Decide(Paid(), View(visible: false)));
        Assert.Equal(RequestAnnouncement.SystemNotification, Decide(Paid(), View(onScreen: true, visible: false)));
    }

    [Fact]
    public void LockedAndVisible_IsTheLockNotice()
    {
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(Paid(), View(locked: true)));
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(Paid(), View(onScreen: true, locked: true)));
    }

    [Fact]
    public void LockedAndHidden_SendsTheNotificationOnce()
    {
        var r = Paid();
        Assert.Equal(RequestAnnouncement.LockedNotification, Decide(r, View(visible: false, locked: true)));

        r.LockNotifiedAt = T0;
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(r, View(visible: false, locked: true)));
    }

    [Fact]
    public void FirstCheckSinceStart_WhileUnlocked_IsTheSummary_WhateverIsOnScreen()
    {
        Assert.Equal(RequestAnnouncement.AwaySummary, Decide(Paid(), View(onScreen: true), first: true));
        Assert.Equal(RequestAnnouncement.AwaySummary, Decide(Paid(), View(), first: true));
        Assert.Equal(RequestAnnouncement.AwaySummary, Decide(Paid(), View(visible: false), first: true));
    }

    [Fact]
    public void Locked_BeatsTheSummary()
    {
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(Paid(), View(locked: true), first: true));
        Assert.Equal(RequestAnnouncement.LockedNotification, Decide(Paid(), View(visible: false, locked: true), first: true));
    }

    [Fact]
    public void OnlyTheUnlockedWays_CountAsTheAnnouncement()
    {
        Assert.True(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.Overlay));
        Assert.True(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.Toast));
        Assert.True(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.SystemNotification));
        Assert.True(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.AwaySummary));
        Assert.False(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.LockNotice));
        Assert.False(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.LockedNotification));
        Assert.False(RequestAnnouncementRule.MarksAnnounced(RequestAnnouncement.None));
    }

    [Fact]
    public void Sequence_LockedAndVisible_ThenUnlock_GivesTheSummary()
    {
        var r = Paid();

        var whileLocked = Decide(r, View(locked: true), first: true);
        Assert.Equal(RequestAnnouncement.LockNotice, whileLocked);
        Assert.False(RequestAnnouncementRule.MarksAnnounced(whileLocked));

        var afterUnlock = Decide(r, View(), first: true);
        Assert.Equal(RequestAnnouncement.AwaySummary, afterUnlock);
        r.PaidAnnouncedAt = T0;
        Assert.Equal(RequestAnnouncement.None, Decide(r, View()));
    }

    [Fact]
    public void Sequence_LockedAndHidden_NotifiesOnce_ThenUnlock_GivesTheSummary()
    {
        var r = Paid();

        Assert.Equal(RequestAnnouncement.LockedNotification, Decide(r, View(visible: false, locked: true)));
        r.LockNotifiedAt = T0;
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(r, View(visible: false, locked: true)));
        Assert.Equal(RequestAnnouncement.LockNotice, Decide(r, View(locked: true)));
        Assert.Equal(RequestAnnouncement.AwaySummary, Decide(r, View(), first: true));
    }

    [Fact]
    public void EveryCombination_ReturnsExactlyOneDefinedValue()
    {
        var statuses = Enum.GetValues<PaymentRequestStatus>();
        foreach (var status in statuses)
        foreach (var announced in new[] { false, true })
        foreach (var lockNotified in new[] { false, true })
        foreach (var onScreen in new[] { false, true })
        foreach (var visible in new[] { false, true })
        foreach (var locked in new[] { false, true })
        foreach (var first in new[] { false, true })
        {
            var r = Paid(status);
            r.PaidAnnouncedAt = announced ? T0 : null;
            r.LockNotifiedAt = lockNotified ? T0 : null;

            var result = Decide(r, View(onScreen, visible, locked), first);

            Assert.True(Enum.IsDefined(result));
        }
    }
}

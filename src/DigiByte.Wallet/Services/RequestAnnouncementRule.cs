using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Where the person is when a request is found paid (spec 036).</summary>
/// <param name="RequestOnScreen">The request's own detail view is the open page.</param>
/// <param name="Visible">The wallet's tab or app is in front.</param>
/// <param name="Locked">The wallet shows the lock screen.</param>
public sealed record RequestViewState(bool RequestOnScreen, bool Visible, bool Locked);

/// <summary>How a paid request is announced (contracts/request-watcher.md).</summary>
public enum RequestAnnouncement
{
    /// <summary>Nothing to announce: not paid, or already announced.</summary>
    None,
    /// <summary>The full-screen card with a vibration, on the request's own page (FR-011).</summary>
    Overlay,
    /// <summary>A tappable message on another screen of the wallet (FR-012).</summary>
    Toast,
    /// <summary>A system notification while the wallet is hidden (FR-013).</summary>
    SystemNotification,
    /// <summary>A system notification while the wallet is locked and hidden, sent once (FR-014b).</summary>
    LockedNotification,
    /// <summary>The "Request paid" notice on the lock screen (FR-014b).</summary>
    LockNotice,
    /// <summary>Part of one "N requests paid since you were last here" message (FR-014a).</summary>
    AwaySummary,
}

/// <summary>
/// Decides, in one place, how a paid request is announced (spec 036). The watcher and announcer only act on the answer.
/// Locked comes first and never counts as the announcement, so the summary still follows an unlock; then the summary
/// for the first check after a start, unlock or wallet switch; then the overlay, a toast or a system notification. Pure.
/// </summary>
public static class RequestAnnouncementRule
{
    public static RequestAnnouncement Decide(PaymentRequest request, RequestViewState view, bool firstCheckSinceStart)
    {
        if (request.Status is not (PaymentRequestStatus.Paid or PaymentRequestStatus.Overpaid)) return RequestAnnouncement.None;
        if (request.PaidAnnouncedAt is not null) return RequestAnnouncement.None;

        if (view.Locked)
            return !view.Visible && request.LockNotifiedAt is null
                ? RequestAnnouncement.LockedNotification
                : RequestAnnouncement.LockNotice;

        if (firstCheckSinceStart) return RequestAnnouncement.AwaySummary;
        if (!view.Visible) return RequestAnnouncement.SystemNotification;
        return view.RequestOnScreen ? RequestAnnouncement.Overlay : RequestAnnouncement.Toast;
    }

    /// <summary>
    /// Whether this way sets <see cref="PaymentRequest.PaidAnnouncedAt"/>. The two locked ways don't, so the person
    /// still gets the summary after unlocking (FR-014a).
    /// </summary>
    public static bool MarksAnnounced(RequestAnnouncement announcement) => announcement is
        RequestAnnouncement.Overlay or RequestAnnouncement.Toast or RequestAnnouncement.SystemNotification or RequestAnnouncement.AwaySummary;
}

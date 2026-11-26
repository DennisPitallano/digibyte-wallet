namespace DigiByte.Pay.Web.Components.Shared.Ui;

public enum BadgeTone { Neutral, Info, Success, Danger, Refund }

/// <summary>How one checkout state looks and sounds to a shopper.</summary>
/// <param name="Badge">Badge text.</param>
/// <param name="Tone">Badge colour.</param>
/// <param name="Icon">Icon name from the shared icon set (never an emoji or glyph).</param>
/// <param name="Announcement">What a screen reader hears when the page changes to this state; null for none.</param>
/// <param name="Heading">Plain-language heading for a final state.</param>
/// <param name="Explanation">Plain-language explanation and next step.</param>
/// <param name="IsFinal">True when the live connection can close: the server will not change it again.</param>
public sealed record CheckoutState(
    string Key, string Badge, BadgeTone Tone, string Icon, string? Announcement,
    string Heading, string Explanation, bool IsFinal);

/// <summary>
/// The checkout's states (specs/032-digipay-ui-refresh/data-model.md §5, FR-009): the Pay API's
/// session statuses plus two the page adds itself, "reconnecting" and "load-failed".
/// </summary>
public static class CheckoutStates
{
    public const string Reconnecting = "reconnecting";
    public const string LoadFailed = "load-failed";

    /// <summary>Blocks a payment needs before the Pay API marks it confirmed (InvoiceMonitor.ConfirmedAt).</summary>
    public const int ConfirmationsRequired = 6;

    /// <summary>True once the shopper has paid and the page only tracks confirmations ("seen", "paid").</summary>
    public static bool IsConfirming(string? status) => status is "seen" or "paid";

    private static readonly CheckoutState Pending = new(
        "pending", "Waiting for payment", BadgeTone.Neutral, "clock", null,
        "Waiting for payment",
        "Scan the code with any DigiByte wallet, press Open in wallet, or copy the address and amount.",
        false);

    private static readonly Dictionary<string, CheckoutState> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pending"] = Pending,
        ["seen"] = new("seen", "Payment detected", BadgeTone.Info, "radar",
            "Payment detected. Waiting for the network.",
            "Payment detected",
            "Your payment is on its way. Keep this page open while the network picks it up.",
            false),
        ["paid"] = new("paid", "Payment received", BadgeTone.Success, "check",
            "Payment received.",
            "Payment received",
            "The merchant has your payment. It's final after 6 network confirmations; you can close this page.",
            false),
        ["confirmed"] = new("confirmed", "Confirmed", BadgeTone.Success, "check-circle",
            "Payment confirmed.",
            "Payment confirmed",
            "The merchant has been notified. You can close this page.",
            true),
        ["underpaid"] = new("underpaid", "Underpaid", BadgeTone.Danger, "alert",
            "Too little was received.",
            "Too little was received",
            "Less than the full amount arrived. Contact the merchant to sort out the difference; don't send the rest to this address.",
            true),
        ["expired"] = new("expired", "Price expired", BadgeTone.Neutral, "timer-off",
            "This price has expired.",
            "This price has expired",
            "This payment window has closed, so don't pay to this address. Go back to the shop to get a new price.",
            true),
        ["refunded"] = new("refunded", "Refunded", BadgeTone.Refund, "undo",
            "This payment was refunded.",
            "Payment refunded",
            "The merchant sent the amount back. Check your wallet.",
            true),
        ["voided"] = new("voided", "Cancelled by the merchant", BadgeTone.Neutral, "ban",
            "The merchant cancelled this payment.",
            "Cancelled by the merchant",
            "The merchant cancelled this payment before it was paid. Nothing was charged.",
            true),
        [Reconnecting] = new(Reconnecting, "Reconnecting…", BadgeTone.Neutral, "refresh",
            "Reconnecting. Your payment is still being watched.",
            "Reconnecting",
            "The live connection dropped. Your payment is still being watched, and this page will update when it reconnects.",
            false),
        [LoadFailed] = new(LoadFailed, "Not available", BadgeTone.Danger, "alert",
            "This payment page could not be loaded.",
            "This payment page could not be loaded",
            "Check your connection and try again. If it keeps happening, go back to the shop and start the payment again.",
            false),
    };

    /// <summary>Every key the page knows, API statuses first.</summary>
    public static IReadOnlyCollection<string> Keys => Map.Keys;

    /// <summary>The Pay API statuses (not the two client-only states).</summary>
    public static readonly IReadOnlyList<string> ApiStatuses =
        ["pending", "seen", "paid", "confirmed", "underpaid", "expired", "refunded", "voided"];

    /// <summary>
    /// The display state for a status. An unknown status shows as "Waiting for payment" with no
    /// announcement, so a new server status never breaks the page.
    /// </summary>
    public static CheckoutState For(string? status) =>
        status is not null && Map.TryGetValue(status, out var s) ? s : Pending with { Announcement = null };

    public static bool IsKnown(string? status) => status is not null && Map.ContainsKey(status);

    /// <summary>True when the server will not change this status again.</summary>
    public static bool IsFinal(string? status) => IsKnown(status) && Map[status!].IsFinal;
}

namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// Announces short messages to screen readers through the visually hidden live regions that
/// <see cref="LiveRegion"/> renders (for example a checkout status change: "Payment received.").
/// Scoped: one per circuit.
/// </summary>
public sealed class LiveAnnouncer
{
    public string Polite { get; private set; } = "";
    public string Assertive { get; private set; } = "";

    public event Action? Changed;

    public void Announce(string text, bool assertive = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Screen readers only speak a live region when its content changes. Repeating the same
        // sentence (two "Reconnecting." in a row) would be silent, so alternate a trailing
        // non-breaking space to force a change without altering what is read.
        if (assertive) Assertive = Toggle(Assertive, text);
        else Polite = Toggle(Polite, text);
        Changed?.Invoke();
    }

    private static string Toggle(string previous, string text) =>
        previous == text ? text + " " : text;
}

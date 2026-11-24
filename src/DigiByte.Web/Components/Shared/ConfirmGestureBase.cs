using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using DigiByte.Web.Services;

namespace DigiByte.Web.Components.Shared;

/// <summary>
/// Behaviour behind <see cref="SlideToConfirm"/>: a control for irreversible, value-moving actions
/// only (see docs/UI_STYLE.md). It raises <see cref="OnConfirmed"/> exactly once and then stays
/// locked in a busy state until <see cref="Reset"/> is called.
///
/// Ways to confirm without a pointer gesture:
/// - keyboard: hold Enter or Space for <see cref="HoldMs"/>; releasing early cancels;
/// - assistive technology (a screen reader's "activate", which arrives as a click with no key held and
///   cannot be held down): activate once to arm, activate again within five seconds to confirm.
/// </summary>
public abstract class ConfirmGestureBase : ComponentBase, IDisposable
{
    /// <summary>Screen-reader text is in the wallet's language (spec 028).</summary>
    [Inject] protected LocalizationService L { get; set; } = default!;

    protected enum Phase { Idle, Dragging, Holding, Done }

    /// <summary>Visible text while idle, for example "Slide to send".</summary>
    [Parameter] public string Label { get; set; } = "Confirm";

    /// <summary>Visible text once confirmed, while the page does the work.</summary>
    [Parameter] public string BusyLabel { get; set; } = "Sending…";

    /// <summary>What is being confirmed, for screen readers, for example "Send 12.5 DGB to Alice".</summary>
    [Parameter] public string? ActionDescription { get; set; }

    /// <summary>How long a press or key must be held, in milliseconds.</summary>
    [Parameter] public int HoldMs { get; set; } = 1000;

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Raised once when the user completes the gesture.</summary>
    [Parameter] public EventCallback OnConfirmed { get; set; }

    protected Phase State { get; set; } = Phase.Idle;
    protected string Announcement { get; private set; } = "";
    protected bool Armed { get; private set; }

    public bool IsConfirmed => State == Phase.Done;

    private CancellationTokenSource? _holdCts;
    private CancellationTokenSource? _armCts;
    private bool _keyHeld;
    private DateTime _lastKeyAt = DateTime.MinValue;

    /// <summary>The words that describe the gesture, used in the accessible name ("Slide", "Press and hold").</summary>
    protected abstract string GestureHint { get; }

    protected string AccessibleName => L.T("send.slide.accessible", ActionDescription ?? Label);

    /// <summary>Returns the control to idle so it can be used again (for example after a failed send).</summary>
    public void Reset()
    {
        _holdCts?.Cancel();
        _armCts?.Cancel();
        _keyHeld = false;
        Armed = false;
        State = Phase.Idle;
        Announcement = "";
        OnReset();
        StateHasChanged();
    }

    protected virtual void OnReset() { }

    protected async Task StartHoldAsync()
    {
        if (Disabled || State != Phase.Idle) return;
        State = Phase.Holding;
        _holdCts?.Cancel();
        _holdCts = new CancellationTokenSource();
        var token = _holdCts.Token;
        try { await Task.Delay(HoldMs, token); }
        catch (TaskCanceledException) { return; }
        if (State == Phase.Holding) await CompleteAsync();
    }

    protected void CancelHold()
    {
        if (State != Phase.Holding) return;
        _holdCts?.Cancel();
        State = Phase.Idle;
    }

    protected async Task CompleteAsync()
    {
        if (State == Phase.Done) return;   // never fires twice
        _holdCts?.Cancel();
        _armCts?.Cancel();
        Armed = false;
        State = Phase.Done;
        Announcement = L.T("send.slide.announce_confirmed", BusyLabel);
        StateHasChanged();
        await OnConfirmed.InvokeAsync();
    }

    protected Task OnKeyDown(KeyboardEventArgs e)
    {
        if (!IsConfirmKey(e)) return Task.CompletedTask;
        _keyHeld = true;
        _lastKeyAt = DateTime.UtcNow;
        return e.Repeat ? Task.CompletedTask : StartHoldAsync();
    }

    protected void OnKeyUp(KeyboardEventArgs e)
    {
        if (!IsConfirmKey(e)) return;
        _keyHeld = false;
        _lastKeyAt = DateTime.UtcNow;
        CancelHold();
    }

    protected void OnBlur()
    {
        _keyHeld = false;
        CancelHold();
    }

    /// <summary>
    /// A click with detail 0 was not made by a pointer. Buttons also synthesise one from Enter and Space,
    /// so those are ignored here (the key handlers own them); what is left is assistive technology.
    /// </summary>
    protected async Task OnClick(MouseEventArgs e)
    {
        if (e.Detail != 0 || Disabled || State != Phase.Idle) return;
        if (_keyHeld || (DateTime.UtcNow - _lastKeyAt).TotalMilliseconds < 400) return;

        if (Armed)
        {
            await CompleteAsync();
            return;
        }

        Armed = true;
        Announcement = L.T("send.slide.announce_armed");
        _armCts?.Cancel();
        _armCts = new CancellationTokenSource();
        var token = _armCts.Token;
        StateHasChanged();
        try { await Task.Delay(5000, token); }
        catch (TaskCanceledException) { return; }
        if (State == Phase.Idle)
        {
            Armed = false;
            Announcement = L.T("send.slide.announce_cancelled");
            StateHasChanged();
        }
    }

    private static bool IsConfirmKey(KeyboardEventArgs e) => e.Key is "Enter" or " " or "Spacebar";

    public virtual void Dispose()
    {
        _holdCts?.Cancel();
        _armCts?.Cancel();
        GC.SuppressFinalize(this);
    }
}

using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The one place pages report a history load to (spec 025). It applies <see cref="FirstReceiveRule"/>, keeps the wallet's
/// stored state, holds a first payment that is waiting to be celebrated, and owns the per-device "first session ended"
/// marker that the install banner and the sponsors strip wait for. Nothing here touches keys or the network.
/// </summary>
public sealed class FirstReceiveCoordinator(FirstReceiveStore store)
{
    // One caller at a time: two pages loading at once must not both detect (or both mark) the first payment.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, FirstPayment> _pending = new(StringComparer.Ordinal);
    private bool _initialized;
    private bool _otherDialogOpen;

    /// <summary>Raised when a celebration becomes pending or is shown, when the first session ends, or when a blocking dialog opens or closes.</summary>
    public event Action? Changed;

    /// <summary>
    /// True once any wallet on this device has had a transaction. False until <see cref="InitializeAsync"/> has run, so the
    /// items that wait for it never appear and then disappear.
    /// </summary>
    public bool FirstSessionEnded { get; private set; }

    /// <summary>A dialog owned by the layout (the "What's New" window) is open, so a celebration must wait (FR-023).</summary>
    public bool OtherDialogOpen
    {
        get => _otherDialogOpen;
        set
        {
            if (_otherDialogOpen == value) return;
            _otherDialogOpen = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The menu's scanner or its More list is open, so a celebration must wait (FR-023).</summary>
    public bool ShellDialogOpen
    {
        get => _shellDialogOpen;
        set
        {
            if (_shellDialogOpen == value) return;
            _shellDialogOpen = value;
            Changed?.Invoke();
        }
    }
    private bool _shellDialogOpen;

    /// <summary>
    /// What a network history load proves. It is judged from that load's own result, never from the wallet service's shared
    /// <see cref="WalletService.LastHistoryIncomplete"/>, which another load running at the same time can overwrite (FR-015).
    /// </summary>
    public static HistorySource SourceOf(WalletService.HistoryLoad load) =>
        !load.Complete ? HistorySource.IncompleteLoad
        : !load.AddressesSettled ? HistorySource.PartialAddressLoad
        : HistorySource.CompleteLoad;

    /// <summary>
    /// Loads the first-session marker, once. When it is absent, <paramref name="anyWalletHasSavedHistory"/> is asked once:
    /// a device that already has a wallet with saved history (an existing user after an update) is past its first session.
    /// </summary>
    public async Task InitializeAsync(Func<Task<bool>>? anyWalletHasSavedHistory = null)
    {
        if (_initialized) return;
        _initialized = true;

        var ended = await store.IsFirstSessionEndedAsync();
        if (!ended && anyWalletHasSavedHistory is not null)
        {
            try { ended = await anyWalletHasSavedHistory(); } catch { ended = false; }
            if (ended) await store.EndFirstSessionAsync();
        }
        if (ended) EndFirstSession();
    }

    /// <summary>
    /// A wallet just created on this device from a newly generated recovery phrase cannot have history: record it as empty
    /// without waiting for the network (FR-011). Only the create page calls this; recovered and imported wallets are decided
    /// by their history.
    /// </summary>
    public Task MarkCreatedEmptyAsync(string walletId) =>
        string.IsNullOrEmpty(walletId) ? Task.CompletedTask : store.SetAsync(walletId, FirstReceiveState.Empty);

    /// <summary>
    /// Reports a list of the wallet's transactions and what it proves. Returns the wallet's first payment when this list
    /// contains it and it has not been celebrated yet (the same payment is returned again by later loads until
    /// <see cref="MarkShownAsync"/>), otherwise null.
    /// </summary>
    public async Task<FirstPayment?> ObserveAsync(string? walletId, IReadOnlyList<TransactionRecord>? transactions, HistorySource source)
    {
        if (string.IsNullOrEmpty(walletId)) return null;
        transactions ??= [];

        var notify = false;
        FirstPayment? payment;
        await _gate.WaitAsync();
        try
        {
            var current = await store.GetAsync(walletId);
            var outcome = FirstReceiveRule.Evaluate(current, transactions, source);
            if (outcome.State != current) await store.SetAsync(walletId, outcome.State);

            // Any transaction, from any source, means this device is past its first session.
            if (transactions.Count > 0 && !FirstSessionEnded)
            {
                await store.EndFirstSessionAsync();
                FirstSessionEnded = true;
                notify = true;
            }

            payment = outcome.Payment;
            if (payment is not null)
            {
                notify |= !_pending.TryGetValue(walletId, out var known) || !SamePayment(known, payment);
                _pending[walletId] = payment;
            }
            else if (outcome.State != FirstReceiveState.Empty)
            {
                // No longer a wallet that can celebrate (for example its first transaction was not a DGB receipt).
                notify |= _pending.Remove(walletId);
            }
        }
        finally { _gate.Release(); }

        if (notify) Changed?.Invoke();
        return payment;
    }

    /// <summary>The first payment waiting to be celebrated for this wallet, if any.</summary>
    public FirstPayment? Pending(string? walletId) =>
        !string.IsNullOrEmpty(walletId) && _pending.TryGetValue(walletId, out var payment) ? payment : null;

    /// <summary>
    /// The celebration is on screen: it never shows again for this wallet, whatever happens next (closing it, leaving the
    /// page, the wallet locking).
    /// </summary>
    public async Task MarkShownAsync(string? walletId)
    {
        if (string.IsNullOrEmpty(walletId)) return;

        await _gate.WaitAsync();
        try
        {
            await store.SetAsync(walletId, FirstReceiveState.Celebrated);
            _pending.Remove(walletId);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private void EndFirstSession()
    {
        if (FirstSessionEnded) return;
        FirstSessionEnded = true;
        Changed?.Invoke();
    }

    private static bool SamePayment(FirstPayment a, FirstPayment b) =>
        a.AmountDgb == b.AmountDgb && a.IsPending == b.IsPending && a.TxIds.SequenceEqual(b.TxIds);
}

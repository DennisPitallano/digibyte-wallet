using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Says "payment received" once per incoming transaction that appears while a page is open. One per page that checks
/// (Home, Receive): the list of transactions already seen lives exactly as long as the page, starts from the wallet's saved
/// history when there is one, and starts again on a wallet switch. This is Home's notice, moved here so Receive shows the
/// same one (spec 025, FR-022, FR-026).
/// </summary>
public sealed class IncomingPaymentAnnouncer(NotificationService notifications, IJSRuntime js)
{
    private string? _walletId;
    // Transaction IDs already seen for the wallet; null until the first list (saved or from the network) has been recorded.
    private HashSet<string>? _known;

    /// <summary>
    /// Records the wallet's saved history as already seen, if nothing has been recorded for it yet. A payment that is in the
    /// saved history is then never announced, and one that is not can be.
    /// </summary>
    public void Seed(string? walletId, IReadOnlyList<TransactionRecord>? saved)
    {
        if (string.IsNullOrEmpty(walletId) || saved is not { Count: > 0 }) return;
        Follow(walletId);
        _known ??= saved.Select(t => t.TxId).ToHashSet();
    }

    /// <summary>Forgets what has been seen (a wallet switch): the next list is recorded without announcing anything.</summary>
    public void Forget()
    {
        _walletId = null;
        _known = null;
    }

    /// <summary>
    /// Reports a freshly loaded list. Shows the notice for transactions that just arrived, except
    /// <paramref name="celebrated"/>: those are the wallet's first payment, which the celebration announces instead.
    /// </summary>
    public async Task AnnounceAsync(string? walletId, IReadOnlyList<TransactionRecord> transactions, bool hideAmounts,
        IReadOnlyCollection<string>? celebrated = null)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        Follow(walletId);

        var arrived = NewArrivals.Find(_known, transactions, DateTime.UtcNow)
            .Where(t => celebrated is null || !celebrated.Contains(t.TxId))
            .ToList();
        _known ??= [];
        foreach (var t in transactions) _known.Add(t.TxId);
        if (arrived.Count == 0) return;

        var message = hideAmounts
            ? (arrived.Count == 1 ? "Payment received" : $"{arrived.Count} payments received")
            : arrived.Count == 1
                ? $"+{arrived[0].AmountDgb:0.########} DGB received"
                : $"{arrived.Count} payments received, +{arrived.Sum(t => t.AmountDgb):0.########} DGB";
        notifications.Success(message);
        try { await js.InvokeVoidAsync("domHelpers.haptic", new[] { 15, 40, 25 }); } catch { }
    }

    private void Follow(string walletId)
    {
        if (_walletId == walletId) return;
        _walletId = walletId;
        _known = null;
    }
}

using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>What this device knows about how a wallet started (spec 025, data-model.md). Stored per wallet.</summary>
public enum FirstReceiveState
{
    /// <summary>Nothing is known yet. Never stored.</summary>
    Undecided,
    /// <summary>The wallet started empty on this device: its first payment is celebrated, once.</summary>
    Empty,
    /// <summary>The wallet already had history, or its first transaction was not a DGB receipt. Never celebrates.</summary>
    History,
    /// <summary>The first-payment celebration has been shown. Never again.</summary>
    Celebrated,
}

/// <summary>Where a list of transactions came from, which decides how much it proves.</summary>
public enum HistorySource
{
    /// <summary>The chain was read and the wallet has finished finding which of its addresses have been used.</summary>
    CompleteLoad,
    /// <summary>The chain was read, but address discovery has not finished: an empty list does not prove the wallet is empty.</summary>
    PartialAddressLoad,
    /// <summary>The chain could not be read (or only in part): proves nothing.</summary>
    IncompleteLoad,
    /// <summary>History saved on this device earlier: it can prove the wallet has history, never that it is empty.</summary>
    SavedSnapshot,
}

/// <summary>A wallet's first incoming DGB payment, as found in one load.</summary>
/// <param name="AmountDgb">The sum of every DGB receipt in that load.</param>
/// <param name="IsPending">True when every receipt in that load is still unconfirmed.</param>
/// <param name="TxIds">The receipts' transaction IDs.</param>
public sealed record FirstPayment(decimal AmountDgb, bool IsPending, IReadOnlyList<string> TxIds);

/// <summary>The state to store after a load, and the first payment if this load contained it.</summary>
public sealed record FirstReceiveOutcome(FirstReceiveState State, FirstPayment? Payment);

/// <summary>
/// Decides whether a wallet started empty and whether a load contains its first payment (spec 025, FR-011 to FR-015).
/// Pure: the caller stores the state and shows the celebration. It never returns <see cref="FirstReceiveState.Celebrated"/>;
/// that move happens when the celebration is actually shown.
/// </summary>
public static class FirstReceiveRule
{
    public static FirstReceiveOutcome Evaluate(
        FirstReceiveState current, IReadOnlyList<TransactionRecord> transactions, HistorySource source)
    {
        var unchanged = new FirstReceiveOutcome(current, null);

        // Settled for good, and a load that could not read the chain proves nothing.
        if (current is FirstReceiveState.History or FirstReceiveState.Celebrated) return unchanged;
        if (source == HistorySource.IncompleteLoad) return unchanged;

        // Saved history can prove a wallet has history; it can never prove it is empty, and it never celebrates.
        if (source == HistorySource.SavedSnapshot)
            return current == FirstReceiveState.Undecided && transactions.Count > 0
                ? new FirstReceiveOutcome(FirstReceiveState.History, null)
                : unchanged;

        // From here the chain was read (CompleteLoad or PartialAddressLoad).
        if (current == FirstReceiveState.Undecided)
        {
            if (transactions.Count > 0) return new FirstReceiveOutcome(FirstReceiveState.History, null);
            // Empty is only proven once every used address is known.
            return source == HistorySource.CompleteLoad ? new FirstReceiveOutcome(FirstReceiveState.Empty, null) : unchanged;
        }

        // current == Empty
        if (transactions.Count == 0) return unchanged;

        var receipts = transactions.Where(IsDgbReceipt).ToList();
        if (receipts.Count == 0) return new FirstReceiveOutcome(FirstReceiveState.History, null);

        var payment = new FirstPayment(
            receipts.Sum(t => t.AmountSatoshis) / 100_000_000m,
            receipts.All(t => t.Confirmations <= 0),
            receipts.Select(t => t.TxId).ToList());
        return new FirstReceiveOutcome(FirstReceiveState.Empty, payment);
    }

    /// <summary>An incoming transaction that moved DGB to the wallet (a DigiDollar transfer, for one, moves none).</summary>
    private static bool IsDgbReceipt(TransactionRecord tx) =>
        tx.Direction == TransactionDirection.Received && tx.AmountSatoshis > 0;
}

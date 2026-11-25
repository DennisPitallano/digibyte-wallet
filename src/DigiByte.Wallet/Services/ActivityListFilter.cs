using System.Globalization;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// What Activity's list is showing: its filter (<c>all</c>, <c>received</c>, <c>sent</c>), search text and chosen wallet address,
/// plus the lookups the search uses. Passed to the export so "What the list shows" is exactly the list (spec 031, US2).
/// </summary>
/// <param name="NameOf">The contact or own-wallet name for a counterparty address, or null.</param>
/// <param name="WalletLabels">Labels of the wallet's own addresses, keyed by <see cref="AddressKey.Normalize"/>.</param>
public sealed record ActivityView(
    string Filter,
    string Query,
    string? SelectedAddress,
    Func<string, string?> NameOf,
    IReadOnlyDictionary<string, string> WalletLabels);

/// <summary>Activity's filter and search rules, shared by the list and the CSV export so the two never disagree.</summary>
public static class ActivityListFilter
{
    public static bool Matches(TransactionRecord tx, ActivityView view)
    {
        var directionOk = view.Filter switch
        {
            "received" => tx.Direction == TransactionDirection.Received,
            "sent" => tx.Direction == TransactionDirection.Sent,
            _ => true,
        };
        if (!directionOk) return false;
        if (view.SelectedAddress != null && !ActivityAddressIndex.Matches(tx, view.SelectedAddress)) return false;
        return view.Query.Length == 0 || MatchesQuery(tx, view);
    }

    /// <summary>The name Activity shows for the counterparty: the saved contact, or the name recorded with the transaction.</summary>
    public static string? NameFor(TransactionRecord tx, Func<string, string?> nameOf) =>
        tx.CounterpartyAddress is { Length: > 0 } a && nameOf(a) is { } n ? n : tx.CounterpartyName;

    private static bool MatchesQuery(TransactionRecord t, ActivityView view)
    {
        const StringComparison ci = StringComparison.OrdinalIgnoreCase;
        var query = view.Query;
        return (NameFor(t, view.NameOf)?.Contains(query, ci) ?? false)
            || (t.CounterpartyAddress?.Contains(query, ci) ?? false)
            || ActivityAddressIndex.MatchesText(t, query, view.WalletLabels)
            || (t.Memo?.Contains(query, ci) ?? false)
            || t.TxId.Contains(query, ci)
            || t.AmountDgb.ToString("0.########", CultureInfo.InvariantCulture).Contains(query, ci);
    }
}

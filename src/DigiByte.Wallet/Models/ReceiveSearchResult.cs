namespace DigiByte.Wallet.Models;

/// <summary>How a search of the wallet's first addresses ended (spec 027, data-model.md section 3).</summary>
public enum ReceiveSearchOutcome
{
    /// <summary>Every unchecked address below the count asked for was looked up.</summary>
    Finished,
    /// <summary>Stopped on request, or because the page was left or the wallet locked.</summary>
    Cancelled,
    /// <summary>A lookup failed. Never to be shown as "nothing found".</summary>
    Failed,
    /// <summary>The data source reported history for an address nobody has ever used, so its answers were not believed.</summary>
    Untrusted,
    /// <summary>The wallet already checks every address below the count asked for.</summary>
    NothingToCheck,
}

/// <summary>
/// One run of the search. All extents are counts from the first address: the search covers indexes 0 to
/// <paramref name="RequestedUpTo"/> - 1.
/// </summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="RequestedUpTo">The count asked for.</param>
/// <param name="ReachedUpTo">Every address below this count was checked, or was already checked. 0 when nothing was.</param>
/// <param name="AddedIndexes">Indexes this run added as paid, or set to paid.</param>
/// <param name="AddedSatoshis">What those addresses hold; null when that could not be read (the addresses are still added).</param>
/// <param name="LeavesUnwatched">Some addresses below <paramref name="ReachedUpTo"/> are not checked from now on: a later payment to one will not appear until another search.</param>
/// <param name="SuggestSearchingFurther">A paid address was found within 20 of the end of the range, and the range can still be widened.</param>
/// <param name="ShowForeignIssuerNotice">The first time this wallet has had addresses added: say the key seems to be in use elsewhere.</param>
/// <param name="AlreadyCheckedUpTo">For <see cref="ReceiveSearchOutcome.NothingToCheck"/>: how many addresses the wallet already checks.</param>
/// <param name="Error">For <see cref="ReceiveSearchOutcome.Failed"/>: what the data source said.</param>
public sealed record ReceiveSearchResult(
    ReceiveSearchOutcome Outcome,
    int RequestedUpTo,
    int ReachedUpTo,
    IReadOnlyList<int> AddedIndexes,
    long? AddedSatoshis,
    bool LeavesUnwatched,
    bool SuggestSearchingFurther,
    bool ShowForeignIssuerNotice,
    int AlreadyCheckedUpTo,
    string? Error);

/// <summary>Reported after each batch of a search: how many addresses are covered out of how many, and how many were found paid.</summary>
public sealed record ReceiveSearchProgress(int CheckedUpTo, int RequestedUpTo, int FoundSoFar);

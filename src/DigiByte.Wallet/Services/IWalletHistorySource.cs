namespace DigiByte.Wallet.Services;

/// <summary>
/// Optional ability (spec 023): the history of a whole wallet in one pass, each transaction processed once. The result is the
/// same as the union of per-address <see cref="IBlockchainService.GetAddressTransactionsAsync"/> answers.
/// </summary>
public interface IWalletHistorySource
{
    /// <summary>All transactions paying or spending any of the given addresses. Throws when it cannot answer.</summary>
    Task<List<TransactionInfo>> GetWalletTransactionsAsync(IReadOnlyList<string> addresses);
}

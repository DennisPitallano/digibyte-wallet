using DigiByte.Crypto.Transactions;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Interface for communicating with the DigiByte blockchain
/// via our own node's API.
/// </summary>
public interface IBlockchainService
{
    Task<long> GetBalanceAsync(string address);
    Task<long> GetBalanceAsync(IEnumerable<string> addresses);
    Task<List<UtxoInfo>> GetUtxosAsync(string address);
    Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses);
    Task<string> BroadcastTransactionAsync(byte[] rawTransaction);
    Task<TransactionInfo?> GetTransactionAsync(string txId);
    Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50);
    Task<decimal> GetFeeRateAsync();
    Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD");
    Task<int> GetBlockHeightAsync();
}

public class UtxoInfo
{
    public required string TxId { get; init; }
    public required uint OutputIndex { get; init; }
    public required long AmountSatoshis { get; init; }
    public required string ScriptPubKey { get; init; }
    public required int Confirmations { get; init; }
}

public class TransactionInfo
{
    public required string TxId { get; init; }
    public required int Confirmations { get; init; }
    public required DateTime Timestamp { get; init; }
    public required long FeeSatoshis { get; init; }
    public required List<TxInput> Inputs { get; init; }
    public required List<TxOutput> Outputs { get; init; }
}

public class TxInput
{
    public required string Address { get; init; }
    public required long AmountSatoshis { get; init; }
}

public class TxOutput
{
    public required string Address { get; init; }
    public required long AmountSatoshis { get; init; }
    public required uint Index { get; init; }
}

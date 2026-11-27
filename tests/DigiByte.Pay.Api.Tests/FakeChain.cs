using System.Collections.Concurrent;
using DigiByte.Pay.Api.Services;
using DigiByte.Wallet.Services;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// Stand-in for the DGB explorer behind <see cref="InvoiceMonitor"/> (specs/033 T002): tests add payments per address
/// and run one monitor pass with <c>InvoiceMonitor.RunOnceAsync()</c>.
/// </summary>
public sealed class FakeChain : IBlockchainService, IChainServiceProvider
{
    private readonly ConcurrentDictionary<string, List<TransactionInfo>> _byAddress = new();

    /// <summary>When true, every lookup throws, like an unreachable explorer.</summary>
    public bool Down { get; set; }

    public IBlockchainService For(string network) => this;

    /// <summary>Records a payment of <paramref name="sats"/> to <paramref name="address"/> and returns its txid.</summary>
    public string Pay(string address, long sats, int confirmations = 0)
    {
        var txid = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tx = new TransactionInfo
        {
            TxId = txid,
            Confirmations = confirmations,
            Timestamp = DateTime.UtcNow,
            FeeSatoshis = 1000,
            Inputs = [],
            Outputs = [new TxOutput { Address = address, AmountSatoshis = sats, Index = 0 }],
        };
        _byAddress.AddOrUpdate(address, _ => [tx], (_, list) => { lock (list) list.Add(tx); return list; });
        return txid;
    }

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        if (Down) throw new HttpRequestException("explorer down");
        if (!_byAddress.TryGetValue(address, out var list)) return Task.FromResult(new List<TransactionInfo>());
        lock (list) return Task.FromResult(list.Skip(skip).Take(take).ToList());
    }

    public Task<long> GetBalanceAsync(string address) => throw new NotSupportedException();
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) => throw new NotSupportedException();
    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => throw new NotSupportedException();
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
    public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
    public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();
    public Task<int> GetBlockHeightAsync() => throw new NotSupportedException();
}

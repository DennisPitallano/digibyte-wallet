namespace DigiByte.Wallet.Services;

/// <summary>
/// Mock blockchain service that returns fake demo data for UI testing.
/// NOT connected to any real network (testnet or mainnet).
/// All data is fabricated for development/testing purposes only.
/// </summary>
public class MockBlockchainService : IBlockchainService
{
    public const string DemoNotice = "DEMO MODE — Data is simulated, not from testnet or mainnet.";

    public bool IsActive { get; private set; }

    public Task<long> GetBalanceAsync(string address)
    {
        IsActive = true;
        // 1,250.5 DGB in satoshis
        return Task.FromResult(125_050_000_000L);
    }

    public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        IsActive = true;
        return Task.FromResult(125_050_000_000L);
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        IsActive = true;
        return Task.FromResult(new List<UtxoInfo>
        {
            new() { TxId = "demo_utxo_001", OutputIndex = 0, AmountSatoshis = 100_000_000_000L, ScriptPubKey = "", Confirmations = 150 },
            new() { TxId = "demo_utxo_002", OutputIndex = 1, AmountSatoshis = 25_050_000_000L, ScriptPubKey = "", Confirmations = 42 },
        });
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
        => GetUtxosAsync(addresses.FirstOrDefault() ?? "");

    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        IsActive = true;
        return Task.FromResult("demo_tx_broadcast_not_real");
    }

    public Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        IsActive = true;
        return Task.FromResult<TransactionInfo?>(null);
    }

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        IsActive = true;
        var now = DateTime.UtcNow;
        return Task.FromResult(new List<TransactionInfo>
        {
            new()
            {
                TxId = "demo_tx_received_001",
                Confirmations = 312,
                Timestamp = now.AddHours(-2),
                FeeSatoshis = 1_000,
                Inputs = [new() { Address = "dgb1qdemoaddress_sender_001", AmountSatoshis = 50_000_000_000L }],
                Outputs = [new() { Address = address, AmountSatoshis = 50_000_000_000L, Index = 0 }],
            },
            new()
            {
                TxId = "demo_tx_sent_001",
                Confirmations = 150,
                Timestamp = now.AddHours(-5),
                FeeSatoshis = 2_000,
                Inputs = [new() { Address = address, AmountSatoshis = 10_000_000_000L }],
                Outputs = [new() { Address = "dgb1qdemoaddress_recipient_001", AmountSatoshis = 10_000_000_000L, Index = 0 }],
            },
            new()
            {
                TxId = "demo_tx_received_002",
                Confirmations = 1024,
                Timestamp = now.AddDays(-1),
                FeeSatoshis = 1_500,
                Inputs = [new() { Address = "dgb1qdemoaddress_sender_002", AmountSatoshis = 75_050_000_000L }],
                Outputs = [new() { Address = address, AmountSatoshis = 75_050_000_000L, Index = 0 }],
            },
        });
    }

    public Task<decimal> GetFeeRateAsync()
    {
        IsActive = true;
        return Task.FromResult(0.001m);
    }

    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        IsActive = true;
        // Simulated price
        return Task.FromResult(0.008m);
    }

    public Task<int> GetBlockHeightAsync()
    {
        IsActive = true;
        return Task.FromResult(19_500_000);
    }
}

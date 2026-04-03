namespace DigiByte.Wallet.Services;

/// <summary>
/// Wraps the real BlockchainApiService and falls back to MockBlockchainService
/// when the real API is unreachable. Tracks whether demo mode is active.
/// </summary>
public class FallbackBlockchainService : IBlockchainService
{
    private readonly BlockchainApiService _real;
    private readonly MockBlockchainService _mock;

    public bool IsDemoMode { get; private set; }
    public bool ForceDemoMode { get; set; }

    public FallbackBlockchainService(BlockchainApiService real, MockBlockchainService mock)
    {
        _real = real;
        _mock = mock;
    }

    public void SetNetwork(bool isTestnet) => _real.SetNetwork(isTestnet);

    private bool UseMock => ForceDemoMode;

    public async Task<long> GetBalanceAsync(string address)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetBalanceAsync(address); }
        try { var r = await _real.GetBalanceAsync(address); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetBalanceAsync(address); }
    }

    public async Task<long> GetBalanceAsync(IEnumerable<string> addresses)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetBalanceAsync(addresses); }
        try { var r = await _real.GetBalanceAsync(addresses); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetBalanceAsync(addresses); }
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetUtxosAsync(address); }
        try { var r = await _real.GetUtxosAsync(address); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetUtxosAsync(address); }
    }

    public async Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetUtxosAsync(addresses); }
        try { var r = await _real.GetUtxosAsync(addresses); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetUtxosAsync(addresses); }
    }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.BroadcastTransactionAsync(rawTransaction); }
        try { var r = await _real.BroadcastTransactionAsync(rawTransaction); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.BroadcastTransactionAsync(rawTransaction); }
    }

    public async Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetTransactionAsync(txId); }
        try { var r = await _real.GetTransactionAsync(txId); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetTransactionAsync(txId); }
    }

    public async Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetAddressTransactionsAsync(address, skip, take); }
        try { var r = await _real.GetAddressTransactionsAsync(address, skip, take); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetAddressTransactionsAsync(address, skip, take); }
    }

    public async Task<decimal> GetFeeRateAsync()
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetFeeRateAsync(); }
        try { var r = await _real.GetFeeRateAsync(); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetFeeRateAsync(); }
    }

    public async Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD")
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetDgbPriceAsync(fiatCurrency); }
        try { var r = await _real.GetDgbPriceAsync(fiatCurrency); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetDgbPriceAsync(fiatCurrency); }
    }

    public async Task<int> GetBlockHeightAsync()
    {
        if (UseMock) { IsDemoMode = true; return await _mock.GetBlockHeightAsync(); }
        try { var r = await _real.GetBlockHeightAsync(); IsDemoMode = false; return r; }
        catch { IsDemoMode = true; return await _mock.GetBlockHeightAsync(); }
    }
}

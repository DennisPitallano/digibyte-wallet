using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>In-memory storage shared by the multisig tests.</summary>
internal sealed class MultisigFakeStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _store = [];

    public Task<string?> GetAsync(string key)
        => Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);

    public Task SetAsync(string key, string value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));

    public Task ClearAsync()
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
        Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());

    /// <summary>Test hook: writes raw JSON (for legacy-data tests).</summary>
    public void Put(string key, string value) => _store[key] = value;

    public string? Peek(string key) => _store.TryGetValue(key, out var v) ? v : null;
}

/// <summary>A chain for multisig tests: balance, unspent outputs, address history and broadcast results are set per test.</summary>
internal sealed class MultisigFakeChain : IBlockchainService
{
    private readonly long _balance;

    public List<UtxoInfo> Utxos { get; set; }
    public List<TransactionInfo> Transactions { get; } = [];
    public string BroadcastResult { get; set; }

    /// <summary>When set, <see cref="BroadcastTransactionAsync"/> throws it.</summary>
    public Exception? BroadcastFailure { get; set; }

    /// <summary>When true, balance, unspent and history calls throw, as when no explorer answers.</summary>
    public bool Offline { get; set; }

    /// <summary>Delay applied to broadcasts, to make concurrent calls overlap.</summary>
    public TimeSpan BroadcastDelay { get; set; } = TimeSpan.Zero;

    public int BroadcastCalls { get; private set; }
    public List<(int Skip, int Take)> HistoryRequests { get; } = [];
    public decimal FeeRate { get; set; } = 0.001m;

    public MultisigFakeChain(long balance = 0, List<UtxoInfo>? utxos = null, string broadcastResult = "fake-txid")
    {
        _balance = balance;
        Utxos = utxos ?? [];
        BroadcastResult = broadcastResult;
    }

    private void ThrowIfOffline()
    {
        if (Offline) throw new HttpRequestException("offline");
    }

    public Task<long> GetBalanceAsync(string address)
    {
        ThrowIfOffline();
        return Task.FromResult(_balance);
    }

    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => GetBalanceAsync("");

    public Task<List<UtxoInfo>> GetUtxosAsync(string address)
    {
        ThrowIfOffline();
        return Task.FromResult(Utxos);
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => GetUtxosAsync("");

    /// <summary>Hex of the last transaction handed to <see cref="BroadcastTransactionAsync"/>, or null.</summary>
    public string? LastBroadcastHex { get; private set; }

    public async Task<string> BroadcastTransactionAsync(byte[] rawTransaction)
    {
        BroadcastCalls++;
        LastBroadcastHex = Convert.ToHexString(rawTransaction);
        if (BroadcastDelay > TimeSpan.Zero) await Task.Delay(BroadcastDelay);
        if (BroadcastFailure is not null) throw BroadcastFailure;
        return BroadcastResult;
    }

    public Task<TransactionInfo?> GetTransactionAsync(string txId) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.TxId == txId));

    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50)
    {
        ThrowIfOffline();
        HistoryRequests.Add((skip, take));
        return Task.FromResult(Transactions.Skip(skip).Take(take).ToList());
    }

    public Task<decimal> GetFeeRateAsync() => Task.FromResult(FeeRate);
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
    public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
}

internal sealed class MultisigFakeCrypto : LegacyCryptoBase
{
    public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
    public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
}

using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class BatchSendTests
{
    private static readonly Network Network = DigiByteNetwork.Mainnet;
    private const string Pin = "123456";

    private static async Task<WalletService> CreateUnlockedWalletAsync(
        IBlockchainService? blockchain = null)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var storage = new InMemorySecureStorage();
        var walletStore = new WalletKeyStore(storage);
        var crypto = new FakeCryptoService();
        var bc = blockchain ?? new FakeBlockchainService();
        var txTracker = new TransactionTracker(storage);

        var service = new WalletService(walletStore, crypto, bc, txTracker);
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("Test", string.Join(' ', mnemonic.Words), Pin);
        return service;
    }

    private static string GenerateAddress()
    {
        var key = new Key();
        return key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network).ToString();
    }

    // ─── Validation ───

    [Fact]
    public async Task SendBatchAsync_EmptyList_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(new List<(string, decimal)>()));
        Assert.Contains("At least one recipient", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_NullList_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(null!));
    }

    [Fact]
    public async Task SendBatchAsync_Over20Recipients_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = Enumerable.Range(0, 21)
            .Select(_ => (GenerateAddress(), 1.0m))
            .ToList();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Maximum 20", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_DuplicateAddress_Throws()
    {
        var service = await CreateUnlockedWalletAsync();
        var addr = GenerateAddress();

        var recipients = new List<(string, decimal)>
        {
            (addr, 1.0m),
            (addr, 2.0m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_InvalidAddress_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            ("notavalidaddress", 1.0m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Invalid address", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_AmountBelowMinimum_Throws()
    {
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 0.00001m),
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("below minimum", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_WatchOnly_Throws()
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
        var hd = new HdKeyDerivation(mnemonic, network: Network);
        var xpub = hd.GetAccountExtPubKey().ToString(Network);

        var storage = new InMemorySecureStorage();
        var service = new WalletService(
            new WalletKeyStore(storage), new FakeCryptoService(),
            new FakeBlockchainService(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        await service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 1.0m),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("Watch-only", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_InsufficientFunds_Throws()
    {
        // FakeBlockchainService returns empty UTXOs by default
        var service = await CreateUnlockedWalletAsync();

        var recipients = new List<(string, decimal)>
        {
            (GenerateAddress(), 1.0m),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("No UTXOs available", ex.Message);
    }

    [Fact]
    public async Task SendBatchAsync_Exactly20Recipients_DoesNotThrowArgumentException()
    {
        // Should pass validation (but fail on insufficient funds)
        var service = await CreateUnlockedWalletAsync();

        var recipients = Enumerable.Range(0, 20)
            .Select(_ => (GenerateAddress(), 0.001m))
            .ToList();

        // Should get past the 20-recipient check and fail on UTXOs instead
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendBatchAsync(recipients));
        Assert.Contains("No UTXOs available", ex.Message);
    }

    // ─── Recording ───

    [Fact]
    public async Task SendBatchAsync_RecordsOneEntryWithFullBatchAmountAndFeeOnce()
    {
        var (service, tracker, _) = await CreateFundedWalletAsync();
        var first = GenerateAddress();

        var txId = await service.SendBatchAsync(new List<(string, decimal)>
        {
            (first, 1m),
            (GenerateAddress(), 2m),
            (GenerateAddress(), 3m),
        });

        var record = Assert.Single(await tracker.GetAllAsync());
        Assert.Equal(txId, record.TxId);
        Assert.Equal(TransactionDirection.Sent, record.Direction);
        Assert.Equal(600_000_000, record.AmountSatoshis);
        Assert.Equal(first, record.CounterpartyAddress);
        Assert.InRange(record.FeeSatoshis, 1, 10_000_000);
    }

    [Fact]
    public async Task SendBatchAsync_CountsFullBatchTowardSpendingLimit()
    {
        var (service, tracker, storage) = await CreateFundedWalletAsync();
        var limits = new SpendingLimitService(storage, tracker);
        const string walletId = "limit-wallet";
        await limits.SaveSettingsAsync(walletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 5m,
            HardBlock = true,
        });

        await service.SendBatchAsync(new List<(string, decimal)>
        {
            (GenerateAddress(), 1m),
            (GenerateAddress(), 3m),
        });

        Assert.Equal(4m, await limits.GetSpentInPeriodAsync(TimeSpan.FromHours(24)));

        // 4 already spent + 2 more is over the limit of 5. Counting only the
        // first recipient would give 1 + 2 and let this send through.
        var check = await limits.CheckLimitAsync(walletId, 2m);
        Assert.False(check.Allowed);
    }

    [Fact]
    public async Task SendBatchAsync_HistoryShowsFullAmountOnceChainSyncReturnsTheTx()
    {
        var blockchain = new FundedBlockchainService();
        var (service, _, _) = await CreateFundedWalletAsync(blockchain);
        var first = GenerateAddress();
        var second = GenerateAddress();

        var txId = await service.SendBatchAsync(new List<(string, decimal)>
        {
            (first, 1m),
            (second, 3m),
        });

        // The explorer reports both outputs, but history merging prefers the local record.
        blockchain.ExplorerTxs.Add(new TransactionInfo
        {
            TxId = txId,
            Confirmations = 1,
            Timestamp = DateTime.UtcNow,
            FeeSatoshis = 1_000,
            Inputs = [new TxInput { Address = service.GetAllAddresses()[0], AmountSatoshis = 10_000_000_000 }],
            Outputs =
            [
                new TxOutput { Address = first, AmountSatoshis = 100_000_000, Index = 0 },
                new TxOutput { Address = second, AmountSatoshis = 300_000_000, Index = 1 },
            ],
        });

        var record = Assert.Single(await service.GetTransactionHistoryAsync());
        Assert.Equal(400_000_000, record.AmountSatoshis);
    }

    // ─── Repairing records saved before the fix ───

    private const string OldTxId = "aa00000000000000000000000000000000000000000000000000000000000001";

    private static TransactionInfo ExplorerTx(WalletService service, params (string Address, long Satoshis)[] outputs) => new()
    {
        TxId = OldTxId,
        Confirmations = 100,
        Timestamp = DateTime.UtcNow,
        FeeSatoshis = 1_000,
        Inputs = [new TxInput { Address = service.GetAllAddresses()[0], AmountSatoshis = 10_000_000_000 }],
        Outputs = outputs
            .Select((o, i) => new TxOutput { Address = o.Address, AmountSatoshis = o.Satoshis, Index = (uint)i })
            .ToList(),
    };

    [Fact]
    public async Task History_RepairsUnderRecordedBatch_AndSpendingLimitSeesIt()
    {
        var blockchain = new FundedBlockchainService();
        var (service, tracker, storage) = await CreateFundedWalletAsync(blockchain);
        var first = GenerateAddress();
        await tracker.RecordSendAsync(OldTxId, first, 100_000_000, 1_000);
        var ownChange = service.GetAllAddresses().Last();
        blockchain.ExplorerTxs.Add(ExplorerTx(service,
            (first, 100_000_000), (GenerateAddress(), 300_000_000), (ownChange, 500_000_000)));

        var record = Assert.Single(await service.GetTransactionHistoryAsync());

        Assert.Equal(400_000_000, record.AmountSatoshis);
        Assert.Equal(first, record.CounterpartyAddress);
        Assert.Equal(4m, await new SpendingLimitService(storage, tracker).GetSpentInPeriodAsync(TimeSpan.FromHours(24)));

        // The repair is saved, not just applied to what history returned.
        var reloaded = new TransactionTracker(storage);
        reloaded.SetActiveWallet(service.ActiveWallet!.Id);
        Assert.Equal(400_000_000, Assert.Single(await reloaded.GetAllAsync()).AmountSatoshis);
    }

    [Fact]
    public async Task History_LeavesSingleSendAlone_WhenChangeIsInScannedRange()
    {
        var blockchain = new FundedBlockchainService();
        var (service, tracker, _) = await CreateFundedWalletAsync(blockchain);
        var dest = GenerateAddress();
        await tracker.RecordSendAsync(OldTxId, dest, 100_000_000, 1_000);
        blockchain.ExplorerTxs.Add(ExplorerTx(service,
            (dest, 100_000_000), (service.GetAllAddresses().Last(), 500_000_000)));

        var record = Assert.Single(await service.GetTransactionHistoryAsync());

        Assert.Equal(100_000_000, record.AmountSatoshis);
    }

    [Fact]
    public async Task History_LeavesSingleSendAlone_WhenChangeIsBeyondScannedRange()
    {
        // Change past the scanned addresses looks external. Treating it as a second recipient
        // would inflate the amount and wrongly use up the user's spending limit.
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var blockchain = new FundedBlockchainService();
        var (service, tracker, _) = await CreateFundedWalletAsync(blockchain, mnemonic);
        var dest = GenerateAddress();
        var change = new HdKeyDerivation(mnemonic, network: Network)
            .DeriveChangeAddress(15, ScriptPubKeyType.Segwit).ToString();
        Assert.DoesNotContain(change, service.GetAllAddresses());
        await tracker.RecordSendAsync(OldTxId, dest, 100_000_000, 1_000);
        blockchain.ExplorerTxs.Add(ExplorerTx(service, (dest, 100_000_000), (change, 500_000_000)));

        var record = Assert.Single(await service.GetTransactionHistoryAsync());

        Assert.Equal(100_000_000, record.AmountSatoshis);
    }

    [Fact]
    public async Task History_LeavesRecordAlone_WhenItDoesNotMatchAnOutput()
    {
        var blockchain = new FundedBlockchainService();
        var (service, tracker, _) = await CreateFundedWalletAsync(blockchain);
        var dest = GenerateAddress();
        await tracker.RecordSendAsync(OldTxId, dest, 100_000_000, 1_000);
        blockchain.ExplorerTxs.Add(ExplorerTx(service,
            (dest, 200_000_000), (GenerateAddress(), 300_000_000)));

        var record = Assert.Single(await service.GetTransactionHistoryAsync());

        Assert.Equal(100_000_000, record.AmountSatoshis);
    }

    private static async Task<(WalletService Service, TransactionTracker Tracker, InMemorySecureStorage Storage)>
        CreateFundedWalletAsync(FundedBlockchainService? blockchain = null, Mnemonic? mnemonic = null)
    {
        mnemonic ??= new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var storage = new InMemorySecureStorage();
        var tracker = new TransactionTracker(storage);
        var service = new WalletService(
            new WalletKeyStore(storage), new FakeCryptoService(),
            blockchain ?? new FundedBlockchainService(), tracker);
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("Test", string.Join(' ', mnemonic.Words), Pin);
        return (service, tracker, storage);
    }

    // ─── Test helpers ───

    /// <summary>Gives every wallet address one confirmed 100 DGB UTXO, and serves explorer history from a list.</summary>
    private class FundedBlockchainService : FakeBlockchainService
    {
        public List<TransactionInfo> ExplorerTxs { get; } = [];

        public override Task<List<UtxoInfo>> GetUtxosAsync(string address)
        {
            var txId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address))).ToLowerInvariant();
            return Task.FromResult(new List<UtxoInfo>
            {
                new() { TxId = txId, OutputIndex = 0, AmountSatoshis = 10_000_000_000, ScriptPubKey = "", Confirmations = 6 },
            });
        }

        public override Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) =>
            Task.FromResult(ExplorerTxs.ToList());
    }

    private class InMemorySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = new();
        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);
        public Task SetAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task ClearAsync() { _store.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
            Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private class FakeCryptoService : LegacyCryptoBase
    {
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(ciphertext);
    }

    private class FakeBlockchainService : IBlockchainService
    {
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
        public virtual Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("faketxid");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public virtual Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }
}

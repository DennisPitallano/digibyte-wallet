using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Saved address summaries and saved chain history (spec 023, T011): versions, isolation and deletion.</summary>
public class AddressSummaryStoreTests
{
    private static AddressSummary Summary(string address, int count = 1) => new()
    {
        Address = address, ConfirmedTxCount = count, ConfirmedFunded = 500, ReadAtUtc = DateTime.UtcNow,
    };

    private static TransactionInfo Tx(string id, int confirmations = 3) => new()
    {
        TxId = id, Confirmations = confirmations, Timestamp = DateTime.UtcNow, FeeSatoshis = 10,
        Inputs = [new TxInput { Address = "dgb1qfrom", AmountSatoshis = 510 }],
        Outputs = [new TxOutput { Address = "dgb1qto", AmountSatoshis = 500, Index = 0 }],
    };

    [Fact]
    public async Task SummariesAndHistory_RoundTrip()
    {
        var storage = new TestMemoryStorage();
        var store = new AddressSummaryStore(storage);

        var record = new AddressSummaryRecord();
        record.Entries["dgb1qa"] = new AddressSummaryEntry(Summary("dgb1qa", 2),
            new AddressHistoryCursor { Complete = true, NewestConfirmedTxId = "t1", PendingTxIds = ["t2"] });
        await store.SaveSummariesAsync("w1", "mainnet", record);

        var history = new SavedChainHistory();
        history.Merge(Tx("t1"), 998);
        history.Merge(Tx("t2", 0), null);
        await store.SaveHistoryAsync("w1", "mainnet", history);

        Assert.True(await storage.ContainsKeyAsync("addr_summary_w1_mainnet"));
        Assert.True(await storage.ContainsKeyAsync("chain_history_w1_mainnet"));

        var loaded = await store.LoadSummariesAsync("w1", "mainnet");
        var entry = Assert.Single(loaded.Entries).Value;
        Assert.Equal(2, entry.Summary.ConfirmedTxCount);
        Assert.True(entry.Cursor.Complete);
        Assert.Equal(["t2"], entry.Cursor.PendingTxIds);

        var loadedHistory = await store.LoadHistoryAsync("w1", "mainnet");
        Assert.Equal(998, loadedHistory.Transactions["t1"].BlockHeight);
        Assert.Null(loadedHistory.Transactions["t2"].BlockHeight);
        Assert.Equal(500, loadedHistory.Transactions["t1"].Tx.Outputs[0].AmountSatoshis);
    }

    [Fact]
    public void Merge_ReplacesBySameTxId_NeverDuplicates()
    {
        var history = new SavedChainHistory();
        history.Merge(Tx("t1", 0), null);
        history.Merge(Tx("t1", 1), 1000);

        var saved = Assert.Single(history.Transactions).Value;
        Assert.Equal(1, saved.Tx.Confirmations);
        Assert.Equal(1000, saved.BlockHeight);
    }

    [Theory]
    [InlineData("""{"Version":2,"Entries":{}}""")]
    [InlineData("not json")]
    public async Task UnknownVersionOrUnreadable_IsDiscarded(string stored)
    {
        var storage = new TestMemoryStorage();
        await storage.SetAsync("addr_summary_w1_mainnet", stored);
        await storage.SetAsync("chain_history_w1_mainnet", stored);
        var store = new AddressSummaryStore(storage);

        Assert.Empty((await store.LoadSummariesAsync("w1", "mainnet")).Entries);
        Assert.Empty((await store.LoadHistoryAsync("w1", "mainnet")).Transactions);
    }

    [Fact]
    public async Task WalletsAndNetworks_NeverShareRecords()
    {
        var store = new AddressSummaryStore(new TestMemoryStorage());
        var record = new AddressSummaryRecord();
        record.Entries["dgb1qa"] = new AddressSummaryEntry(Summary("dgb1qa"), new AddressHistoryCursor());
        await store.SaveSummariesAsync("w1", "mainnet", record);

        Assert.Empty((await store.LoadSummariesAsync("w2", "mainnet")).Entries);
        Assert.Empty((await store.LoadSummariesAsync("w1", "testnet")).Entries);
        Assert.Single((await store.LoadSummariesAsync("w1", "mainnet")).Entries);
    }

    [Fact]
    public async Task DeletingAWallet_LeavesNoneOfItsKeys_AndKeepsAnotherWallets()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var gone = service.ActiveWallet!.Id;
        env.Explorer.Pay(env.Receive(0), 5000);
        await service.GetTransactionHistoryAsync();

        var otherWords = string.Join(' ', new NBitcoin.Mnemonic(NBitcoin.Wordlist.English, NBitcoin.WordCount.Twelve).Words);
        await service.CreateWalletAsync("Other", otherWords, ChangeAddressTests.Pin);
        var kept = service.ActiveWallet!.Id;
        Assert.NotEqual(gone, kept);
        await service.GetTransactionHistoryAsync();

        foreach (var id in new[] { gone, kept })
        {
            await env.Storage.SetAsync(TransactionTracker.StorageKey(id), "[]");
            foreach (var key in WalletService.WalletSnapshotKeys(id))
                await env.Storage.SetAsync(key, "x");
        }
        await env.Storage.SetAsync("cache:dgbPrice", "0.01");

        await service.DeleteWalletAsync(gone);

        foreach (var prefix in new[] { "addr_summary_", "chain_history_", "tx_history_", "cache:balance:", "cache:transactions:", "cache:lastFetch:" })
        {
            Assert.Empty(await env.Storage.GetKeysWithPrefixAsync(prefix + gone));
            Assert.NotEmpty(await env.Storage.GetKeysWithPrefixAsync(prefix + kept));
        }
        Assert.Equal("0.01", await env.Storage.GetAsync("cache:dgbPrice"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesBothRecordsForEveryNetwork_AndKeepsOtherWallets()
    {
        var storage = new TestMemoryStorage();
        var store = new AddressSummaryStore(storage);
        foreach (var network in new[] { "mainnet", "testnet", "regtest" })
            foreach (var wallet in new[] { "w1", "w2" })
            {
                await store.SaveSummariesAsync(wallet, network, new AddressSummaryRecord());
                await store.SaveHistoryAsync(wallet, network, new SavedChainHistory());
            }

        await store.DeleteAsync("w1");

        Assert.Empty(await storage.GetKeysWithPrefixAsync("addr_summary_w1_"));
        Assert.Empty(await storage.GetKeysWithPrefixAsync("chain_history_w1_"));
        Assert.Equal(3, (await storage.GetKeysWithPrefixAsync("addr_summary_w2_")).Count);
        Assert.Equal(3, (await storage.GetKeysWithPrefixAsync("chain_history_w2_")).Count);
    }
}

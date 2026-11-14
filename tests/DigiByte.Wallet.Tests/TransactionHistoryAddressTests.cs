using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which of the wallet's own addresses a transaction ties to (spec 010): resolving them from an explorer
/// transaction, copying them onto locally tracked sends, and keeping old stored records readable.
/// </summary>
public class TransactionHistoryAddressTests
{
    // Bech32 addresses: the last 6 characters are the checksum, which AddressKey.Normalize ignores.
    private const string R1 = "dgb1qreceive0000000000000001abcdef";
    private const string R2 = "dgb1qreceive0000000000000002abcdef";
    private const string R3 = "dgb1qreceive0000000000000003abcdef";
    private const string Change1 = "dgb1qchange0000000000000001abcdef";
    private const string Other = "dgb1qsomeoneelse000000000009abcdef";
    private const string Legacy = "DLegacyAddress0000000000000000001";

    private static Dictionary<string, string> Own(params string[] addresses) =>
        addresses.ToDictionary(AddressKey.Normalize, a => a);

    private static HashSet<string> ChangeKeys(params string[] addresses) =>
        addresses.Select(AddressKey.Normalize).ToHashSet();

    private static TxInput In(string address, long sats = 1000) => new() { Address = address, AmountSatoshis = sats };

    private static TxOutput Out(string address, long sats = 1000, uint index = 0) =>
        new() { Address = address, AmountSatoshis = sats, Index = index };

    private static TransactionInfo Tx(string id, IEnumerable<TxInput> inputs, IEnumerable<TxOutput> outputs) => new()
    {
        TxId = id,
        Confirmations = 3,
        Timestamp = DateTime.UtcNow,
        FeeSatoshis = 10,
        Inputs = inputs.ToList(),
        Outputs = outputs.ToList(),
    };

    // ── Old stored data ──

    [Fact]
    public void OldStoredRecord_WithoutAddressMembers_DeserializesWithEmptyLists()
    {
        const string oldJson = """
            [{"TxId":"abc","Direction":1,"AmountSatoshis":500,"FeeSatoshis":10,
              "Timestamp":"2026-01-01T00:00:00Z","Confirmations":6,"CounterpartyAddress":"dgb1qfoo","CounterpartyName":null,"Memo":null}]
            """;

        var records = JsonSerializer.Deserialize<List<TransactionRecord>>(oldJson)!;

        Assert.Single(records);
        Assert.Empty(records[0].WalletAddresses);
        Assert.Empty(records[0].WalletReceivedAddresses);
    }

    // ── Resolving addresses from an explorer transaction ──

    [Fact]
    public void Received_PayingOneOwnAddress_ListsThatAddress()
    {
        var tx = Tx("t1", [In(Other)], [Out(R2, 700), Out(Other, 300, 1)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: false, Own(R1, R2, R3), ChangeKeys(Change1), singleKey: false);

        Assert.Equal([R2], wallet);
        Assert.Empty(receivedTo);
    }

    [Fact]
    public void Received_PayingSeveralOwnAddresses_ListsAllInOutputOrder()
    {
        var tx = Tx("t2", [In(Other)], [Out(R3, 100), Out(Other, 50, 1), Out(R1, 200, 2)]);

        var (wallet, _) = TransactionAddressResolver.Resolve(
            tx, isSent: false, Own(R1, R2, R3), ChangeKeys(Change1), singleKey: false);

        Assert.Equal([R3, R1], wallet);
    }

    [Fact]
    public void Sent_FundedFromSeveralOwnAddresses_ListsFundingAddressesAndOmitsChange()
    {
        var tx = Tx("t3",
            [In(R1), In(R2), In(R1)],
            [Out(Other, 900), Out(Change1, 80, 1)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: true, Own(R1, R2, R3, Change1), ChangeKeys(Change1), singleKey: false);

        Assert.Equal([R1, R2], wallet);
        Assert.DoesNotContain(Change1, wallet);
        Assert.Empty(receivedTo);
    }

    [Fact]
    public void Sent_ToAnotherOwnAddress_PutsReceivingAddressInReceivedTo_NotChange()
    {
        var tx = Tx("t4",
            [In(R1)],
            [Out(R2, 500), Out(Change1, 450, 1)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: true, Own(R1, R2, Change1), ChangeKeys(Change1), singleKey: false);

        Assert.Equal([R1], wallet);
        Assert.Equal([R2], receivedTo);
    }

    [Fact]
    public void Bech32VariantsOfTheSameAddress_AreDeduplicated_AndShowWalletForm()
    {
        // Same address, different checksum variant as an explorer may return it.
        const string variant = "dgb1qreceive0000000000000001zzzzzz";
        var tx = Tx("t5", [In(Other)], [Out(variant, 100), Out(R1, 100, 1)]);

        var (wallet, _) = TransactionAddressResolver.Resolve(
            tx, isSent: false, Own(R1), ChangeKeys(), singleKey: false);

        Assert.Equal([R1], wallet);
    }

    [Fact]
    public void SingleKeyWallet_Received_ListsEachOwnFormatPaid()
    {
        var tx = Tx("t6", [In(Other)], [Out(Legacy, 400)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: false, Own(Legacy, R1), ChangeKeys(), singleKey: true);

        Assert.Equal([Legacy], wallet);
        Assert.Empty(receivedTo);
    }

    [Fact]
    public void SingleKeyWallet_Sent_ListsFundingAddress_AndTreatsOwnOutputsAsChange()
    {
        var tx = Tx("t7", [In(Legacy)], [Out(Other, 400), Out(R1, 500, 1)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: true, Own(Legacy, R1), ChangeKeys(), singleKey: true);

        Assert.Equal([Legacy], wallet);
        Assert.Empty(receivedTo);
    }

    [Fact]
    public void UnknownOrMissingAddresses_GiveEmptyLists_WithoutThrowing()
    {
        var tx = Tx("t8", [In("")], [Out("", 100), Out(Other, 100, 1)]);

        var (wallet, receivedTo) = TransactionAddressResolver.Resolve(
            tx, isSent: false, Own(R1), ChangeKeys(), singleKey: false);

        Assert.Empty(wallet);
        Assert.Empty(receivedTo);
    }

    // ── Local records and the explorer's version ──

    private static TransactionRecord Local(string id, params string[] wallet) => new()
    {
        TxId = id,
        Direction = TransactionDirection.Sent,
        AmountSatoshis = 100,
        FeeSatoshis = 1,
        Timestamp = DateTime.UtcNow,
        Confirmations = 0,
        WalletAddresses = wallet.ToList(),
    };

    [Fact]
    public void CopyMissing_FillsLocalRecordFromExplorerRecord()
    {
        var local = Local("a");
        var explorer = Local("a", R1, R2);
        explorer.WalletReceivedAddresses = [R3];

        var changed = TransactionAddressResolver.CopyMissing([local], new Dictionary<string, TransactionRecord> { ["a"] = explorer });

        Assert.Equal(["a"], changed.Select(t => t.TxId));
        Assert.Equal([R1, R2], local.WalletAddresses);
        Assert.Equal([R3], local.WalletReceivedAddresses);
    }

    [Fact]
    public void CopyMissing_NoExplorerData_LeavesListsEmpty()
    {
        var local = Local("a");

        var changed = TransactionAddressResolver.CopyMissing([local], new Dictionary<string, TransactionRecord>());

        Assert.Empty(changed);
        Assert.Empty(local.WalletAddresses);
    }

    [Fact]
    public void CopyMissing_KeepsAddressesAlreadyOnTheLocalRecord()
    {
        var local = Local("a", R3);
        var explorer = Local("a", R1);

        var changed = TransactionAddressResolver.CopyMissing([local], new Dictionary<string, TransactionRecord> { ["a"] = explorer });

        Assert.Empty(changed);
        Assert.Equal([R3], local.WalletAddresses);
    }

    [Fact]
    public async Task Tracker_SavesCopiedAddresses_SoTheySurviveAReload()
    {
        var storage = new InMemorySecureStorage();
        var tracker = new TransactionTracker(storage);
        tracker.SetActiveWallet("w1");
        await tracker.RecordSendAsync("tx1", Other, 1000, 10);

        await tracker.SetWalletAddressesAsync(new Dictionary<string, (List<string> Wallet, List<string> ReceivedTo)>
        {
            ["tx1"] = ([R1], [R2]),
        });

        // A fresh tracker over the same storage models a reload with the explorer unreachable.
        var reloaded = new TransactionTracker(storage);
        reloaded.SetActiveWallet("w1");
        var tx = Assert.Single(await reloaded.GetAllAsync());
        Assert.Equal([R1], tx.WalletAddresses);
        Assert.Equal([R2], tx.WalletReceivedAddresses);
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
}

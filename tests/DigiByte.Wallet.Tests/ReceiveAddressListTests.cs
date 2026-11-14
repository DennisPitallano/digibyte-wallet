using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The "Your addresses" list on Receive (spec 012): which addresses are listed, in what order, how many are shown,
/// what each one is called and whether it was used.
/// </summary>
public class ReceiveAddressListTests
{
    // Bech32 addresses: the last 6 characters are the checksum, which AddressKey.Normalize ignores.
    private static string Addr(int i) => $"dgb1qlist{i:D20}abcdef";

    private static List<(int Index, string Address)> NewestFirst(int newestIndex, int count) =>
        Enumerable.Range(0, count).Select(n => (newestIndex - n, Addr(newestIndex - n))).ToList();

    private static IReadOnlyDictionary<string, string> Labels(params (string Address, string Label)[] items) =>
        ActivityAddressIndex.BuildLabelLookup(items.Select(i => new KeyValuePair<string, string>(i.Address, i.Label)));

    // ── Windowing: the newest 10, then 10 more at a time ──

    [Theory]
    [InlineData(10, 25, 20)]
    [InlineData(20, 25, 25)]   // stops at the number of generated addresses
    [InlineData(25, 25, 25)]
    [InlineData(10, 4, 4)]     // fewer generated than the first window
    [InlineData(10, 10, 10)]
    public void NextWindow_AddsTenButNeverMoreThanGenerated(int current, int generated, int expected) =>
        Assert.Equal(expected, ReceiveAddressList.NextWindow(current, generated));

    [Theory]
    [InlineData(25, 10, 16, 10)]  // newest index 25 shows 25..16
    [InlineData(25, 26, 0, 26)]   // everything
    [InlineData(3, 10, 0, 4)]     // never below index 0
    [InlineData(0, 10, 0, 1)]     // a single address
    [InlineData(25, 40, 0, 26)]   // asking for more than exist
    public void WindowStart_NeverGoesBelowIndexZero(int currentIndex, int shownCount, int expectedStart, int expectedCount)
    {
        var (start, count) = ReceiveAddressList.WindowStart(currentIndex, shownCount);

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedCount, count);
    }

    // ── Build: entries ──

    [Fact]
    public void Build_KeepsTheNewestFirstOrderItIsGiven()
    {
        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history: null, Labels(), onShowAddress: null);

        Assert.Equal([5, 4, 3], entries.Select(e => e.Index));
        Assert.Equal([Addr(5), Addr(4), Addr(3)], entries.Select(e => e.Address));
    }

    [Fact]
    public void Build_TakesNamesFromTheLabelLookup_ByNormalizedAddress()
    {
        var labels = Labels((Addr(4), "Shop"));

        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history: null, labels, onShowAddress: null);

        Assert.Equal("Shop", entries.Single(e => e.Index == 4).Label);
        Assert.Null(entries.Single(e => e.Index == 5).Label);
    }

    [Fact]
    public void Build_MarksOnlyTheAddressOnShow_EvenForABech32ChecksumVariant()
    {
        const string variantOfAddr4 = "dgb1qlist00000000000000000004zzzzzz";

        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history: null, Labels(), onShowAddress: variantOfAddr4);

        Assert.Equal([4], entries.Where(e => e.IsOnShow).Select(e => e.Index));
    }

    [Fact]
    public void Build_NoAddressOnShow_MarksNothing()
    {
        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history: null, Labels(), onShowAddress: null);

        Assert.DoesNotContain(entries, e => e.IsOnShow);
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ReceiveAddressList.Build([], history: null, Labels(), onShowAddress: null));
    }

    [Fact]
    public void Build_NoHistory_GivesUnknownForEveryEntry_NeverUnused()
    {
        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history: null, Labels(), onShowAddress: null);

        Assert.All(entries, e => Assert.Equal(ReceiveAddressStatus.Unknown, e.Status));
    }

    // ── Status: used, unused or unknown, from the history the wallet already loads ──

    private static TransactionRecord Tx(string id, string[]? wallet = null, string[]? receivedTo = null) => new()
    {
        TxId = id,
        Direction = TransactionDirection.Received,
        AmountSatoshis = 100,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow,
        Confirmations = 1,
        WalletAddresses = (wallet ?? []).ToList(),
        WalletReceivedAddresses = (receivedTo ?? []).ToList(),
    };

    [Fact]
    public void Status_AddressInEitherList_IsUsed_WithTheRightCount_AndOthersAreUnused()
    {
        var history = new List<TransactionRecord>
        {
            Tx("a", wallet: [Addr(3)]),
            Tx("b", wallet: [Addr(3), Addr(4)]),
            Tx("c", wallet: [Addr(1)], receivedTo: [Addr(3)]),
        };

        var entries = ReceiveAddressList.Build(NewestFirst(5, 6), history, Labels(), onShowAddress: null);

        var three = entries.Single(e => e.Index == 3);
        Assert.Equal(ReceiveAddressStatus.Used, three.Status);
        Assert.Equal(3, three.TransactionCount);
        Assert.Equal(1, entries.Single(e => e.Index == 4).TransactionCount);
        Assert.Equal(ReceiveAddressStatus.Used, entries.Single(e => e.Index == 1).Status);
        Assert.Equal(ReceiveAddressStatus.Unused, entries.Single(e => e.Index == 5).Status);
        Assert.Equal(0, entries.Single(e => e.Index == 5).TransactionCount);
    }

    [Fact]
    public void StatusOf_GivesTheSameStatusAndCountAsTheMatchingEntry()
    {
        var history = new List<TransactionRecord> { Tx("a", wallet: [Addr(3)]), Tx("b", wallet: [Addr(3)]) };
        var entry = ReceiveAddressList.Build(NewestFirst(5, 6), history, Labels(), onShowAddress: null).Single(e => e.Index == 3);

        var (status, count) = ReceiveAddressList.StatusOf(Addr(3), history);

        Assert.Equal(entry.Status, status);
        Assert.Equal(entry.TransactionCount, count);
        Assert.Equal((ReceiveAddressStatus.Unused, 0), ReceiveAddressList.StatusOf(Addr(5), history));
    }

    [Fact]
    public void Status_ABech32ChecksumVariantInTheHistory_StillMarksTheAddressUsed_CountedOnce()
    {
        const string variantOfAddr3 = "dgb1qlist00000000000000000003zzzzzz";
        var history = new List<TransactionRecord> { Tx("a", wallet: [variantOfAddr3, Addr(3)]) };

        var (status, count) = ReceiveAddressList.StatusOf(Addr(3), history);

        Assert.Equal(ReceiveAddressStatus.Used, status);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Status_AnEmptyLoadedHistory_IsUnused_WhileNoHistoryIsUnknown()
    {
        Assert.Equal(ReceiveAddressStatus.Unused, ReceiveAddressList.StatusOf(Addr(1), new List<TransactionRecord>()).Status);
        Assert.Equal(ReceiveAddressStatus.Unknown, ReceiveAddressList.StatusOf(Addr(1), null).Status);
    }

    [Fact]
    public void Status_RecordsWithoutAnyWalletAddressData_GiveUnknown_NeverUnused()
    {
        // Records cached by an older version carry no wallet addresses: a missing address may just be missing data.
        var history = new List<TransactionRecord> { Tx("a"), Tx("b") };

        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history, Labels(), onShowAddress: null);

        Assert.All(entries, e => Assert.Equal(ReceiveAddressStatus.Unknown, e.Status));
    }

    [Fact]
    public void Status_OneRecordWithAddressData_MakesTheOthersUnused()
    {
        // Once any record carries wallet addresses the history is trustworthy for every address.
        var history = new List<TransactionRecord> { Tx("a"), Tx("b", wallet: [Addr(5)]) };

        var entries = ReceiveAddressList.Build(NewestFirst(5, 3), history, Labels(), onShowAddress: null);

        Assert.Equal(ReceiveAddressStatus.Used, entries.Single(e => e.Index == 5).Status);
        Assert.Equal(ReceiveAddressStatus.Unused, entries.Single(e => e.Index == 4).Status);
    }

    // ── The newest listed address is the one Receive shows today ──

    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        return service;
    }

    [Fact]
    public async Task GeneratedRange_EndsAtTheAddressReceiveShows_AndGainsOneEntryPerNewAddress()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        var current = service.ActiveWallet!.NextReceivingIndex;
        var before = await service.GetReceivingAddressesAsync(current + 1, 0);
        var shown = await service.GetReceivingAddressAsync();

        Assert.Equal(current + 1, before.Count);
        Assert.Equal(shown, before[^1].Address);

        var (newIndex, newAddress) = await service.GenerateNewReceivingAddressAsync();
        var after = await service.GetReceivingAddressesAsync(service.ActiveWallet!.NextReceivingIndex + 1, 0);

        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal(newIndex, after[^1].Index);
        Assert.Equal(newAddress, after[^1].Address);
        Assert.Equal(newAddress, await service.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task WatchOnlyWallet_ListsTheSameAddressesAsItsSeedWallet_AndCanGenerateMore()
    {
        // A watch-only (xpub) wallet is not a single-key wallet: Receive treats it as HD and shows the list, so it must derive
        // exactly the addresses its seed wallet would.
        var env = new Env();
        var xpub = env.Hd.GetAccountExtPubKey().ToString(Network);
        var service = env.NewService();
        await service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        var listed = await service.GetReceivingAddressesAsync(4, 0);

        Assert.Equal(Enumerable.Range(0, 4).Select(env.ReceiveAddress), listed.Select(a => a.Address));
        Assert.Equal(env.ReceiveAddress(0), await service.GetReceivingAddressAsync());

        var (index, address) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(env.ReceiveAddress(index), address);
        Assert.Equal(address, await service.GetReceivingAddressAsync());
    }

    [Fact]
    public async Task ListingAddresses_DoesNotMoveTheCurrentReceivingIndex()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var current = service.ActiveWallet!.NextReceivingIndex;

        _ = await service.GetReceivingAddressesAsync(15, 0);

        Assert.Equal(current, service.ActiveWallet!.NextReceivingIndex);
    }
}

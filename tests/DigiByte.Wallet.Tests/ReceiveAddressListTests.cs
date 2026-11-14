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
    public async Task ListingAddresses_DoesNotMoveTheCurrentReceivingIndex()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var current = service.ActiveWallet!.NextReceivingIndex;

        _ = await service.GetReceivingAddressesAsync(15, 0);

        Assert.Equal(current, service.ActiveWallet!.NextReceivingIndex);
    }
}

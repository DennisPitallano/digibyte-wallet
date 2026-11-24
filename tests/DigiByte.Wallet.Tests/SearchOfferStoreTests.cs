using DigiByte.Wallet.Services;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How a wallet came to be on this device, and whether its person has answered the "search further" offer (spec 027,
/// data-model.md section 4). Nothing here is secret, and a storage problem can hide the offer but never bring it back.
/// </summary>
public class SearchOfferStoreTests
{
    private const string W = "w1";

    [Theory]
    [InlineData(WalletOrigin.Created)]
    [InlineData(WalletOrigin.Restored)]
    [InlineData(WalletOrigin.WatchOnly)]
    public async Task Origin_RoundTrips(WalletOrigin origin)
    {
        var store = new SearchOfferStore(new InMemoryStorage());

        await store.SetOriginAsync(W, origin);

        Assert.Equal(origin, await store.GetOriginAsync(W));
    }

    [Fact]
    public async Task Origin_WithNoValue_IsUnknown() =>
        Assert.Equal(WalletOrigin.Unknown, await new SearchOfferStore(new InMemoryStorage()).GetOriginAsync(W));

    [Fact]
    public async Task Origin_WithAnUnknownValue_IsUnknown()
    {
        var storage = new InMemoryStorage();
        await storage.SetAsync(SearchOfferStore.OriginKey(W), "something-else");

        Assert.Equal(WalletOrigin.Unknown, await new SearchOfferStore(storage).GetOriginAsync(W));
    }

    [Fact]
    public async Task Origin_WhenStorageCannotBeRead_IsUnknown_AndNothingThrows()
    {
        var store = new SearchOfferStore(new ThrowingStorage());

        Assert.Equal(WalletOrigin.Unknown, await store.GetOriginAsync(W));
        await store.SetOriginAsync(W, WalletOrigin.Restored);
    }

    [Fact]
    public async Task Origin_SetTwice_KeepsTheFirstValue()
    {
        var store = new SearchOfferStore(new InMemoryStorage());

        await store.SetOriginAsync(W, WalletOrigin.Restored);
        await store.SetOriginAsync(W, WalletOrigin.Created);

        Assert.Equal(WalletOrigin.Restored, await store.GetOriginAsync(W));
    }

    [Fact]
    public async Task Origin_UnknownIsNeverWritten()
    {
        var storage = new InMemoryStorage();

        await new SearchOfferStore(storage).SetOriginAsync(W, WalletOrigin.Unknown);

        Assert.False(await storage.ContainsKeyAsync(SearchOfferStore.OriginKey(W)));
    }

    [Theory]
    [InlineData(SearchOfferAnswer.Dismissed)]
    [InlineData(SearchOfferAnswer.Searched)]
    public async Task Answer_RoundTrips(SearchOfferAnswer answer)
    {
        var store = new SearchOfferStore(new InMemoryStorage());

        await store.SetAnswerAsync(W, answer);

        Assert.Equal(answer, await store.GetAnswerAsync(W));
    }

    [Fact]
    public async Task Answer_WithNoValue_IsNone() =>
        Assert.Equal(SearchOfferAnswer.None, await new SearchOfferStore(new InMemoryStorage()).GetAnswerAsync(W));

    /// <summary>An unreadable answer must hide the offer, not show it again to someone who dismissed it.</summary>
    [Fact]
    public async Task Answer_WithAnUnknownValue_IsDismissed()
    {
        var storage = new InMemoryStorage();
        await storage.SetAsync(SearchOfferStore.AnswerKey(W), "something-else");

        Assert.Equal(SearchOfferAnswer.Dismissed, await new SearchOfferStore(storage).GetAnswerAsync(W));
    }

    [Fact]
    public async Task Answer_WhenStorageCannotBeRead_IsDismissed_AndNothingThrows()
    {
        var store = new SearchOfferStore(new ThrowingStorage());

        Assert.Equal(SearchOfferAnswer.Dismissed, await store.GetAnswerAsync(W));
        await store.SetAnswerAsync(W, SearchOfferAnswer.Searched);
    }

    [Fact]
    public async Task Answer_NeverGoesBackToNone()
    {
        var storage = new InMemoryStorage();
        var store = new SearchOfferStore(storage);
        await store.SetAnswerAsync(W, SearchOfferAnswer.Dismissed);

        await store.SetAnswerAsync(W, SearchOfferAnswer.None);

        Assert.Equal(SearchOfferAnswer.Dismissed, await store.GetAnswerAsync(W));
    }

    [Fact]
    public void Keys_ArePerWallet()
    {
        Assert.Equal("walletOrigin:w1", SearchOfferStore.OriginKey(W));
        Assert.Equal("searchOffer:w1", SearchOfferStore.AnswerKey(W));
    }

    [Fact]
    public async Task DeletingAWallet_RemovesItsKeys_AndLeavesAnotherWallets()
    {
        var env = new Env();
        var service = env.NewService();
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);
        var store = new SearchOfferStore(env.Storage);
        await store.SetOriginAsync(wallet.Id, WalletOrigin.Restored);
        await store.SetAnswerAsync(wallet.Id, SearchOfferAnswer.Dismissed);
        await store.SetOriginAsync("other", WalletOrigin.WatchOnly);
        await store.SetAnswerAsync("other", SearchOfferAnswer.Searched);

        await service.DeleteWalletAsync(wallet.Id);

        Assert.False(await env.Storage.ContainsKeyAsync(SearchOfferStore.OriginKey(wallet.Id)));
        Assert.False(await env.Storage.ContainsKeyAsync(SearchOfferStore.AnswerKey(wallet.Id)));
        Assert.Equal(WalletOrigin.WatchOnly, await store.GetOriginAsync("other"));
        Assert.Equal(SearchOfferAnswer.Searched, await store.GetAnswerAsync("other"));
    }
}

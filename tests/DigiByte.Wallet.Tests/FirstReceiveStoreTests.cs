using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>The per-wallet first-receive record and the per-device first-session marker (spec 025, FR-014, FR-028, FR-029, T005).</summary>
public class FirstReceiveStoreTests
{
    private readonly TestMemoryStorage _storage = new();
    private FirstReceiveStore Store => new(_storage);

    [Fact]
    public void StateKey_IsPerWallet()
    {
        Assert.Equal("firstReceive:abc", FirstReceiveStore.StateKey("abc"));
    }

    [Fact]
    public async Task NoKey_ReadsAsUndecided()
    {
        Assert.Equal(FirstReceiveState.Undecided, await Store.GetAsync("w1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EMPTY ")]
    [InlineData("undecided")]
    [InlineData("3")]
    [InlineData("{\"state\":\"empty\"}")]
    public async Task UnknownStoredValue_ReadsAsUndecided(string stored)
    {
        await _storage.SetAsync(FirstReceiveStore.StateKey("w1"), stored);
        Assert.Equal(FirstReceiveState.Undecided, await Store.GetAsync("w1"));
    }

    [Theory]
    [InlineData(FirstReceiveState.Empty, "empty")]
    [InlineData(FirstReceiveState.History, "history")]
    public async Task Set_RoundTrips_AsThePlainStoredValue(FirstReceiveState state, string stored)
    {
        await Store.SetAsync("w1", state);

        Assert.Equal(stored, await _storage.GetAsync(FirstReceiveStore.StateKey("w1")));
        Assert.Equal(state, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task Celebrated_IsStoredFromEmpty()
    {
        await Store.SetAsync("w1", FirstReceiveState.Empty);
        await Store.SetAsync("w1", FirstReceiveState.Celebrated);

        Assert.Equal("celebrated", await _storage.GetAsync(FirstReceiveStore.StateKey("w1")));
        Assert.Equal(FirstReceiveState.Celebrated, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task SetUndecided_WritesNothing()
    {
        await Store.SetAsync("w1", FirstReceiveState.Undecided);
        Assert.False(await _storage.ContainsKeyAsync(FirstReceiveStore.StateKey("w1")));

        await Store.SetAsync("w1", FirstReceiveState.Empty);
        await Store.SetAsync("w1", FirstReceiveState.Undecided);
        Assert.Equal(FirstReceiveState.Empty, await Store.GetAsync("w1"));
    }

    /// <summary>A wallet never moves back out of History or Celebrated: that is what stops a second or a false celebration.</summary>
    [Theory]
    [InlineData(FirstReceiveState.History, FirstReceiveState.Empty)]
    [InlineData(FirstReceiveState.History, FirstReceiveState.Celebrated)]
    [InlineData(FirstReceiveState.Celebrated, FirstReceiveState.Empty)]
    [InlineData(FirstReceiveState.Celebrated, FirstReceiveState.History)]
    public async Task SettledStates_AreNeverOverwritten(FirstReceiveState settled, FirstReceiveState attempt)
    {
        await _storage.SetAsync(FirstReceiveStore.StateKey("w1"), settled == FirstReceiveState.History ? "history" : "celebrated");

        await Store.SetAsync("w1", attempt);

        Assert.Equal(settled, await Store.GetAsync("w1"));
    }

    /// <summary>Only a wallet that started empty can be celebrated.</summary>
    [Fact]
    public async Task Celebrated_IsNotAcceptedFromUndecided()
    {
        await Store.SetAsync("w1", FirstReceiveState.Celebrated);
        Assert.Equal(FirstReceiveState.Undecided, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task Empty_CanBecomeHistory()
    {
        await Store.SetAsync("w1", FirstReceiveState.Empty);
        await Store.SetAsync("w1", FirstReceiveState.History);
        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task States_ArePerWallet()
    {
        await Store.SetAsync("w1", FirstReceiveState.Empty);
        await Store.SetAsync("w2", FirstReceiveState.History);

        Assert.Equal(FirstReceiveState.Empty, await Store.GetAsync("w1"));
        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w2"));
        Assert.Equal(FirstReceiveState.Undecided, await Store.GetAsync("w3"));
    }

    [Fact]
    public async Task FirstSession_IsNotEnded_UntilEnded_AndEndingTwiceIsFine()
    {
        Assert.False(await Store.IsFirstSessionEndedAsync());

        await Store.EndFirstSessionAsync();
        await Store.EndFirstSessionAsync();

        Assert.True(await Store.IsFirstSessionEndedAsync());
        Assert.Equal("1", await _storage.GetAsync("firstSession:ended"));
    }

    [Fact]
    public async Task Delete_RemovesTheWalletsRecord_AndLeavesTheMarker()
    {
        await Store.SetAsync("w1", FirstReceiveState.Empty);
        await Store.SetAsync("w2", FirstReceiveState.Empty);
        await Store.EndFirstSessionAsync();

        await Store.DeleteAsync("w1");

        Assert.False(await _storage.ContainsKeyAsync(FirstReceiveStore.StateKey("w1")));
        Assert.Equal(FirstReceiveState.Empty, await Store.GetAsync("w2"));
        Assert.True(await Store.IsFirstSessionEndedAsync());
    }

    /// <summary>A storage problem only ever delays a celebration or keeps optional items hidden; it never throws to a page.</summary>
    [Fact]
    public async Task StorageFailures_ReadAsUndecidedAndNotEnded_AndNeverThrow()
    {
        var store = new FirstReceiveStore(new ChangeAddressTests.ThrowingStorage());

        Assert.Equal(FirstReceiveState.Undecided, await store.GetAsync("w1"));
        Assert.False(await store.IsFirstSessionEndedAsync());
        await store.SetAsync("w1", FirstReceiveState.Empty);
        await store.EndFirstSessionAsync();
        await store.DeleteAsync("w1");
    }
}

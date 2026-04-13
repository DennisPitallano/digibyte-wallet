using DigiByte.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

public class AppStateTests
{
    private static AppState CreateAppState(IJSRuntime? js = null)
    {
        var mockJs = js ?? new FakeJSRuntime();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DefaultNetwork"] = "mainnet" })
            .Build();
        return new AppState(mockJs, config);
    }

    [Fact]
    public void OnTabHidden_WhenUnlockedAndLockOnHiddenTrue_LocksWallet()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockOnHidden = true;

        var autoLocked = false;
        sut.OnAutoLocked += () => autoLocked = true;

        sut.OnTabHidden();

        Assert.False(sut.IsWalletUnlocked);
        Assert.True(autoLocked);
    }

    [Fact]
    public void OnTabHidden_WhenUnlockedAndLockOnHiddenFalse_DoesNotLock()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockOnHidden = false;

        sut.OnTabHidden();

        Assert.True(sut.IsWalletUnlocked);
    }

    [Fact]
    public void OnTabHidden_WhenAlreadyLocked_DoesNothing()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = false;
        sut.LockOnHidden = true;

        var autoLocked = false;
        sut.OnAutoLocked += () => autoLocked = true;

        sut.OnTabHidden();

        Assert.False(sut.IsWalletUnlocked);
        Assert.False(autoLocked);
    }

    [Fact]
    public void OnTabHidden_FiresOnChange()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockOnHidden = true;

        var stateChanged = false;
        sut.OnChange += () => stateChanged = true;

        sut.OnTabHidden();

        Assert.True(stateChanged);
    }

    [Fact]
    public void ResetLockTimer_WhenLocked_DoesNothing()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = false;

        sut.ResetLockTimer();
    }

    [Fact]
    public async Task AutoLockTimer_FiresAfterTimeout()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockTimeout = TimeSpan.FromMilliseconds(50);

        var tcs = new TaskCompletionSource();
        sut.OnAutoLocked += () => tcs.TrySetResult();

        sut.ResetLockTimer();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Equal(tcs.Task, completed);
        Assert.False(sut.IsWalletUnlocked);
    }

    [Fact]
    public async Task ResetLockTimer_ResetsCountdown()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockTimeout = TimeSpan.FromMilliseconds(200);

        var lockCount = 0;
        sut.OnAutoLocked += () => lockCount++;

        sut.ResetLockTimer();
        await Task.Delay(100);

        sut.IsWalletUnlocked = true;
        sut.ResetLockTimer();
        await Task.Delay(100);

        Assert.Equal(0, lockCount);

        await Task.Delay(150);
        Assert.Equal(1, lockCount);
    }

    [Fact]
    public void StopLockTimer_PreventsAutoLock()
    {
        var sut = CreateAppState();
        sut.IsWalletUnlocked = true;
        sut.LockTimeout = TimeSpan.FromMilliseconds(50);

        var autoLocked = false;
        sut.OnAutoLocked += () => autoLocked = true;

        sut.ResetLockTimer();
        sut.StopLockTimer();

        Thread.Sleep(100);

        Assert.True(sut.IsWalletUnlocked);
        Assert.False(autoLocked);
    }

    [Fact]
    public void DefaultLockTimeout_IsFiveMinutes()
    {
        var sut = CreateAppState();
        Assert.Equal(TimeSpan.FromMinutes(5), sut.LockTimeout);
    }

    [Fact]
    public void DefaultLockOnHidden_IsFalse()
    {
        var sut = CreateAppState();
        Assert.False(sut.LockOnHidden);
    }

    [Fact]
    public void NetworkMode_UsesConfigDefault()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DefaultNetwork"] = "testnet" })
            .Build();
        var sut = new AppState(new FakeJSRuntime(), config);

        Assert.Equal("testnet", sut.NetworkMode);
        Assert.True(sut.IsTestnet);
    }

    [Fact]
    public void FiatSymbol_MapsCorrectly()
    {
        var sut = CreateAppState();

        sut.FiatCurrency = "USD";
        Assert.Equal("$", sut.FiatSymbol);

        sut.FiatCurrency = "EUR";
        Assert.Equal("\u20ac", sut.FiatSymbol);

        sut.FiatCurrency = "GBP";
        Assert.Equal("\u00a3", sut.FiatSymbol);

        sut.FiatCurrency = "PHP";
        Assert.Equal("\u20b1", sut.FiatSymbol);

        sut.FiatCurrency = "JPY";
        Assert.Equal("\u00a5", sut.FiatSymbol);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var sut = CreateAppState();
        sut.ResetLockTimer();
        sut.Dispose();
    }

    private class FakeJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            default;
    }
}

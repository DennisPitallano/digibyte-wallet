using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class UpdateAdviceTests
{
    [Theory]
    [InlineData("/unlock")]
    [InlineData("/welcome")]
    [InlineData("/Unlock/")]
    public void LockedOnAnIdleScreen_AppliesWithoutAsking(string path) =>
        Assert.True(UpdateAdvice.ApplyWithoutAsking(path, walletUnlocked: false));

    [Theory]
    [InlineData("/")]
    [InlineData("/send")]
    [InlineData("/settings")]
    [InlineData("/unlock")]
    public void UnlockedWallet_IsAlwaysAsked(string path) =>
        Assert.False(UpdateAdvice.ApplyWithoutAsking(path, walletUnlocked: true));

    [Theory]
    [InlineData("/create")]
    [InlineData("/recover")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    public void LockedElsewhere_IsAsked(string? path) =>
        Assert.False(UpdateAdvice.ApplyWithoutAsking(path, walletUnlocked: false));

    [Fact]
    public void OnlyAReadyUpdate_SaysItIsReloading()
    {
        Assert.Contains("Reloading", UpdateAdvice.Message("ready"));
        foreach (var status in new[] { "none", "downloading", "failed", null, "something-else" })
            Assert.DoesNotContain("Reloading", UpdateAdvice.Message(status));
    }

    [Fact]
    public void EachStatus_HasItsOwnMessage()
    {
        var messages = new[] { "none", "ready", "downloading", "failed" }.Select(UpdateAdvice.Message).ToList();
        Assert.Equal(4, messages.Distinct().Count());
        Assert.Equal(UpdateAdvice.Message("none"), UpdateAdvice.Message(null));
    }
}

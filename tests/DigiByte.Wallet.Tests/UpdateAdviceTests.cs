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

    [Theory]
    [InlineData("ready", UpdateCheckResult.Ready)]
    [InlineData("downloading", UpdateCheckResult.Downloading)]
    [InlineData("failed", UpdateCheckResult.Failed)]
    [InlineData("none", UpdateCheckResult.UpToDate)]
    [InlineData(null, UpdateCheckResult.UpToDate)]
    [InlineData("something-else", UpdateCheckResult.UpToDate)]
    public void EachAnswerFromThePageScript_HasItsResult(string? status, UpdateCheckResult expected) =>
        Assert.Equal(expected, UpdateAdvice.ResultOf(status));

    [Fact]
    public void OnlyAReadyUpdate_SaysItIsReloading()
    {
        var en = LocaleReasonKeysTests.English;
        Assert.Contains("Reloading", en[LocaleReasonKeys.For(UpdateCheckResult.Ready)]);
        foreach (var status in new[] { "none", "downloading", "failed", null, "something-else" })
            Assert.DoesNotContain("Reloading", en[LocaleReasonKeys.For(UpdateAdvice.ResultOf(status))]);
    }

    [Fact]
    public void EachResult_HasItsOwnMessage()
    {
        var keys = Enum.GetValues<UpdateCheckResult>().Select(LocaleReasonKeys.For).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}

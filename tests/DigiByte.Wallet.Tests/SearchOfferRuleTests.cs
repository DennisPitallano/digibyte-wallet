using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// When Home offers the wider search: for a wallet that was brought onto the device (not created here), has further addresses
/// to search, and whose completed load found nothing, until the person answers (spec 027, FR-001 to FR-006).
/// </summary>
public class SearchOfferRuleTests
{
    [Theory]
    [InlineData("hd", WalletOrigin.Restored)]
    [InlineData("xpub", WalletOrigin.WatchOnly)]
    // Wallets from before this feature have no recorded origin: an empty one may have been restored (FR-002b).
    [InlineData("hd", WalletOrigin.Unknown)]
    [InlineData("xpub", WalletOrigin.Unknown)]
    public void ShownForAnEmptyWalletThatWasNotCreatedHere(string walletType, WalletOrigin origin) =>
        Assert.True(SearchOfferRule.ShouldShow(walletType, origin, FirstReceiveState.Empty, SearchOfferAnswer.None));

    [Theory]
    [InlineData("hd")]
    [InlineData("xpub")]
    public void NeverShownForAWalletCreatedHere(string walletType) =>
        Assert.False(SearchOfferRule.ShouldShow(walletType, WalletOrigin.Created, FirstReceiveState.Empty, SearchOfferAnswer.None));

    [Theory]
    [InlineData(FirstReceiveState.Undecided)]   // no completed load yet
    [InlineData(FirstReceiveState.History)]     // the wallet has payments
    [InlineData(FirstReceiveState.Celebrated)]
    public void NotShownUnlessACompletedLoadFoundNothing(FirstReceiveState state) =>
        Assert.False(SearchOfferRule.ShouldShow("hd", WalletOrigin.Restored, state, SearchOfferAnswer.None));

    [Theory]
    [InlineData(SearchOfferAnswer.Dismissed)]
    [InlineData(SearchOfferAnswer.Searched)]
    public void NotShownOnceAnswered(SearchOfferAnswer answer) =>
        Assert.False(SearchOfferRule.ShouldShow("hd", WalletOrigin.Restored, FirstReceiveState.Empty, answer));

    [Theory]
    [InlineData("wif")]
    [InlineData("multisig")]
    [InlineData("")]
    [InlineData(null)]
    public void NotShownForAWalletWithNoFurtherAddresses(string? walletType) =>
        Assert.False(SearchOfferRule.ShouldShow(walletType, WalletOrigin.Restored, FirstReceiveState.Empty, SearchOfferAnswer.None));
}

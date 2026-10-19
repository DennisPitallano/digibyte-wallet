using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

public class SendRiskEvaluatorTests
{
    private const string Known = "DKnownAddress111111111111111111111";
    private const string Unknown = "DUnknownAddress1111111111111111111";

    private static IReadOnlySet<string> KnownSet(params string[] addresses) =>
        KnownAddressProvider.Build(addresses, []);

    // ── LargeAmount: 50% of balance, measured on the amount sent (fee excluded) ──

    [Theory]
    [InlineData(49.99, 100, false)]
    [InlineData(50, 100, true)]
    [InlineData(50.01, 100, true)]
    [InlineData(100, 100, true)]   // send-max counts as 100%
    [InlineData(1, 100, false)]
    [InlineData(0, 100, false)]
    public void LargeAmount_FollowsFiftyPercentRule(double amount, double balance, bool expected)
    {
        var result = SendRiskEvaluator.Evaluate((decimal)amount, (decimal)balance, Known, KnownSet(Known));
        Assert.Equal(expected, result.LargeAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void LargeAmount_IsFalse_WhenBalanceIsZeroOrNegative_AndNeverDividesByZero(double balance)
    {
        var result = SendRiskEvaluator.Evaluate(10m, (decimal)balance, Known, KnownSet(Known));
        Assert.False(result.LargeAmount);
    }

    [Fact]
    public void LargeAmount_IsFalse_ForNegativeAmount()
    {
        var result = SendRiskEvaluator.Evaluate(-60m, 100m, Known, KnownSet(Known));
        Assert.False(result.LargeAmount);
    }

    [Fact]
    public void LargeAmount_Works_WithDigiDollarCents()
    {
        // DigiDollar callers pass dollars (cents / 100) as the amount and balance: same rule, same result.
        Assert.True(SendRiskEvaluator.Evaluate(25.00m, 50.00m, Known, KnownSet(Known)).LargeAmount);
        Assert.False(SendRiskEvaluator.Evaluate(24.99m, 50.00m, Known, KnownSet(Known)).LargeAmount);
    }

    // ── NewAddress: neither a saved contact nor previously sent to ──

    [Fact]
    public void NewAddress_IsTrue_ForUnknownDestination()
    {
        var result = SendRiskEvaluator.Evaluate(1m, 100m, Unknown, KnownSet(Known));
        Assert.True(result.NewAddress);
    }

    [Fact]
    public void NewAddress_IsFalse_ForSavedContact()
    {
        var known = KnownAddressProvider.Build(contactAddresses: [Known], sentAddresses: []);
        Assert.False(SendRiskEvaluator.Evaluate(1m, 100m, Known, known).NewAddress);
    }

    [Fact]
    public void NewAddress_IsFalse_ForPreviouslySentAddress()
    {
        var known = KnownAddressProvider.Build(contactAddresses: [], sentAddresses: [Known]);
        Assert.False(SendRiskEvaluator.Evaluate(1m, 100m, Known, known).NewAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NewAddress_IsFalse_WhenThereIsNoDestinationToCheck(string destination)
    {
        // Address validation is the caller's job and runs first; an empty destination is not a "new address" warning.
        var result = SendRiskEvaluator.Evaluate(1m, 100m, destination, KnownSet(Known));
        Assert.False(result.NewAddress);
    }

    [Fact]
    public void NewAddress_IgnoresSurroundingWhitespace()
    {
        var result = SendRiskEvaluator.Evaluate(1m, 100m, "  " + Known + "  ", KnownSet(Known));
        Assert.False(result.NewAddress);
    }

    [Fact]
    public void NewAddress_Bech32_IsCaseInsensitive()
    {
        var known = KnownAddressProvider.Build(["dgb1qexampleaddress0000000000000000000"], []);
        Assert.False(SendRiskEvaluator.Evaluate(1m, 100m, "DGB1QEXAMPLEADDRESS0000000000000000000", known).NewAddress);
    }

    [Fact]
    public void NewAddress_Base58_IsCaseSensitive()
    {
        var known = KnownAddressProvider.Build([Known], []);
        Assert.True(SendRiskEvaluator.Evaluate(1m, 100m, Known.ToLowerInvariant(), known).NewAddress);
    }

    [Fact]
    public void NewAddress_IsTrue_WhenKnownSetIsEmpty()
    {
        var result = SendRiskEvaluator.Evaluate(1m, 100m, Unknown, KnownAddressProvider.Build([], []));
        Assert.True(result.NewAddress);
    }

    // ── Combined ──

    [Fact]
    public void Both_AreReported_Together()
    {
        var result = SendRiskEvaluator.Evaluate(60m, 100m, Unknown, KnownSet(Known));
        Assert.True(result.LargeAmount);
        Assert.True(result.NewAddress);
        Assert.True(result.AnyRisk);
    }

    [Fact]
    public void NoRisk_WhenSmallAndKnown()
    {
        var result = SendRiskEvaluator.Evaluate(10m, 100m, Known, KnownSet(Known));
        Assert.False(result.AnyRisk);
    }

    // ── Batch sends ──

    [Fact]
    public void Batch_IsNew_WhenAnyRecipientIsUnknown()
    {
        var result = SendRiskEvaluator.EvaluateBatch([Known, Unknown], 10m, 100m, KnownSet(Known));
        Assert.True(result.NewAddress);
        Assert.False(result.LargeAmount);
    }

    [Fact]
    public void Batch_UsesTheTotal_ForTheLargeAmountRule()
    {
        var result = SendRiskEvaluator.EvaluateBatch([Known, Known], 55m, 100m, KnownSet(Known));
        Assert.True(result.LargeAmount);
        Assert.False(result.NewAddress);
    }
}

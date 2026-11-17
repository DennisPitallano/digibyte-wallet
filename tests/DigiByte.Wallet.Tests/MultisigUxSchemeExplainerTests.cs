using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Plain-language M-of-N explanation (spec 018, FR-009).</summary>
public class MultisigUxSchemeExplainerTests
{
    [Fact]
    public void Two_of_three() =>
        Assert.Equal("Any 2 of 3 co-signers must approve each payment.", SchemeExplainer.Describe(2, 3));

    [Fact]
    public void All_must_approve() =>
        Assert.Equal("All 3 co-signers must approve each payment.", SchemeExplainer.Describe(3, 3));

    [Fact]
    public void Any_one_can_approve() =>
        Assert.Equal("Any 1 of 3 co-signers can approve a payment on their own.", SchemeExplainer.Describe(1, 3));

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 3)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void Invalid_input_gives_an_empty_string(int required, int total) =>
        Assert.Equal("", SchemeExplainer.Describe(required, total));
}

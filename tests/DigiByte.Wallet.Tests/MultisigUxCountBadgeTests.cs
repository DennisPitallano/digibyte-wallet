using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The text of a count bubble on the menu and the cards (spec 026, FR-004 and FR-010a).</summary>
public class MultisigUxCountBadgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void Nothing_to_show_is_an_empty_string(int count)
    {
        Assert.Equal("", CountBadge.Text(count));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(7, "7")]
    [InlineData(42, "42")]
    [InlineData(99, "99")]
    public void Up_to_ninety_nine_shows_the_number(int count, string expected)
    {
        Assert.Equal(expected, CountBadge.Text(count));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(5000)]
    public void Above_ninety_nine_is_capped(int count)
    {
        Assert.Equal("99+", CountBadge.Text(count));
    }
}

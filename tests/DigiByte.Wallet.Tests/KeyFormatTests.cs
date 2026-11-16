using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class KeyFormatTests
{
    [Fact]
    public void Group_SplitsACompressedKeyIntoReadableBlocks_ThatRejoinExactly()
    {
        var hex = new Key().PubKey.ToHex();
        Assert.Equal(66, hex.Length);

        var grouped = MultisigKeyFormat.Group(hex);

        Assert.Contains(' ', grouped);
        Assert.Equal(hex, grouped.Replace(" ", ""));
        Assert.All(grouped.Split(' '), block => Assert.InRange(block.Length, 1, 8));
    }

    [Fact]
    public void Group_NormalisesCaseAndWhitespace()
    {
        var hex = new Key().PubKey.ToHex();
        var messy = "  " + hex.ToUpperInvariant().Insert(10, " ").Insert(30, "\n") + " ";

        Assert.Equal(MultisigKeyFormat.Group(hex), MultisigKeyFormat.Group(messy));
        Assert.Equal(hex, MultisigKeyFormat.Normalize(messy));
    }

    [Theory]
    [InlineData("not hex at all")]
    [InlineData("")]
    [InlineData("02ab")]
    public void Group_ReturnsInvalidInputUnchanged(string input)
    {
        Assert.Equal(input, MultisigKeyFormat.Group(input));
    }
}

using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The Request tab's keypad (spec 036, US4, FR-022): what each key does to the typed amount, how many decimals each
/// currency takes, and how the typed text is read. Modelled on DigiPay's till keypad.
/// </summary>
public class RequestKeypadEntryTests
{
    private static string Type(string keys, int decimals = 8) =>
        keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).Aggregate("", (amount, key) => RequestKeypadEntry.Press(amount, key, decimals));

    [Fact]
    public void Digits_AndOnePoint_Build_TheAmount()
    {
        Assert.Equal("12.5", Type("1 2 . 5"));
        Assert.Equal("12.5", Type("1 2 . . 5"));
    }

    [Fact]
    public void APointFirst_Reads_ZeroPoint()
    {
        Assert.Equal("0.", Type("."));
        Assert.Equal("0.5", Type(". 5"));
    }

    [Fact]
    public void LeadingZeros_Collapse()
    {
        Assert.Equal("7", Type("0 0 7"));
        Assert.Equal("0", Type("0 0"));
        Assert.Equal("0.07", Type("0 . 0 7"));
    }

    [Fact]
    public void NoMoreDecimals_ThanTheCurrencyAllows()
    {
        Assert.Equal("1.12345678", Type("1 . 1 2 3 4 5 6 7 8 9", 8));
        Assert.Equal("1.25", Type("1 . 2 5 9", 2));
        Assert.Equal("100", Type("1 0 0 .", 0));
    }

    [Fact]
    public void DecimalsFor_IsEightForDgb_AndTheCurrencysOwnOtherwise()
    {
        Assert.Equal(8, RequestKeypadEntry.DecimalsFor(RequestKeypadEntry.Dgb));
        Assert.Equal(2, RequestKeypadEntry.DecimalsFor("USD"));
        Assert.Equal(0, RequestKeypadEntry.DecimalsFor("JPY"));
    }

    [Fact]
    public void Backspace_AndClear()
    {
        Assert.Equal("12.", Type("1 2 . 5 back"));
        Assert.Equal("", Type("back"));
        Assert.Equal("", Type("1 2 . 5 clear"));
    }

    [Fact]
    public void TooLong_IsIgnored()
    {
        var typed = Type(string.Join(' ', Enumerable.Repeat("9", 30)));

        Assert.Equal(RequestKeypadEntry.MaxLength, typed.Length);
    }

    [Fact]
    public void UnknownKeys_DoNothing()
    {
        Assert.Equal("12", Type("1 2 x - ,"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.")]
    [InlineData("0.00")]
    public void Empty_OrZero_MeansAnyAmount(string typed)
    {
        Assert.Null(RequestKeypadEntry.Parse(typed));
    }

    [Fact]
    public void Parse_UsesThePoint_WhateverTheLanguage()
    {
        using var _ = new CultureScope("es");

        Assert.Equal(12.5m, RequestKeypadEntry.Parse("12.5"));
        Assert.Equal(1234.5m, RequestKeypadEntry.Parse("1234.5"));
    }

    [Fact]
    public void AnAmountThatDoesNotFit_IsRejected()
    {
        Assert.False(RequestKeypadEntry.Fits("1.123", 2));
        Assert.False(RequestKeypadEntry.Fits("1.5", 0));
        Assert.True(RequestKeypadEntry.Fits("1.12", 2));
        Assert.True(RequestKeypadEntry.Fits("", 8));
        Assert.False(RequestKeypadEntry.Fits("21000000000", 8));     // more DGB than can ever exist
        Assert.Null(RequestKeypadEntry.Parse("21000000000"));
    }
}

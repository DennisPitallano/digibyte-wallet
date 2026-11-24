using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Typed amounts (spec 028, FR-013 / FR-013a, contracts/amount-entry.md). The same keystrokes must give the same
/// result in every language, a comma or a point may be the decimal mark, and "1.234" must never be guessed.
/// </summary>
public class AmountEntryTests
{
    private const int Decimals = 8;
    private const decimal Max = 21_000_000_000m;

    private static readonly string[] Cultures = ["en-US", "es"];

    private static AmountEntryResult ParseEverywhere(string? text, int decimals = Decimals, decimal max = Max)
    {
        // Parse under each culture and require one answer: the parser must not depend on the display language.
        AmountEntryResult? first = null;
        foreach (var culture in Cultures)
        {
            using var _ = new CultureScope(culture);
            var result = AmountEntry.Parse(text, decimals, max);
            if (first is null) first = result;
            else Assert.Equal(first, result);
        }
        return first!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NothingTyped_IsEmpty(string? text) =>
        Assert.Equal(AmountEntryKind.Empty, ParseEverywhere(text).Kind);

    [Theory]
    [InlineData("12", "12")]
    [InlineData("12,5", "12.5")]
    [InlineData("12.5", "12.5")]
    [InlineData("0.00012345", "0.00012345")]
    [InlineData("0,123", "0.123")]      // leading 0: can't be a thousands mark
    [InlineData("0.123", "0.123")]
    [InlineData("1234.567", "1234.567")] // four digits before the mark: can't be a thousands mark
    [InlineData("1 234,5", "1234.5")]    // a space as thousands mark keeps its meaning
    [InlineData(",5", "0.5")]
    [InlineData("5.", "5")]
    [InlineData("007", "7")]
    [InlineData("  12,5  ", "12.5")]
    public void ClearInput_GivesOneValue(string text, string expected)
    {
        var result = ParseEverywhere(text);

        Assert.Equal(AmountEntryKind.Value, result.Kind);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result.Amount);
    }

    [Fact]
    public void NoBreakSpaces_AreRemovedLikeSpaces()
    {
        Assert.Equal(1234.5m, ParseEverywhere("1" + (char)0xA0 + "234,5").Amount);   // no-break space
        Assert.Equal(1234.5m, ParseEverywhere("1" + (char)0x202F + "234,5").Amount); // narrow no-break space
    }

    [Theory]
    [InlineData("1.234", "1.234", "1234")]
    [InlineData("1,234", "1.234", "1234")]
    [InlineData("999,999", "999.999", "999999")]
    [InlineData("12.500", "12.5", "12500")]
    public void OneMarkWithThreeDigitsAfter_AsksWhichWasMeant(string text, string asDecimal, string asThousands)
    {
        var result = ParseEverywhere(text);

        Assert.Equal(AmountEntryKind.Ambiguous, result.Kind);
        Assert.Equal(decimal.Parse(asDecimal, System.Globalization.CultureInfo.InvariantCulture), result.AsDecimal);
        Assert.Equal(decimal.Parse(asThousands, System.Globalization.CultureInfo.InvariantCulture), result.AsThousands);
        Assert.Null(result.Amount);
    }

    [Fact]
    public void Ambiguous_WithOnlyOneValidReading_GivesThatReading()
    {
        // With two decimals allowed, 1.234 can't be meant, so "1.234" can only be 1234.
        var result = ParseEverywhere("1.234", decimals: 2);
        Assert.Equal(AmountEntryKind.Value, result.Kind);
        Assert.Equal(1234m, result.Amount);

        // With a cap of 100, 1234 can't be meant, so "1,234" can only be 1.234.
        var capped = ParseEverywhere("1,234", max: 100m);
        Assert.Equal(AmountEntryKind.Value, capped.Kind);
        Assert.Equal(1.234m, capped.Amount);
    }

    [Theory]
    [InlineData("1.234,5", AmountEntryError.MoreThanOneMark)]
    [InlineData("1,234.5", AmountEntryError.MoreThanOneMark)]
    [InlineData("1.234.567", AmountEntryError.MoreThanOneMark)]
    [InlineData("1,,5", AmountEntryError.MoreThanOneMark)]
    [InlineData("12a", AmountEntryError.NotANumber)]
    [InlineData("-5", AmountEntryError.NotANumber)]
    [InlineData("1e3", AmountEntryError.NotANumber)]
    [InlineData("$5", AmountEntryError.NotANumber)]
    [InlineData(".", AmountEntryError.NotANumber)]
    [InlineData("0.000000001", AmountEntryError.TooManyDecimals)]
    [InlineData("0", AmountEntryError.NotPositive)]
    [InlineData("0,0", AmountEntryError.NotPositive)]
    [InlineData("21000000001", AmountEntryError.TooLarge)]
    [InlineData("99999999999999999999999999999999999999", AmountEntryError.TooLarge)]
    public void BadInput_IsInvalidWithAReason(string text, AmountEntryError error)
    {
        var result = ParseEverywhere(text);

        Assert.Equal(AmountEntryKind.Invalid, result.Kind);
        Assert.Equal(error, result.Error);
        Assert.Null(result.Amount);
    }

    [Fact]
    public void Ambiguous_WithNoValidReading_IsInvalid()
    {
        var result = ParseEverywhere("999,999", max: 100m);

        Assert.Equal(AmountEntryKind.Invalid, result.Kind);
        Assert.Equal(AmountEntryError.TooLarge, result.Error);
    }

    [Fact]
    public void TheMaximumItself_IsAllowed() =>
        Assert.Equal(Max, ParseEverywhere("21000000000").Amount);
}

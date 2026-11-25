using DigiByte.Wallet.Services.Export;

namespace DigiByte.Wallet.Tests.Export;

/// <summary>Cell rules of the exported CSV (spec 031, contracts/csv-file-format.md, research R4).</summary>
public class CsvFormatTests
{
    // ── Separator and decimal mark follow the device ──

    [Fact]
    public void DotDecimalDevice_UsesCommaSeparatorAndDotDecimal()
    {
        var f = CsvFormat.ForDecimalMark(".");
        Assert.Equal(',', f.Separator);
        Assert.Equal('.', f.DecimalMark);
        Assert.Equal("-12.50000000", f.Number(-12.5m, 8));
    }

    [Fact]
    public void CommaDecimalDevice_UsesSemicolonSeparatorAndCommaDecimal()
    {
        var f = CsvFormat.ForDecimalMark(",");
        Assert.Equal(';', f.Separator);
        Assert.Equal(',', f.DecimalMark);
        Assert.Equal("-12,50000000", f.Number(-12.5m, 8));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    public void UnknownDecimalMark_FallsBackToComma(string? mark) =>
        Assert.Equal(',', CsvFormat.ForDecimalMark(mark).Separator);

    // ── Quoting (FR-008) ──

    [Theory]
    [InlineData("Invoice 12, part 1", "\"Invoice 12, part 1\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("cr\rhere", "\"cr\rhere\"")]
    [InlineData("plain", "plain")]
    public void Text_QuotesOnlyWhenNeeded(string input, string expected) =>
        Assert.Equal(expected, CsvFormat.ForDecimalMark(".").Text(input));

    [Fact]
    public void Text_SemicolonStyle_QuotesSemicolonsNotCommas()
    {
        var f = CsvFormat.ForDecimalMark(",");
        Assert.Equal("\"a;b\"", f.Text("a;b"));
        Assert.Equal("a,b", f.Text("a,b"));
    }

    // ── Formula neutralising (FR-009, SC-006) ──

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"\"x\"\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(1)", "'@SUM(1)")]
    [InlineData("\tTAB", "'\tTAB")]
    public void Text_StartingLikeAFormula_GetsALeadingApostrophe(string input, string expectedInner)
    {
        var cell = CsvFormat.ForDecimalMark(".").Text(input);
        // Quoting may wrap the neutralised value; the first character of the value is always the apostrophe.
        var inner = cell.StartsWith('"') ? cell[1..^1] : cell;
        Assert.Equal(expectedInner, inner);
    }

    [Fact]
    public void Text_StartingWithCarriageReturn_IsNeutralisedAndQuoted() =>
        Assert.Equal("\"'\rx\"", CsvFormat.ForDecimalMark(".").Text("\rx"));

    [Fact]
    public void Number_Negative_IsNotNeutralised() =>
        Assert.Equal("-1.50", CsvFormat.ForDecimalMark(".").Number(-1.5m, 2));

    // ── Empty values ──

    [Fact]
    public void EmptyValues_AreEmptyFields()
    {
        var f = CsvFormat.ForDecimalMark(".");
        Assert.Equal("", f.Text(null));
        Assert.Equal("", f.Text(""));
        Assert.Equal("", f.Number(null, 8));
        Assert.Equal("", f.Integer(null));
    }

    // ── Numbers: fixed decimals, no thousands separator ──

    [Theory]
    [InlineData(1234567.123456789, 8, "1234567.12345679")]
    [InlineData(250, 8, "250.00000000")]
    [InlineData(1.425, 2, "1.43")]
    [InlineData(0, 2, "0.00")]
    public void Number_HasFixedDecimalsAndNoThousandsSeparator(double value, int decimals, string expected) =>
        Assert.Equal(expected, CsvFormat.ForDecimalMark(".").Number((decimal)value, decimals));

    [Fact]
    public void Number_UsesTheCurrencyDecimals()
    {
        var f = CsvFormat.ForDecimalMark(".");
        Assert.Equal("1250", f.Number(1250m, Models.FiatCurrencies.DecimalsFor("JPY")));
        Assert.Equal("2.80", f.Number(2.8m, Models.FiatCurrencies.DecimalsFor("USD")));
    }
}

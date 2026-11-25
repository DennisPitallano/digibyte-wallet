using System.Globalization;
using System.Text;

namespace DigiByte.Wallet.Services.Export;

/// <summary>Which separator and decimal mark the file uses (spec 031, research R4).</summary>
public enum CsvStyle
{
    /// <summary><c>,</c> between cells and <c>.</c> in numbers: for devices whose decimal mark is a dot.</summary>
    Comma,

    /// <summary><c>;</c> between cells and <c>,</c> in numbers: for devices whose decimal mark is a comma (es-ES, de, fr, pt-BR…).</summary>
    Semicolon,
}

/// <summary>
/// The cell rules of the exported CSV (contracts/csv-file-format.md): quoting, formula neutralising, fixed-decimal numbers and
/// empty fields. Excel opens the file in columns without an import step only when the separator and decimal mark match the
/// device's region, so both follow the device's decimal mark.
/// </summary>
public sealed class CsvFormat
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    private CsvFormat(CsvStyle style)
    {
        Style = style;
        Separator = style == CsvStyle.Semicolon ? ';' : ',';
        DecimalMark = style == CsvStyle.Semicolon ? ',' : '.';
    }

    public CsvStyle Style { get; }
    public char Separator { get; }
    public char DecimalMark { get; }

    /// <summary>The style for a device whose decimal mark is <paramref name="decimalMark"/>. Anything but a comma means a dot.</summary>
    public static CsvFormat ForDecimalMark(string? decimalMark) =>
        new(decimalMark == "," ? CsvStyle.Semicolon : CsvStyle.Comma);

    /// <summary>
    /// A text cell. A value that a spreadsheet would run as a formula gets a leading apostrophe (FR-009); a value containing the
    /// separator, a quote, CR or LF is quoted with inner quotes doubled (FR-008). Empty or null is an empty field.
    /// </summary>
    public string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (Array.IndexOf(FormulaStarts, value[0]) >= 0) value = "'" + value;
        return NeedsQuotes(value) ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>A number with exactly <paramref name="decimals"/> decimals and no thousands separator. Null is an empty field.</summary>
    public string Number(decimal? value, int decimals)
    {
        if (value is null) return "";
        var text = Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
        return DecimalMark == '.' ? text : text.Replace('.', DecimalMark);
    }

    /// <summary>A whole number. Null is an empty field.</summary>
    public string Integer(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>One line of cells, already formatted.</summary>
    public string Line(IEnumerable<string> cells) => string.Join(Separator, cells);

    private bool NeedsQuotes(string value)
    {
        foreach (var c in value)
            if (c == Separator || c == '"' || c == '\r' || c == '\n') return true;
        return false;
    }
}

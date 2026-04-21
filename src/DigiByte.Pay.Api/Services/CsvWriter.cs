using System.Globalization;
using System.Text;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Tiny RFC 4180 CSV writer. We only need it for two export endpoints
/// (sessions and webhook deliveries) and the values are well-behaved
/// (no embedded newlines beyond the response snippet), so a 30-line
/// helper is preferable to pulling in CsvHelper as a dependency.
///
/// Quoting rule: wrap a field in double quotes if it contains a comma,
/// double quote, CR, or LF; escape inner double quotes by doubling.
/// Line endings are CRLF per the spec — Excel and Google Sheets both
/// expect this.
/// </summary>
public static class CsvWriter
{
    public static void WriteRow(StringBuilder sb, IEnumerable<object?> fields)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Escape(field));
        }
        sb.Append("\r\n");
    }

    private static string Escape(object? value)
    {
        if (value is null) return string.Empty;
        var s = value switch
        {
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),  // ISO 8601, sortable
            decimal d   => d.ToString(CultureInfo.InvariantCulture),
            double f    => f.ToString("R", CultureInfo.InvariantCulture),
            float f     => f.ToString("R", CultureInfo.InvariantCulture),
            bool b      => b ? "true" : "false",
            _           => value.ToString() ?? string.Empty,
        };

        // Fast path: nothing needs quoting.
        if (s.IndexOfAny(QuotableChars) < 0) return s;

        // Escape inner quotes by doubling, then wrap in quotes.
        var sb = new StringBuilder(s.Length + 4);
        sb.Append('"');
        foreach (var ch in s)
        {
            if (ch == '"') sb.Append('"');
            sb.Append(ch);
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static readonly char[] QuotableChars = { ',', '"', '\r', '\n' };
}

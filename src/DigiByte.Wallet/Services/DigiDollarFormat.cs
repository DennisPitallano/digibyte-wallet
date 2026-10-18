using System.Globalization;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Display formatting for DigiDollar (FR-004, FR-025). Amounts stay integers everywhere else (DD in USD cents,
/// DGB in satoshis); conversion to text happens only here, using the user's locale for number formatting.
/// </summary>
public static class DigiDollarFormat
{
    /// <summary>The culture for a locale code such as "en" or "es"; unknown or empty codes use the invariant culture.</summary>
    public static CultureInfo CultureFor(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return CultureInfo.InvariantCulture;
        try { return CultureInfo.GetCultureInfo(locale, predefinedOnly: true); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    /// <summary>US dollars from cents, two decimals, locale separators, a leading <c>$</c> (and a leading minus when negative).</summary>
    public static string Usd(long cents, CultureInfo culture)
    {
        var negative = cents < 0;
        var magnitude = negative ? -(decimal)cents : cents;
        var number = (magnitude / 100m).ToString("N2", culture);
        return (negative ? "-$" : "$") + number;
    }

    /// <summary>
    /// Parse what a user typed as a dollar amount into exact cents. Accepts the locale's separators and an optional leading
    /// <c>$</c>; rejects empty, non-numeric, zero or negative input and anything with more than two decimals (never rounds).
    /// </summary>
    public static bool TryParseUsdToCents(string? text, CultureInfo culture, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('$')) s = s[1..].TrimStart();
        if (s.Length == 0) return false;

        if (!HasValidGrouping(s, culture.NumberFormat)) return false;

        var style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;
        if (!decimal.TryParse(s, style, culture, out var dollars)) return false;
        if (dollars <= 0) return false;

        // Exactly two decimals or fewer: scaling by 100 must leave no fractional part.
        var scaled = dollars * 100m;
        if (scaled != decimal.Truncate(scaled)) return false;
        if (scaled > long.MaxValue) return false;

        cents = (long)scaled;
        return cents > 0;
    }

    /// <summary>
    /// .NET accepts "1,2,3" as 123 when thousands separators are allowed. For money that is unsafe, so group separators in the
    /// whole-number part must split it into a leading group of 1 to 3 digits followed by groups of exactly three.
    /// </summary>
    private static bool HasValidGrouping(string s, NumberFormatInfo nf)
    {
        var group = nf.NumberGroupSeparator;
        if (string.IsNullOrEmpty(group) || !s.Contains(group, StringComparison.Ordinal)) return true;

        var decimalAt = s.IndexOf(nf.NumberDecimalSeparator, StringComparison.Ordinal);
        var whole = decimalAt >= 0 ? s[..decimalAt] : s;
        if (!whole.Contains(group, StringComparison.Ordinal)) return true; // the separator only appears in the fraction: let the parser reject it

        var parts = whole.Split(group);
        if (parts[0].Length is < 1 or > 3) return false;
        return parts.Skip(1).All(p => p.Length == 3);
    }

    /// <summary>
    /// How old the oracle price is: blocks between the oracle height and the node tip times 15 seconds
    /// (<see cref="Models.NetworkHealth.OracleStaleBlocks"/> blocks, about 5 minutes, is the stale threshold). Null when unknown.
    /// </summary>
    public static TimeSpan? OracleAge(Models.NetworkHealth health)
    {
        if (health.OracleHeight is not { } oracleHeight) return null;
        var blocks = Math.Max(0, health.NodeHeight - oracleHeight);
        return TimeSpan.FromSeconds(blocks * 15L);
    }

    /// <summary>DGB from satoshis without trailing zeros (for the network-fee line).</summary>
    public static string Dgb(long satoshis, CultureInfo culture) =>
        (satoshis / 100_000_000m).ToString("0.########", culture);

    /// <summary>The oracle price (USD per DGB times 10^6) as a four-decimal dollar price.</summary>
    public static string DgbPrice(long priceMicroUsd, CultureInfo culture) =>
        "$" + (priceMicroUsd / 1_000_000m).ToString("N4", culture);

    /// <summary>USD value in cents of a DGB amount in satoshis at an oracle price (USD per DGB times 10^6), rounded to the nearest cent.</summary>
    public static long DgbValueCents(long satoshis, long priceMicroUsd)
    {
        if (satoshis <= 0 || priceMicroUsd <= 0) return 0;
        // dollars = sats/1e8 * micro/1e6; cents = dollars * 100 = sats * micro / 1e12
        var cents = (decimal)satoshis * priceMicroUsd / 1_000_000_000_000m;
        return (long)Math.Round(cents, MidpointRounding.AwayFromZero);
    }
}

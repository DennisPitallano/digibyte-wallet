using System.Globalization;
using DigiByte.Pay.Web.Components.Shared.Ui;
using DigiByte.Wallet.Models;

// Same namespace as the Pos page: a "Pos" namespace beside the "Pos" class would not compile.
namespace DigiByte.Pay.Web.Components.Pages;

/// <summary>
/// The till's amount rules, kept apart from the page so they can be read (and tested) on their own: how many decimals a
/// currency takes (DGB 8, fiat per <see cref="Fiat.Decimals"/>), what a key press does to the typed text, and how the
/// typed text is shown ("12." stays "₱12." while typing; "1250" reads "₱1,250").
/// </summary>
public static class PosEntry
{
    public const string Dgb = "DGB";

    /// <summary>DigiDollar: USD-pegged, typed in dollars with 2 decimals, written "12.50 DD" (specs/033 US1).</summary>
    public const string Dd = "DD";

    /// <summary>Longest typed amount, digits and point included.</summary>
    public const int MaxLength = 16;

    public static bool IsDgb(string? code) => string.Equals(code, Dgb, StringComparison.OrdinalIgnoreCase);

    public static bool IsDd(string? code) => string.Equals(code, Dd, StringComparison.OrdinalIgnoreCase);

    /// <summary>A coin the till charges in directly (DGB or DD), as opposed to a fiat price converted to DGB.</summary>
    public static bool IsCoin(string? code) => IsDgb(code) || IsDd(code);

    public static int DecimalsFor(string? code) => IsDgb(code) ? 8 : IsDd(code) ? 2 : Fiat.Decimals(code);

    /// <summary>A stored or picked till currency, normalised ("php" → "PHP"); null when it isn't DGB, DD or a wallet currency.</summary>
    public static string? NormalizeCurrency(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim();
        if (IsDgb(c)) return Dgb;
        if (IsDd(c)) return Dd;
        return FiatCurrencies.Find(c)?.Code.ToUpperInvariant();
    }

    /// <summary>Recently used currencies: valid, distinct, most recent first, at most four.</summary>
    public static List<string> NormalizeRecent(IEnumerable<string?>? codes) =>
        (codes ?? [])
            .Select(NormalizeCurrency)
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

    /// <summary>Puts <paramref name="code"/> at the front of the recent list.</summary>
    public static List<string> PushRecent(IEnumerable<string>? recent, string code) =>
        NormalizeRecent(new[] { code }.Concat(recent ?? []));

    /// <summary>
    /// The typed text after one key: a digit, ".", "00", "back" or "clear". Leading zeros collapse ("007" → "7"), a
    /// second point is ignored, a currency with no decimals takes no point, and digits past the currency's decimals
    /// are ignored.
    /// </summary>
    public static string Press(string amount, string key, int decimals)
    {
        amount ??= "";
        switch (key)
        {
            case "clear":
                return "";
            case "back":
                return amount.Length > 0 ? amount[..^1] : amount;
            case ".":
                if (decimals == 0 || amount.Contains('.')) return amount;
                return amount.Length == 0 ? "0." : amount + ".";
            case "00":
                return Press(Press(amount, "0", decimals), "0", decimals);
            case { Length: 1 } when char.IsAsciiDigit(key[0]):
                if (amount == "0") return key;
                var dot = amount.IndexOf('.');
                if (dot >= 0 && amount.Length - dot - 1 >= decimals) return amount;
                if (amount.Length >= MaxLength) return amount;
                return amount + key;
            default:
                return amount;
        }
    }

    /// <summary>True when the typed text is valid in a currency with this many decimals.</summary>
    public static bool Fits(string amount, int decimals)
    {
        var dot = (amount ?? "").IndexOf('.');
        if (dot < 0) return true;
        return decimals > 0 && amount!.Length - dot - 1 <= decimals;
    }

    public static decimal? Parse(string? amount) =>
        decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    /// <summary>Rounded to the currency's decimals, half away from zero (DGB to the satoshi).</summary>
    public static decimal Round(decimal value, string? code) =>
        IsDgb(code) ? Math.Round(value, 8, MidpointRounding.AwayFromZero)
        : IsDd(code) ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
        : Fiat.Round(value, code);

    /// <summary>The symbol written before an amount ("₱", "KWD"); empty for DGB, which is written after.</summary>
    public static string Symbol(string? code) => IsCoin(code) ? "" : FiatCurrencies.SymbolFor(code).TrimEnd();

    /// <summary>Whether the symbol is a word ("KWD", "kr") that needs a space before the number.</summary>
    public static bool SymbolIsWord(string? code) => !IsCoin(code) && FiatCurrencies.SymbolFor(code).EndsWith(' ');

    /// <summary>The typed text as the readout shows it: integer part grouped, the fraction exactly as typed.</summary>
    public static string TypedNumber(string amount, string? code)
    {
        if (string.IsNullOrEmpty(amount)) return Placeholder(code);
        if (IsDgb(code)) return amount; // DGB amounts keep their plain 8-dp form
        var dot = amount.IndexOf('.');
        var whole = dot >= 0 ? amount[..dot] : amount;
        var grouped = decimal.TryParse(whole.Length == 0 ? "0" : whole, NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            ? w.ToString("#,0", CultureInfo.InvariantCulture)
            : whole;
        return dot >= 0 ? grouped + amount[dot..] : grouped;
    }

    /// <summary>What an empty readout shows ("0.00", "0", "0.000").</summary>
    public static string Placeholder(string? code)
    {
        if (IsDgb(code)) return "0";
        if (IsDd(code)) return "0.00";
        var d = Fiat.Decimals(code);
        return d == 0 ? "0" : "0." + new string('0', d);
    }

    /// <summary>A computed value without the symbol: fiat grouped with its decimals ("1,250.00"), DGB trimmed ("12.5").</summary>
    public static string Number(decimal value, string? code) =>
        IsDgb(code) ? FormatDgb(value) : IsDd(code) ? FormatDd(value) : Fiat.Number(value, code);

    /// <summary>A computed value with its currency: "₱1,250.00" or "12.5 DGB".</summary>
    public static string Money(decimal value, string? code) =>
        IsDgb(code) ? $"{FormatDgb(value)} DGB" : IsDd(code) ? $"{FormatDd(value)} DD" : Fiat.Format(value, code);

    /// <summary>A DigiDollar amount in dollars, grouped with 2 decimals ("1,250.00").</summary>
    public static string FormatDd(decimal dollars) =>
        Math.Round(dollars, 2, MidpointRounding.AwayFromZero).ToString("#,0.00", CultureInfo.InvariantCulture);

    /// <summary>A DigiDollar amount from USD cents with its unit ("12.50 DD").</summary>
    public static string FormatDdCents(long cents) => $"{FormatDd(cents / 100m)} DD";

    /// <summary>The lowest and highest DD amounts DigiPay accepts, in dollars (the API's 100 to 10,000,000 cents).</summary>
    public const decimal DdMin = 1.00m;
    public const decimal DdMax = 100_000.00m;

    /// <summary>True when a DD amount is within <see cref="DdMin"/> to <see cref="DdMax"/> (specs/033 FR-016).</summary>
    public static bool DdInRange(decimal dollars) => dollars >= DdMin && dollars <= DdMax;

    /// <summary>
    /// The typed amount to keep when the till switches currency to DD: a USD amount carries over as dollars; any
    /// other amount (another fiat currency or DGB) is cleared, never converted (specs/033 edge case).
    /// </summary>
    public static string CarryToDd(string amount, string? fromCode) =>
        string.Equals(fromCode, "USD", StringComparison.OrdinalIgnoreCase) || IsDd(fromCode)
            ? (Fits(amount, 2) ? amount : "")
            : "";

    public static string FormatDgb(decimal dgb) => dgb.ToString("0.########", CultureInfo.InvariantCulture);

    public static string FormatDgb(long sats) => FormatDgb(sats / 100_000_000m);

    /// <summary>A value as the keypad would have typed it ("100", "12.5"), for quick amounts and Charge again.</summary>
    public static string Typed(decimal value, string? code)
    {
        var d = DecimalsFor(code);
        var rounded = Round(value, code);
        return d == 0 ? rounded.ToString("0", CultureInfo.InvariantCulture)
                      : rounded.ToString("0." + new string('#', d), CultureInfo.InvariantCulture);
    }

    /// <summary>Quick amounts typed in settings ("100, 200 500"): positive, rounded to the currency, distinct, at most six.</summary>
    public static List<decimal> ParseQuickAmounts(string? text, string? code)
    {
        var result = new List<decimal>();
        foreach (var part in (text ?? "").Split([',', ';', ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!decimal.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) continue;
            v = Round(v, code);
            if (v <= 0 || result.Contains(v)) continue;
            result.Add(v);
            if (result.Count == 6) break;
        }
        return result;
    }

    /// <summary>Shrinks the big readout for long numbers so it never overflows its panel.</summary>
    public static string SizeClass(string text) => text.Length switch
    {
        > 16 => "is-xlong",
        > 11 => "is-long",
        _ => "",
    };

    /// <summary>"just now", "12s ago", "4 min ago", "2 h ago".</summary>
    public static string Ago(TimeSpan age)
    {
        if (age < TimeSpan.FromSeconds(5)) return "just now";
        if (age < TimeSpan.FromMinutes(1)) return $"{(int)age.TotalSeconds}s ago";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        return $"{(int)age.TotalHours} h ago";
    }
}

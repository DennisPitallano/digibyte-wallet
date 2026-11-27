using System.Globalization;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The Request tab's keypad rules (spec 036, US4, FR-022): what a key press does to the typed amount, how many decimals
/// a currency takes, and how the typed text is read. Modelled on DigiPay's till keypad (<c>PosEntry</c>), which stays
/// as it is. Pure.
/// </summary>
public static class RequestKeypadEntry
{
    public const string Dgb = "DGB";

    /// <summary>Longest typed amount, digits and point included.</summary>
    public const int MaxLength = 16;

    /// <summary>No amount can reach this: more DGB than will ever exist.</summary>
    public const decimal MaxAmount = 21_000_000_000m;

    /// <summary>8 for DGB; the currency's own for anything else (0 for yen, 3 for dinars).</summary>
    public static int DecimalsFor(string? code) =>
        string.Equals(code, Dgb, StringComparison.OrdinalIgnoreCase) ? 8 : FiatCurrencies.DecimalsFor(code);

    /// <summary>
    /// The typed text after one key: a digit, ".", "back" or "clear". Leading zeros collapse ("007" → "7"), a second
    /// point is ignored, a currency with no decimals takes no point, digits past the currency's decimals are ignored,
    /// and so is anything past <see cref="MaxLength"/>. Any other key does nothing.
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

    /// <summary>True when the typed text has at most the currency's decimals and stays below <see cref="MaxAmount"/>.</summary>
    public static bool Fits(string amount, int decimals)
    {
        amount ??= "";
        var dot = amount.IndexOf('.');
        if (dot >= 0 && (decimals == 0 || amount.Length - dot - 1 > decimals)) return false;
        return amount.Length == 0 || !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < MaxAmount;
    }

    /// <summary>The typed amount, always read with a point; null for empty or zero, which means "any amount".</summary>
    public static decimal? Parse(string? amount) =>
        decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v > 0 && v < MaxAmount ? v : null;
}

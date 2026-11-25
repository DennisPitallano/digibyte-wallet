using System.Globalization;
using DigiByte.Wallet.Models;

namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// Fiat amounts, written the way the wallet writes them (<see cref="FiatCurrencies"/>): the currency's symbol, then the
/// amount with grouping and the currency's own number of decimals ("₱1,250.00", "¥1,250", "KWD 1.250"). Unknown codes
/// fall back to the code itself and two decimals, so nothing is mislabelled.
/// </summary>
public static class Fiat
{
    /// <summary>Digits after the decimal point (0 for yen, won, rupiah and similar; 3 for the Gulf dinars).</summary>
    public static int Decimals(string? code) => FiatCurrencies.DecimalsFor(code);

    /// <summary>The symbol written before an amount, without trailing space ("₱", "KWD").</summary>
    public static string Symbol(string? code) => FiatCurrencies.SymbolFor(code).Trim();

    public static string Name(string? code) => FiatCurrencies.Find(code)?.Name ?? (code ?? "").ToUpperInvariant();

    /// <summary>The amount rounded to the currency's decimals, half away from zero.</summary>
    public static decimal Round(decimal amount, string? code) =>
        Math.Round(amount, Decimals(code), MidpointRounding.AwayFromZero);

    /// <summary>"1,250.00": grouped, the currency's decimals, no symbol.</summary>
    public static string Number(decimal amount, string? code) =>
        Round(amount, code).ToString("N" + Decimals(code), CultureInfo.InvariantCulture);

    /// <summary>"₱1,250.00" (symbol then amount), the wallet's display form.</summary>
    public static string Format(decimal amount, string? code)
    {
        var raw = FiatCurrencies.SymbolFor(code);
        // Symbols written as a word ("KWD ", "kr ") keep their space; glyphs ("₱", "$") sit against the number.
        return raw + Number(amount, code);
    }

    /// <summary>"₱1,250.00 PHP", for places where two currencies share a symbol ($, kr, Rs).</summary>
    public static string FormatWithCode(decimal amount, string? code) =>
        $"{Format(amount, code)} {(code ?? "").Trim().ToUpperInvariant()}";

    /// <summary>The plain number a person or a copy button would type: no grouping, the currency's decimals.</summary>
    public static string Plain(decimal amount, string? code) =>
        Round(amount, code).ToString(Decimals(code) == 0 ? "0" : "0." + new string('0', Decimals(code)), CultureInfo.InvariantCulture);

    /// <summary>Price of 1 DGB, with enough digits to be useful for small prices ("₱0.5825", "$0.0098").</summary>
    public static string Rate(decimal pricePerDgb, string? code)
    {
        var digits = pricePerDgb >= 100 ? 2 : pricePerDgb >= 1 ? 4 : 6;
        var text = pricePerDgb.ToString("#,0." + new string('#', digits), CultureInfo.InvariantCulture);
        return FiatCurrencies.SymbolFor(code) + text;
    }

    /// <summary>The DGB that pays <paramref name="fiat"/> at <paramref name="pricePerDgb"/>, on the satoshi grid, rounded up
    /// half-way so the customer never pays less than the quote.</summary>
    public static decimal ToDgb(decimal fiat, decimal pricePerDgb) =>
        pricePerDgb > 0 ? Math.Round(fiat / pricePerDgb, 8, MidpointRounding.AwayFromZero) : 0m;
}

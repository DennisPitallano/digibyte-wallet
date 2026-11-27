using System.Globalization;

namespace DigiByte.Wallet.Models;

/// <summary>
/// The price lock a request made in the requester's currency carries in its link (spec 036, FR-027a,
/// contracts/payment-link-fields.md): <c>fiat</c>, <c>currency</c> and <c>expires</c>. Other wallets ignore these keys,
/// so the DGB address and amount still work everywhere. The three are written together or not at all, and read only as
/// a group: any fault drops all three and leaves the rest of the link as it was. Pure.
/// </summary>
public static class PriceLockFields
{
    public const string FiatKey = "fiat";
    public const string CurrencyKey = "currency";
    public const string ExpiresKey = "expires";

    /// <summary>The three query pairs, already escaped, or none when any part is missing.</summary>
    public static IEnumerable<string> Write(decimal? fiatAmount, string? currency, DateTime? expiresAtUtc)
    {
        if (fiatAmount is not { } fiat || FiatCurrencies.Find(currency) is not { } ccy || expiresAtUtc is not { } expires)
            yield break;
        yield return $"{FiatKey}={fiat.ToString("F" + ccy.Decimals, CultureInfo.InvariantCulture)}";
        yield return $"{CurrencyKey}={ccy.Code}";
        yield return $"{ExpiresKey}={new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)).ToUnixTimeSeconds()}";
    }

    /// <summary>True for one of the three keys (compared without case).</summary>
    public static bool IsKey(string key) =>
        key.Equals(FiatKey, StringComparison.OrdinalIgnoreCase)
        || key.Equals(CurrencyKey, StringComparison.OrdinalIgnoreCase)
        || key.Equals(ExpiresKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the group from the raw (still escaped) values. Fails, and the caller keeps none of them, when any is
    /// missing, can't be unescaped, isn't a plain decimal with a point and at most the currency's decimals, names a
    /// currency the wallet doesn't know, or gives an expiry that isn't a positive Unix time.
    /// </summary>
    public static bool TryRead(string? rawFiat, string? rawCurrency, string? rawExpires,
        out decimal fiatAmount, out string currency, out DateTime expiresAtUtc)
    {
        fiatAmount = 0;
        currency = "";
        expiresAtUtc = default;
        if (Unescape(rawFiat) is not { } fiatText || Unescape(rawCurrency) is not { } ccyText || Unescape(rawExpires) is not { } expText)
            return false;
        if (FiatCurrencies.Find(ccyText.Trim()) is not { } ccy) return false;
        if (!decimal.TryParse(fiatText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fiat)) return false;
        if (fiat <= 0 || decimal.Round(fiat, ccy.Decimals) != fiat) return false;
        if (!long.TryParse(expText, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) || unix <= 0) return false;
        try { expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        fiatAmount = fiat;
        currency = ccy.Code;
        return true;
    }

    private static string? Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return null; }
    }
}

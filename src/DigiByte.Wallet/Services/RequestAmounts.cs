using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Converting a request's amount between the person's currency and DGB (spec 036, FR-016 to FR-018, research R5).
/// The DGB amount is fixed at the price shown when the request is made and held for <see cref="PriceLock"/>. Pure.
/// </summary>
public static class RequestAmounts
{
    /// <summary>How long a currency request holds its price (FR-017).</summary>
    public static readonly TimeSpan PriceLock = TimeSpan.FromMinutes(15);

    private const decimal SatoshisPerCoin = 100_000_000m;

    /// <summary>When a currency request made at <paramref name="createdAtUtc"/> expires.</summary>
    public static DateTime ExpiresAt(DateTime createdAtUtc) => createdAtUtc + PriceLock;

    /// <summary>
    /// The DGB to ask for <paramref name="fiatAmount"/> at <paramref name="price"/> (currency per DGB): the amount is
    /// first rounded to the currency's decimals, then divided, rounded half-up to 8 decimals. Null when the price, the
    /// currency or the amount can't make a request.
    /// </summary>
    public static decimal? ToDgb(decimal fiatAmount, decimal price, string? currency)
    {
        if (price <= 0 || FiatCurrencies.Find(currency) is not { } ccy) return null;
        var fiat = Math.Round(fiatAmount, ccy.Decimals, MidpointRounding.AwayFromZero);
        if (fiat <= 0) return null;
        var dgb = Math.Round(fiat / price, 8, MidpointRounding.AwayFromZero);
        return dgb > 0 ? dgb : null;
    }

    /// <summary>DGB still owed: the amount asked less what confirmed, never below zero. Null for any-amount requests.</summary>
    public static decimal? MissingDgb(PaymentRequest request)
    {
        if (request.AmountDgb is not { } asked) return null;
        var confirmed = request.Payments.Where(p => p.Confirmed).Sum(p => p.AmountSatoshis) / SatoshisPerCoin;
        return Math.Max(0m, asked - confirmed);
    }

    /// <summary>
    /// What <paramref name="missingDgb"/> is worth now, in the currency, rounded half-up to its decimals: what
    /// "Renew at today's price" asks for after a partly paid request expired (FR-018). Null when it can't be worked out.
    /// </summary>
    public static decimal? RestInCurrency(decimal missingDgb, decimal price, string? currency)
    {
        if (missingDgb <= 0 || price <= 0 || FiatCurrencies.Find(currency) is not { } ccy) return null;
        var rest = Math.Round(missingDgb * price, ccy.Decimals, MidpointRounding.AwayFromZero);
        return rest > 0 ? rest : null;
    }
}

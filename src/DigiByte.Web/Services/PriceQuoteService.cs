using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The DGB price in the person's currency, with when it was fetched, for requests made in that currency (spec 036,
/// research R5). Never throws: no price is null, and the currency option then says why (FR-021). Uses the price source
/// the wallet already uses and shares the pages' cache, so asking adds no request and sends nothing about a request
/// (FR-031).
/// </summary>
public sealed class PriceQuoteService(IBlockchainService blockchain, MemoryCacheService cache, AppState app)
{
    /// <summary>A quote older than this is fetched again before a request is made, so a request never locks a stale price.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The price now in the person's currency. With <paramref name="fresh"/>, a quote older than <see cref="MaxAge"/>
    /// is fetched again; without, any cached quote will do (for showing the equivalent while typing).
    /// </summary>
    public async Task<PriceQuote?> GetAsync(bool fresh = false)
    {
        var currency = app.FiatCurrency;
        var key = QuoteKey(currency);
        if (cache.TryGet<PriceQuote>(key, out var cached) && (!fresh || DateTime.UtcNow - cached.FetchedAt < MaxAge))
            return cached;

        try
        {
            var price = await blockchain.GetDgbPriceAsync(currency);
            if (price <= 0) return null;
            var quote = new PriceQuote(price, currency, DateTime.UtcNow);
            cache.Set(key, quote, MaxAge);
            cache.Set($"price:{currency}", price, MaxAge);   // the pages' own key
            return quote;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Requests] No price: {ex.GetType().Name}");
            return null;
        }
    }

    private static string QuoteKey(string currency) => $"pricequote:{currency}";
}

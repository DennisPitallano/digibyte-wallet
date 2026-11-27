namespace DigiByte.Wallet.Models;

/// <summary>The price of one DGB in <paramref name="Currency"/>, and when it was fetched (spec 036, research R5).</summary>
public sealed record PriceQuote(decimal Price, string Currency, DateTime FetchedAt);

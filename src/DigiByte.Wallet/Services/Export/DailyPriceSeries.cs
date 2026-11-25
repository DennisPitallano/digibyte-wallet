namespace DigiByte.Wallet.Services.Export;

/// <summary>How the daily prices were made (contracts/price-range-api.md <c>basis</c>).</summary>
public enum PriceBasis
{
    /// <summary>The source prices DGB in this currency directly.</summary>
    Native,

    /// <summary>The day's USD price converted at that day's USD exchange rate, by the price service (clarification Q7).</summary>
    ConvertedFromUsd,

    /// <summary>DigiDollar: 1 DD = 1 USD, converted at the day's rate outside USD.</summary>
    Peg,
}

/// <summary>
/// One price per day in the person's currency, covering an export's date range (data-model.md "DailyPriceSeries"). A day that
/// is missing is absent, never zero: its rows get empty Price and Value cells and are counted.
/// </summary>
public sealed class DailyPriceSeries
{
    private readonly IReadOnlyDictionary<DateOnly, decimal> _prices;

    public DailyPriceSeries(string currency, PriceBasis basis, IReadOnlyDictionary<DateOnly, decimal> prices,
        DateOnly from, DateOnly to, bool failed = false)
    {
        Currency = currency.ToUpperInvariant();
        Basis = basis;
        _prices = prices;
        RequestedFrom = from;
        RequestedTo = to;
        Failed = failed;
    }

    public string Currency { get; }
    public PriceBasis Basis { get; }
    public DateOnly RequestedFrom { get; }
    public DateOnly RequestedTo { get; }

    /// <summary>True when the price service could not be reached or answered with an error.</summary>
    public bool Failed { get; }

    public int Count => _prices.Count;

    /// <summary>The price for that day, by its date only: a row's local day looks up the entry with the same date.</summary>
    public bool TryGet(DateOnly day, out decimal price) => _prices.TryGetValue(day, out price) && price > 0;

    /// <summary>No prices: the export still runs, with empty Price and Value cells.</summary>
    public static DailyPriceSeries Empty(string currency, PriceBasis basis, DateOnly from, DateOnly to, bool failed) =>
        new(currency, basis, new Dictionary<DateOnly, decimal>(), from, to, failed);

    /// <summary>DigiDollar in USD: 1.00 every day, with no request.</summary>
    public static DailyPriceSeries UsdPeg(DateOnly from, DateOnly to)
    {
        var prices = new Dictionary<DateOnly, decimal>();
        for (var d = from; d <= to; d = d.AddDays(1)) prices[d] = 1m;
        return new DailyPriceSeries("USD", PriceBasis.Peg, prices, from, to);
    }
}

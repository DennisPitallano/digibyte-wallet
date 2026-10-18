namespace DigiByte.Wallet.Models;

/// <summary>A display currency the wallet can show DGB values in.</summary>
public sealed record FiatCurrency(string Code, string Name, string Symbol, string Region, int Decimals = 2);

/// <summary>
/// The currencies offered in Settings, grouped by region. CoinGecko prices DGB directly in only some of them
/// (<see cref="IsCoinGeckoNative"/>); for the rest the wallet takes the USD price and converts it with a USD exchange rate
/// (<see cref="DigiByte.Wallet.Services.FiatRateProvider"/>).
/// </summary>
public static class FiatCurrencies
{
    public const string Global = "Global";
    public const string Asia = "Asia";
    public const string Africa = "Africa";
    public const string Americas = "Americas";
    public const string Europe = "Europe";
    public const string Oceania = "Oceania";

    /// <summary>Display order of the groups.</summary>
    public static readonly string[] Regions = [Global, Asia, Africa, Americas, Europe, Oceania];

    public static readonly IReadOnlyList<FiatCurrency> All =
    [
        new("USD", "US Dollar", "$", Global),
        new("EUR", "Euro", "€", Global),
        new("GBP", "British Pound", "£", Global),

        // Asia (East, South-East, South, Central, Western)
        new("PHP", "Philippine Peso", "₱", Asia),
        new("JPY", "Japanese Yen", "¥", Asia, 0),
        new("CNY", "Chinese Yuan", "CN¥", Asia),
        new("HKD", "Hong Kong Dollar", "HK$", Asia),
        new("TWD", "New Taiwan Dollar", "NT$", Asia),
        new("KRW", "South Korean Won", "₩", Asia, 0),
        new("SGD", "Singapore Dollar", "S$", Asia),
        new("MYR", "Malaysian Ringgit", "RM", Asia),
        new("THB", "Thai Baht", "฿", Asia),
        new("IDR", "Indonesian Rupiah", "Rp", Asia, 0),
        new("VND", "Vietnamese Dong", "₫", Asia, 0),
        new("MMK", "Myanmar Kyat", "K", Asia, 0),
        new("INR", "Indian Rupee", "₹", Asia),
        new("PKR", "Pakistani Rupee", "Rs ", Asia),
        new("BDT", "Bangladeshi Taka", "৳", Asia),
        new("LKR", "Sri Lankan Rupee", "Rs ", Asia),
        new("NPR", "Nepalese Rupee", "Rs ", Asia),
        new("KZT", "Kazakhstani Tenge", "₸", Asia),
        new("AED", "UAE Dirham", "AED ", Asia),
        new("SAR", "Saudi Riyal", "SAR ", Asia),
        new("QAR", "Qatari Riyal", "QAR ", Asia),
        new("KWD", "Kuwaiti Dinar", "KWD ", Asia, 3),
        new("BHD", "Bahraini Dinar", "BHD ", Asia, 3),
        new("ILS", "Israeli Shekel", "₪", Asia),
        new("TRY", "Turkish Lira", "₺", Asia),

        // Africa
        new("NGN", "Nigerian Naira", "₦", Africa),
        new("ZAR", "South African Rand", "R", Africa),
        new("KES", "Kenyan Shilling", "KSh ", Africa),
        new("GHS", "Ghanaian Cedi", "GH₵", Africa),
        new("EGP", "Egyptian Pound", "E£", Africa),
        new("MAD", "Moroccan Dirham", "MAD ", Africa),
        new("TZS", "Tanzanian Shilling", "TSh ", Africa, 0),
        new("UGX", "Ugandan Shilling", "USh ", Africa, 0),
        new("ETB", "Ethiopian Birr", "Br ", Africa),
        new("RWF", "Rwandan Franc", "RF ", Africa, 0),
        new("ZMW", "Zambian Kwacha", "ZK ", Africa),
        new("XOF", "West African CFA Franc", "CFA ", Africa, 0),
        new("XAF", "Central African CFA Franc", "FCFA ", Africa, 0),
        new("DZD", "Algerian Dinar", "DA ", Africa),
        new("TND", "Tunisian Dinar", "DT ", Africa, 3),
        new("BWP", "Botswana Pula", "P", Africa),
        new("MUR", "Mauritian Rupee", "Rs ", Africa),

        // Americas
        new("CAD", "Canadian Dollar", "CA$", Americas),
        new("BRL", "Brazilian Real", "R$", Americas),
        new("MXN", "Mexican Peso", "MX$", Americas),

        // Europe
        new("CHF", "Swiss Franc", "CHF ", Europe),
        new("SEK", "Swedish Krona", "kr ", Europe),
        new("NOK", "Norwegian Krone", "kr ", Europe),
        new("DKK", "Danish Krone", "kr ", Europe),
        new("PLN", "Polish Zloty", "zł ", Europe),

        // Oceania
        new("AUD", "Australian Dollar", "A$", Oceania),
        new("NZD", "New Zealand Dollar", "NZ$", Oceania),
    ];

    /// <summary>The vs_currencies CoinGecko can price DigiByte in directly.</summary>
    private static readonly HashSet<string> CoinGeckoNative = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "AED", "ARS", "AUD", "BDT", "BHD", "BMD", "BRL", "CAD", "CHF", "CLP", "CNY", "CZK", "DKK", "EUR", "GBP",
        "GEL", "HKD", "HUF", "IDR", "ILS", "INR", "JPY", "KRW", "KWD", "LKR", "MMK", "MXN", "MYR", "NGN", "NOK", "NZD",
        "PHP", "PKR", "PLN", "RUB", "SAR", "SEK", "SGD", "THB", "TRY", "TWD", "UAH", "VND", "ZAR",
    };

    /// <summary>ISO 3166 flag code and the countries that use the currency (for search); no flag where several countries share it.</summary>
    private static readonly Dictionary<string, (string? Flag, string[] Countries)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = ("us", ["United States", "USA", "America"]),
        ["EUR"] = ("eu", ["Europe", "Eurozone", "Germany", "France", "Italy", "Spain", "Netherlands", "Ireland", "Portugal", "Belgium", "Austria", "Finland", "Greece"]),
        ["GBP"] = ("gb", ["United Kingdom", "UK", "Britain", "England", "Scotland", "Wales"]),
        ["PHP"] = ("ph", ["Philippines"]),
        ["JPY"] = ("jp", ["Japan"]),
        ["CNY"] = ("cn", ["China"]),
        ["HKD"] = ("hk", ["Hong Kong"]),
        ["TWD"] = ("tw", ["Taiwan"]),
        ["KRW"] = ("kr", ["South Korea", "Korea"]),
        ["SGD"] = ("sg", ["Singapore"]),
        ["MYR"] = ("my", ["Malaysia"]),
        ["THB"] = ("th", ["Thailand"]),
        ["IDR"] = ("id", ["Indonesia"]),
        ["VND"] = ("vn", ["Vietnam"]),
        ["MMK"] = ("mm", ["Myanmar", "Burma"]),
        ["INR"] = ("in", ["India"]),
        ["PKR"] = ("pk", ["Pakistan"]),
        ["BDT"] = ("bd", ["Bangladesh"]),
        ["LKR"] = ("lk", ["Sri Lanka"]),
        ["NPR"] = ("np", ["Nepal"]),
        ["KZT"] = ("kz", ["Kazakhstan"]),
        ["AED"] = ("ae", ["United Arab Emirates", "UAE", "Dubai", "Abu Dhabi"]),
        ["SAR"] = ("sa", ["Saudi Arabia"]),
        ["QAR"] = ("qa", ["Qatar"]),
        ["KWD"] = ("kw", ["Kuwait"]),
        ["BHD"] = ("bh", ["Bahrain"]),
        ["ILS"] = ("il", ["Israel"]),
        ["TRY"] = ("tr", ["Turkey", "Turkiye"]),
        ["NGN"] = ("ng", ["Nigeria"]),
        ["ZAR"] = ("za", ["South Africa"]),
        ["KES"] = ("ke", ["Kenya"]),
        ["GHS"] = ("gh", ["Ghana"]),
        ["EGP"] = ("eg", ["Egypt"]),
        ["MAD"] = ("ma", ["Morocco"]),
        ["TZS"] = ("tz", ["Tanzania"]),
        ["UGX"] = ("ug", ["Uganda"]),
        ["ETB"] = ("et", ["Ethiopia"]),
        ["RWF"] = ("rw", ["Rwanda"]),
        ["ZMW"] = ("zm", ["Zambia"]),
        ["XOF"] = (null, ["Senegal", "Ivory Coast", "Cote d'Ivoire", "Mali", "Benin", "Burkina Faso", "Togo", "Niger", "Guinea-Bissau"]),
        ["XAF"] = (null, ["Cameroon", "Gabon", "Chad", "Congo", "Central African Republic", "Equatorial Guinea"]),
        ["DZD"] = ("dz", ["Algeria"]),
        ["TND"] = ("tn", ["Tunisia"]),
        ["BWP"] = ("bw", ["Botswana"]),
        ["MUR"] = ("mu", ["Mauritius"]),
        ["CAD"] = ("ca", ["Canada"]),
        ["BRL"] = ("br", ["Brazil"]),
        ["MXN"] = ("mx", ["Mexico"]),
        ["CHF"] = ("ch", ["Switzerland"]),
        ["SEK"] = ("se", ["Sweden"]),
        ["NOK"] = ("no", ["Norway"]),
        ["DKK"] = ("dk", ["Denmark"]),
        ["PLN"] = ("pl", ["Poland"]),
        ["AUD"] = ("au", ["Australia"]),
        ["NZD"] = ("nz", ["New Zealand"]),
    };

    /// <summary>The flag file name (without extension) under <c>/images/flags</c>, or null when the currency has no single flag.</summary>
    public static string? FlagFor(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Countries.TryGetValue(code.Trim(), out var c) ? c.Flag : null;

    /// <summary>Countries that use the currency, so a search for "Kenya" finds KES.</summary>
    public static IReadOnlyList<string> CountryNames(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Countries.TryGetValue(code.Trim(), out var c) ? c.Countries : [];

    public static FiatCurrency? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(c => c.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<FiatCurrency> InRegion(string region) => All.Where(c => c.Region == region);

    /// <summary>The symbol written before an amount. Unknown codes fall back to the code itself so nothing is mislabelled.</summary>
    public static string SymbolFor(string? code) => Find(code)?.Symbol ?? (string.IsNullOrWhiteSpace(code) ? "$" : code.Trim().ToUpperInvariant() + " ");

    /// <summary>Digits after the decimal point for amounts of at least one unit (0 for yen, rupiah and similar).</summary>
    public static int DecimalsFor(string? code) => Find(code)?.Decimals ?? 2;

    /// <summary>True when CoinGecko can price DGB in this currency directly; otherwise convert from USD.</summary>
    public static bool IsCoinGeckoNative(string? code) => !string.IsNullOrWhiteSpace(code) && CoinGeckoNative.Contains(code.Trim());
}

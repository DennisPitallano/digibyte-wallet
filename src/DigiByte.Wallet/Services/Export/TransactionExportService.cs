using System.Globalization;
using System.Text;

namespace DigiByte.Wallet.Services.Export;

/// <summary>
/// Builds the CSV export of the active wallet's history (spec 031): read the complete history → map to rows → filter → daily
/// prices → write. The file is built on the device; nothing about the history leaves it (FR-016). It never returns a partial
/// file: an unreadable history is <see cref="ExportOutcome.HistoryUnavailable"/> and nothing to export is
/// <see cref="ExportOutcome.NothingToExport"/>, both without content (FR-006, FR-017).
/// </summary>
public sealed class TransactionExportService(WalletService wallet, IBlockchainService chain)
{
    public async Task<ExportResult> ExportAsync(ExportRequest request, ExportEnvironment env,
        IProgress<HistoryExportProgress>? progress = null, CancellationToken ct = default)
    {
        if (!wallet.IsUnlocked) return ExportResult.Of(ExportOutcome.Locked);   // FR-019

        List<ExportRow> rows;
        try
        {
            ct.ThrowIfCancellationRequested();
            var records = await wallet.LoadCompleteHistoryAsync(progress, ct);
            var today = env.Today;
            rows = records
                .Select(r => (Record: r, Row: ExportRowMapper.FromRecord(r, env.TimeZone, env.NameOf, env.WalletLabelOf)))
                .Where(x => request.Matches(x.Row, x.Record, today))
                .Select(x => x.Row)
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ExportResult.Of(ExportOutcome.Cancelled);
        }
        catch (HistoryIncompleteException)
        {
            return ExportResult.Of(ExportOutcome.HistoryUnavailable);
        }
        catch (InvalidOperationException) when (!wallet.IsUnlocked)
        {
            return ExportResult.Of(ExportOutcome.Locked);   // locked while reading
        }

        if (rows.Count == 0) return ExportResult.Of(ExportOutcome.NothingToExport);
        if (ct.IsCancellationRequested) return ExportResult.Of(ExportOutcome.Cancelled);

        DailyPriceSeries series;
        try
        {
            series = await PricesForAsync(rows, request.Asset, env.FiatCurrency, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ExportResult.Of(ExportOutcome.Cancelled);
        }
        var (priced, withoutPrice) = ApplyPrices(rows, series, env.FiatCurrency);

        var content = TransactionCsvWriter.Write(priced, CsvFormat.ForDecimalMark(env.DecimalMark), CsvWords.For(env.Language),
            env.FiatCurrency, series.Basis);

        return new ExportResult
        {
            Outcome = ExportOutcome.Ready,
            RowCount = priced.Select(r => r.TxId).Distinct().Count(),
            RowsWithoutPrice = withoutPrice,
            FileName = FileNameFor(wallet.ActiveWallet?.Name, request.Asset, env),
            Content = content,
        };
    }

    /// <summary>
    /// One daily price series covering the rows' local days, widened by one day on each side (FR-007, FR-016). The request carries
    /// only the currency, the asset and those two dates. DigiDollar in USD needs no request: 1 DD = 1 USD.
    /// </summary>
    private async Task<DailyPriceSeries> PricesForAsync(IReadOnlyList<ExportRow> rows, ExportAsset asset, string fiatCurrency,
        CancellationToken ct)
    {
        var from = rows.Min(r => r.LocalDate).AddDays(-1);
        var to = rows.Max(r => r.LocalDate).AddDays(1);
        if (asset == ExportAsset.Dd && string.Equals(fiatCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            return DailyPriceSeries.UsdPeg(from, to);
        return await chain.GetDailyPricesAsync(fiatCurrency, asset == ExportAsset.Dd ? "dd" : "dgb", from, to, ct);
    }

    /// <summary>
    /// Sets each row's Price (the price on its local day) and Value (Amount × Price, rounded to the currency's decimals). A row
    /// whose day has no price keeps empty cells and is counted, so the person can be told (FR-007).
    /// </summary>
    public static (List<ExportRow> Rows, int WithoutPrice) ApplyPrices(IReadOnlyList<ExportRow> rows, DailyPriceSeries series,
        string fiatCurrency)
    {
        var decimals = Models.FiatCurrencies.DecimalsFor(fiatCurrency);
        var missing = 0;
        var priced = new List<ExportRow>(rows.Count);
        foreach (var row in rows)
        {
            if (series.TryGet(row.LocalDate, out var price))
            {
                priced.Add(row with
                {
                    Price = price,
                    Value = Math.Round(row.Amount * price, decimals, MidpointRounding.AwayFromZero),
                });
            }
            else
            {
                missing++;
                priced.Add(row);
            }
        }
        return (priced, missing);
    }

    /// <summary>
    /// <c>digibyte-{wallet}-{DGB|DD}-{yyyy-MM-dd}.csv</c> (FR-011): the wallet name in lower-case ASCII letters, digits and dashes
    /// (accents dropped), or <c>wallet</c> when nothing is left; the export's local date.
    /// </summary>
    public static string FileNameFor(string? walletName, ExportAsset asset, ExportEnvironment env) =>
        $"digibyte-{Slug(walletName)}-{(asset == ExportAsset.Dd ? "DD" : "DGB")}-{env.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";

    private static string Slug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "wallet";
        var sb = new StringBuilder();
        var dash = false;
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(lower);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "wallet" : slug;
    }
}

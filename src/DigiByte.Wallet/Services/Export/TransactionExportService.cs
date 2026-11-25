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
            rows = records.Select(r => ExportRowMapper.FromRecord(r, env.TimeZone, env.NameOf, env.WalletLabelOf)).ToList();
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

        var basis = PriceBasis.Native;
        var content = TransactionCsvWriter.Write(rows, CsvFormat.ForDecimalMark(env.DecimalMark), CsvWords.For(env.Language),
            env.FiatCurrency, basis);

        return new ExportResult
        {
            Outcome = ExportOutcome.Ready,
            RowCount = rows.Select(r => r.TxId).Distinct().Count(),
            RowsWithoutPrice = rows.Count(r => r.Value is null),
            FileName = FileNameFor(wallet.ActiveWallet?.Name, request.Asset, env),
            Content = content,
        };
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

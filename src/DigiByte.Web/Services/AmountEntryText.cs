using System.Globalization;
using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>The translated message for a typed amount that can't be used (spec 028, <see cref="AmountEntry"/>).</summary>
public static class AmountEntryText
{
    public static string? Error(LocalizationService l, AmountEntryResult result, int maxDecimals, decimal maxValue) =>
        result.Error switch
        {
            null => null,
            AmountEntryError.NotANumber => l.T("amount.invalid.notANumber"),
            AmountEntryError.MoreThanOneMark => l.T("amount.invalid.moreThanOneMark"),
            AmountEntryError.TooManyDecimals => l.T("amount.invalid.tooManyDecimals", maxDecimals),
            AmountEntryError.NotPositive => l.T("amount.invalid.notPositive"),
            AmountEntryError.TooLarge => l.T("amount.invalid.tooLarge", maxValue.ToString("N0", CultureInfo.CurrentCulture)),
            _ => null,
        };
}

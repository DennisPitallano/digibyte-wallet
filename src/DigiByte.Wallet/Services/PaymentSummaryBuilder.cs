using System.Globalization;
using System.Text;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Builds the one payment summary shown the same way everywhere (specs/018-multisig-ux-polish, FR-006).</summary>
public static class PaymentSummaryBuilder
{
    /// <param name="changeSat">Change returning to the wallet, or null when unknown (the line is then left out).</param>
    public static PaymentSummaryModel Build(long amountSat, long feeSat, string recipient, string? memo, long? changeSat)
    {
        var cleanMemo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        string? change = changeSat is > 0
            ? $"{(changeSat.Value / 100_000_000m).ToString("0.########", CultureInfo.InvariantCulture)} DGB returns to this wallet as change."
            : null;

        return new PaymentSummaryModel(amountSat, recipient, Group(recipient), feeSat, amountSat + feeSat, cleanMemo, change);
    }

    /// <summary>Splits an address into groups of four so it is easier to compare by eye. The value is unchanged without the spaces.</summary>
    public static string Group(string address)
    {
        var sb = new StringBuilder(address.Length + address.Length / 4);
        for (var i = 0; i < address.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(address[i]);
        }
        return sb.ToString();
    }
}

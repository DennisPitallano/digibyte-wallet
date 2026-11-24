using System.Globalization;
using System.Text;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Builds the one payment summary shown the same way everywhere (specs/018-multisig-ux-polish, FR-006).</summary>
public static class PaymentSummaryBuilder
{
    /// <param name="changeSat">Change returning to the wallet, or null when unknown (the line is then left out).</param>
    public static PaymentSummaryModel Build(long amountSat, long feeSat, string recipient, string? memo, long? changeSat) =>
        Build([new ProposalRecipient(recipient, amountSat)], feeSat, memo, changeSat);

    /// <summary>
    /// The summary of a payment to one or more recipients (specs/029-multisig-multiple-recipients). Each line carries the
    /// warnings that point at it; <paramref name="contactNameFor"/> supplies names from this device's contacts only.
    /// </summary>
    /// <param name="changeSat">Change returning to the wallet, or null when unknown (the line is then left out).</param>
    public static PaymentSummaryModel Build(
        IReadOnlyList<ProposalRecipient> recipients,
        long feeSat,
        string? memo,
        long? changeSat,
        IReadOnlyList<ProposalWarning>? warnings = null,
        Func<string, string?>? contactNameFor = null)
    {
        var cleanMemo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        string? change = changeSat is > 0
            ? $"{(changeSat.Value / 100_000_000m).ToString("0.########", CultureInfo.InvariantCulture)} DGB returns to this wallet as change."
            : null;
        var amountSat = recipients.Sum(r => r.AmountSatoshis);
        var first = recipients.Count > 0 ? recipients[0].Address : "";
        var lines = recipients.Select((r, i) => new PaymentSummaryRecipient(
            r.Address,
            Group(r.Address),
            r.AmountSatoshis,
            contactNameFor?.Invoke(r.Address),
            (warnings ?? []).Where(w => w.RecipientIndex == i).Select(w => w.Kind).ToList())).ToList();
        var tooMany = (warnings ?? []).Any(w => w.Kind == ProposalWarningKind.TooManyRecipients);

        return new PaymentSummaryModel(amountSat, first, Group(first), feeSat, amountSat + feeSat, cleanMemo, change,
            lines, tooMany, changeSat is > 0 ? changeSat : null);
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

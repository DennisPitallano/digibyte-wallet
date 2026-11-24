using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>One payment summary, same fields in the same order everywhere (spec 018, FR-006).</summary>
public class MultisigUxPaymentSummaryTests
{
    private const string Addr = "dgb1qabcdefghijklmnopqrstuvwxyz0123456789";

    [Fact]
    public void Total_is_amount_plus_fee()
    {
        var s = PaymentSummaryBuilder.Build(250_000_000, 10_000, Addr, null, null);
        Assert.Equal(250_010_000, s.TotalOutSat);
    }

    [Fact]
    public void Address_is_grouped_without_changing_its_value()
    {
        var s = PaymentSummaryBuilder.Build(1, 1, Addr, null, null);
        Assert.Equal(Addr, s.RecipientAddress);
        Assert.Equal(Addr, s.RecipientGrouped.Replace(" ", ""));
        Assert.All(s.RecipientGrouped.Split(' '), g => Assert.InRange(g.Length, 1, 4));
    }

    [Fact]
    public void Empty_memo_is_omitted_and_a_memo_is_trimmed()
    {
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, "   ", null).Memo);
        Assert.Equal("rent", PaymentSummaryBuilder.Build(1, 1, Addr, " rent ", null).Memo);
    }

    [Fact]
    public void Change_note_is_omitted_when_there_is_no_change_or_it_is_unknown()
    {
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, null, null).ChangeNote);
        Assert.Null(PaymentSummaryBuilder.Build(1, 1, Addr, null, 0).ChangeNote);
    }

    [Fact]
    public void Change_note_says_it_returns_to_the_wallet()
    {
        var note = PaymentSummaryBuilder.Build(1, 1, Addr, null, 150_000_000).ChangeNote;
        Assert.NotNull(note);
        Assert.Contains("1.5", note);
        Assert.Contains("this wallet", note);
    }

    // ─── Several recipients (specs/029-multisig-multiple-recipients) ───

    private const string Addr2 = "dgb1qzyxwvutsrqponmlkjihgfedcba9876543210";

    [Fact]
    public void Several_recipients_each_get_a_line_with_their_warnings_and_local_name()
    {
        var lines = new List<ProposalRecipient> { new(Addr, 100_000_000), new(Addr2, 50_000), new(Addr, 25_000_000) };
        var warnings = new List<ProposalWarning>
        {
            new(ProposalWarningKind.RecipientBelowMinimum, 1),
            new(ProposalWarningKind.RecipientRepeated, 2),
            new(ProposalWarningKind.TooManyRecipients, null),
        };

        var s = PaymentSummaryBuilder.Build(lines, 10_000, null, 300_000_000, warnings, a => a == Addr2 ? "Alice" : null);

        Assert.Equal(3, s.Recipients.Count);
        Assert.Equal(Addr, s.Recipients[0].Address);
        Assert.Equal(Addr, s.Recipients[0].Grouped.Replace(" ", ""));
        Assert.Equal(100_000_000, s.Recipients[0].AmountSat);
        Assert.Null(s.Recipients[0].ContactName);
        Assert.Empty(s.Recipients[0].Warnings);
        Assert.Equal("Alice", s.Recipients[1].ContactName);
        Assert.Equal([ProposalWarningKind.RecipientBelowMinimum], s.Recipients[1].Warnings);
        Assert.Equal([ProposalWarningKind.RecipientRepeated], s.Recipients[2].Warnings);
        Assert.True(s.TooManyRecipients);
        Assert.Equal(125_050_000, s.AmountSat);
        Assert.Equal(125_060_000, s.TotalOutSat);
        Assert.Equal(Addr, s.RecipientAddress);
        Assert.Equal(300_000_000, s.ChangeSat);
    }

    [Fact]
    public void The_single_recipient_summary_is_unchanged_and_has_one_line()
    {
        var s = PaymentSummaryBuilder.Build(250_000_000, 10_000, Addr, " rent ", 150_000_000);

        Assert.Equal(250_000_000, s.AmountSat);
        Assert.Equal(Addr, s.RecipientAddress);
        Assert.Equal(250_010_000, s.TotalOutSat);
        Assert.Equal("rent", s.Memo);
        Assert.NotNull(s.ChangeNote);
        Assert.False(s.TooManyRecipients);
        var line = Assert.Single(s.Recipients);
        Assert.Equal(new ProposalRecipient(Addr, 250_000_000), new ProposalRecipient(line.Address, line.AmountSat));
    }
}

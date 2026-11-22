using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Whether a wallet started empty and whether a load contains its first payment (spec 025, FR-011 to FR-015, T004).
/// Every row of the transition table in specs/025-first-receive-flow/data-model.md.
/// </summary>
public class FirstReceiveRuleTests
{
    private static readonly TransactionRecord[] None = [];
    private static int _next;

    private static TransactionRecord Tx(
        TransactionDirection direction = TransactionDirection.Received, long sats = 100_000_000, int confirmations = 0, int ageDays = 0) => new()
    {
        TxId = (++_next).ToString("x").PadLeft(64, '0'),
        Direction = direction,
        AmountSatoshis = sats,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow.AddDays(-ageDays),
        Confirmations = confirmations,
    };

    private static FirstReceiveOutcome Evaluate(FirstReceiveState state, HistorySource source, params TransactionRecord[] txs) =>
        FirstReceiveRule.Evaluate(state, txs, source);

    // ── Undecided ──

    [Fact]
    public void Undecided_CompleteLoad_NoTransactions_BecomesEmpty()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.CompleteLoad);
        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Undecided_CompleteLoad_WithTransactions_BecomesHistory_WithoutPayment()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.CompleteLoad, Tx());
        Assert.Equal(FirstReceiveState.History, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undecided_IncompleteLoad_StaysUndecided(bool withTransactions)
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.IncompleteLoad, withTransactions ? [Tx()] : None);
        Assert.Equal(FirstReceiveState.Undecided, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Undecided_SavedSnapshot_WithTransactions_BecomesHistory()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.SavedSnapshot, Tx(confirmations: 50));
        Assert.Equal(FirstReceiveState.History, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Undecided_SavedSnapshot_WithoutTransactions_StaysUndecided()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.SavedSnapshot);
        Assert.Equal(FirstReceiveState.Undecided, outcome.State);
        Assert.Null(outcome.Payment);
    }

    /// <summary>The wallet's addresses are not all known yet, so an empty list does not prove the wallet is empty.</summary>
    [Fact]
    public void Undecided_PartialAddressLoad_NoTransactions_StaysUndecided()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.PartialAddressLoad);
        Assert.Equal(FirstReceiveState.Undecided, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Undecided_PartialAddressLoad_WithTransactions_BecomesHistory()
    {
        var outcome = Evaluate(FirstReceiveState.Undecided, HistorySource.PartialAddressLoad, Tx());
        Assert.Equal(FirstReceiveState.History, outcome.State);
        Assert.Null(outcome.Payment);
    }

    // ── Empty ──

    [Fact]
    public void Empty_CompleteLoad_NoTransactions_StaysEmpty()
    {
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad);
        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Empty_CompleteLoad_OneUnconfirmedReceipt_IsThePendingFirstPayment()
    {
        var tx = Tx(sats: 250_000_000);
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad, tx);

        Assert.Equal(FirstReceiveState.Empty, outcome.State); // Celebrated only once the celebration is shown
        Assert.NotNull(outcome.Payment);
        Assert.Equal(2.5m, outcome.Payment.AmountDgb);
        Assert.True(outcome.Payment.IsPending);
        Assert.Equal([tx.TxId], outcome.Payment.TxIds);
    }

    [Fact]
    public void Empty_CompleteLoad_TwoReceipts_OneConfirmed_IsOnePaymentForTheTotal_NotPending()
    {
        var a = Tx(sats: 100_000_000, confirmations: 3);
        var b = Tx(sats: 50_000_000);
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad, a, b);

        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.NotNull(outcome.Payment);
        Assert.Equal(1.5m, outcome.Payment.AmountDgb);
        Assert.False(outcome.Payment.IsPending);
        Assert.Equal(new[] { a.TxId, b.TxId }.Order(), outcome.Payment.TxIds.Order());
    }

    /// <summary>A payment that arrived while the wallet was closed is still the first payment: age does not matter.</summary>
    [Fact]
    public void Empty_CompleteLoad_ReceiptConfirmedDaysAgo_IsStillThePayment()
    {
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad, Tx(confirmations: 20_000, ageDays: 4));
        Assert.NotNull(outcome.Payment);
        Assert.False(outcome.Payment.IsPending);
    }

    [Fact]
    public void Empty_CompleteLoad_TransactionsButNoDgbReceipt_BecomesHistory_WithoutPayment()
    {
        // A sent transaction, and a "received" one that moved no DGB (as a DigiDollar transfer does).
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad,
            Tx(TransactionDirection.Sent), Tx(TransactionDirection.Received, sats: 0));
        Assert.Equal(FirstReceiveState.History, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Empty_CompleteLoad_SentAndReceipt_CountsOnlyTheReceipt()
    {
        var receipt = Tx(sats: 70_000_000, confirmations: 1);
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.CompleteLoad, Tx(TransactionDirection.Sent, sats: 10_000_000), receipt);
        Assert.NotNull(outcome.Payment);
        Assert.Equal(0.7m, outcome.Payment.AmountDgb);
        Assert.Equal([receipt.TxId], outcome.Payment.TxIds);
    }

    [Fact]
    public void Empty_IncompleteLoad_ContainingAReceipt_ChangesNothing()
    {
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.IncompleteLoad, Tx());
        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.Null(outcome.Payment);
    }

    /// <summary>A saved snapshot can only cause undecided → history.</summary>
    [Fact]
    public void Empty_SavedSnapshot_ContainingAReceipt_ChangesNothing()
    {
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.SavedSnapshot, Tx());
        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.Null(outcome.Payment);
    }

    /// <summary>Unsettled addresses only stop "empty" from being proven; a wallet already known to be empty still celebrates.</summary>
    [Fact]
    public void Empty_PartialAddressLoad_WithAReceipt_IsThePayment()
    {
        var outcome = Evaluate(FirstReceiveState.Empty, HistorySource.PartialAddressLoad, Tx(sats: 100_000_000));
        Assert.Equal(FirstReceiveState.Empty, outcome.State);
        Assert.NotNull(outcome.Payment);
        Assert.Equal(1m, outcome.Payment.AmountDgb);
    }

    // ── History and Celebrated never change ──

    public static TheoryData<FirstReceiveState, HistorySource> SettledStatesAndSources()
    {
        var data = new TheoryData<FirstReceiveState, HistorySource>();
        foreach (var state in new[] { FirstReceiveState.History, FirstReceiveState.Celebrated })
            foreach (var source in Enum.GetValues<HistorySource>())
                data.Add(state, source);
        return data;
    }

    [Theory]
    [MemberData(nameof(SettledStatesAndSources))]
    public void HistoryAndCelebrated_NeverChange_AndNeverPay(FirstReceiveState state, HistorySource source)
    {
        var outcome = Evaluate(state, source, Tx());
        Assert.Equal(state, outcome.State);
        Assert.Null(outcome.Payment);

        outcome = Evaluate(state, source);
        Assert.Equal(state, outcome.State);
        Assert.Null(outcome.Payment);
    }

    [Fact]
    public void Evaluate_NeverReturnsCelebrated_FromAnyOtherState()
    {
        foreach (var state in new[] { FirstReceiveState.Undecided, FirstReceiveState.Empty, FirstReceiveState.History })
            foreach (var source in Enum.GetValues<HistorySource>())
                foreach (var txs in new[] { None, [Tx()], [Tx(TransactionDirection.Sent)] })
                    Assert.NotEqual(FirstReceiveState.Celebrated, FirstReceiveRule.Evaluate(state, txs, source).State);
    }
}

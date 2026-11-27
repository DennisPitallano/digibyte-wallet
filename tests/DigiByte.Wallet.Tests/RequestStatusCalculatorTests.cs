using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A request's status comes only from the transactions paying its own address, recomputed from scratch on every check
/// (spec 036, data-model "Status rules", FR-004, FR-009).
/// </summary>
public class RequestStatusCalculatorTests
{
    private const string Addr = "dgb1qrequestaddress";
    private const long Coin = 100_000_000;
    private static readonly DateTime T0 = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

    private static PaymentRequest Request(decimal? amount = 1m, bool tracked = true) => new()
    {
        Id = "r",
        Address = Addr,
        AmountDgb = amount,
        Tracked = tracked,
        CreatedAt = T0,
    };

    private static TransactionInfo Tx(string id, long toRequest, int confirmations, long toOther = 0) => new()
    {
        TxId = id,
        Confirmations = confirmations,
        Timestamp = T0,
        FeeSatoshis = 0,
        Inputs = [],
        Outputs =
        [
            new TxOutput { Address = Addr, AmountSatoshis = toRequest, Index = 0 },
            .. toOther > 0 ? new[] { new TxOutput { Address = "dgb1qsomeoneelse", AmountSatoshis = toOther, Index = 1 } } : [],
        ],
    };

    private static RequestCheck Compute(PaymentRequest r, params TransactionInfo[] txs) =>
        RequestStatusCalculator.Compute(r, txs, T0.AddMinutes(1));

    [Fact]
    public void Untracked_IsNotTracked_WhateverArrived()
    {
        var check = Compute(Request(tracked: false), Tx("a", Coin, 3));

        Assert.Equal(PaymentRequestStatus.NotTracked, check.Status);
        Assert.False(check.BecamePaid);
    }

    [Fact]
    public void NothingReceived_IsWaiting()
    {
        var check = Compute(Request());

        Assert.Equal(PaymentRequestStatus.Waiting, check.Status);
        Assert.Empty(check.Payments);
        Assert.Equal(0, check.ArrivingSatoshis);
    }

    [Fact]
    public void OnlyPending_IsPaymentArriving()
    {
        var check = Compute(Request(), Tx("a", Coin, 0));

        Assert.Equal(PaymentRequestStatus.PaymentArriving, check.Status);
        Assert.Equal(0, check.ArrivingSatoshis); // the status itself says it; no separate note
        Assert.False(Assert.Single(check.Payments).Confirmed);
    }

    [Fact]
    public void PartlyConfirmed_WithMorePending_IsPartlyPaid_WithAnArrivingNote()
    {
        var check = Compute(Request(), Tx("a", 60_000_000, 2), Tx("b", 40_000_000, 0));

        Assert.Equal(PaymentRequestStatus.PartlyPaid, check.Status);
        Assert.Equal(40_000_000, check.ArrivingSatoshis);
    }

    [Fact]
    public void ExactlyTheAmount_IsPaid()
    {
        var check = Compute(Request(), Tx("a", Coin, 1));

        Assert.Equal(PaymentRequestStatus.Paid, check.Status);
    }

    [Fact]
    public void OneSatoshiShort_IsPartlyPaid()
    {
        var check = Compute(Request(), Tx("a", Coin - 1, 1));

        Assert.Equal(PaymentRequestStatus.PartlyPaid, check.Status);
    }

    [Fact]
    public void MoreThanTheAmount_IsOverpaid()
    {
        var check = Compute(Request(), Tx("a", Coin + 1, 1));

        Assert.Equal(PaymentRequestStatus.Overpaid, check.Status);
    }

    [Fact]
    public void TwoPayments_AddUp()
    {
        var check = Compute(Request(), Tx("a", 60_000_000, 1), Tx("b", 40_000_000, 5));

        Assert.Equal(PaymentRequestStatus.Paid, check.Status);
        Assert.Equal(2, check.Payments.Count);
    }

    [Fact]
    public void OnlyOutputsToTheRequestsAddress_Count()
    {
        var check = Compute(Request(), Tx("a", 60_000_000, 1, toOther: 10 * Coin));

        Assert.Equal(PaymentRequestStatus.PartlyPaid, check.Status);
        Assert.Equal(60_000_000, Assert.Single(check.Payments).AmountSatoshis);
    }

    [Fact]
    public void ATransactionNotPayingTheAddress_IsIgnored()
    {
        var check = Compute(Request(), Tx("a", 0, 1, toOther: Coin));

        Assert.Equal(PaymentRequestStatus.Waiting, check.Status);
        Assert.Empty(check.Payments);
    }

    [Theory]
    [InlineData(0, PaymentRequestStatus.Waiting)]
    [InlineData(1, PaymentRequestStatus.PaymentArriving)]
    [InlineData(2, PaymentRequestStatus.Paid)]
    public void AnyAmount_FollowsWhatArrived(int state, PaymentRequestStatus expected)
    {
        var txs = state switch
        {
            0 => Array.Empty<TransactionInfo>(),
            1 => [Tx("a", 5, 0)],
            _ => [Tx("a", 5, 1)],
        };

        Assert.Equal(expected, Compute(Request(amount: null), txs).Status);
    }

    [Fact]
    public void ADroppedTransaction_StopsCounting_AndTheStatusGoesBack()
    {
        var r = Request();
        var first = Compute(r, Tx("a", Coin, 0));
        Assert.Equal(PaymentRequestStatus.PaymentArriving, first.Status);
        r.Payments = first.Payments.ToList();
        r.Status = first.Status;

        var second = Compute(r);

        Assert.Equal(PaymentRequestStatus.Waiting, second.Status);
        Assert.Empty(second.Payments);
        Assert.False(second.BecamePaid);
    }

    [Fact]
    public void FirstSeenAt_IsKeptByTransactionId_AcrossChecks()
    {
        var r = Request();
        var first = RequestStatusCalculator.Compute(r, [Tx("a", 50_000_000, 0)], T0.AddMinutes(1));
        r.Payments = first.Payments.ToList();

        var second = RequestStatusCalculator.Compute(r, [Tx("a", 50_000_000, 1), Tx("b", 1, 0)], T0.AddMinutes(9));

        Assert.Equal(T0.AddMinutes(1), second.Payments.Single(p => p.TxId == "a").FirstSeenAt);
        Assert.Equal(T0.AddMinutes(9), second.Payments.Single(p => p.TxId == "b").FirstSeenAt);
    }

    [Fact]
    public void BecamePaid_WhenPaidAndNotYetAnnounced_Only()
    {
        Assert.True(Compute(Request(), Tx("a", Coin, 1)).BecamePaid);
        Assert.True(Compute(Request(), Tx("a", 2 * Coin, 1)).BecamePaid);
        Assert.False(Compute(Request(), Tx("a", Coin, 0)).BecamePaid);
        Assert.False(Compute(Request(), Tx("a", 1, 1)).BecamePaid);

        var announced = Request();
        announced.PaidAnnouncedAt = T0;
        Assert.False(Compute(announced, Tx("a", Coin, 1)).BecamePaid);
    }

    [Fact]
    public void BecameArriving_WhenConfirmedPlusPendingFirstReachesTheAmount()
    {
        var r = Request();
        var partial = Compute(r, Tx("a", 60_000_000, 1));
        Assert.False(partial.BecameArriving);
        r.Payments = partial.Payments.ToList();

        var reaching = Compute(r, Tx("a", 60_000_000, 1), Tx("b", 40_000_000, 0));
        Assert.True(reaching.BecameArriving);
        r.Payments = reaching.Payments.ToList();

        var again = Compute(r, Tx("a", 60_000_000, 1), Tx("b", 40_000_000, 0));
        Assert.False(again.BecameArriving);
    }

    [Fact]
    public void BecameArriving_ForAnyAmount_WhenSomethingFirstShowsUp()
    {
        var r = Request(amount: null);

        Assert.True(Compute(r, Tx("a", 5, 0)).BecameArriving);
        Assert.False(Compute(r).BecameArriving);
    }

    [Fact]
    public void AnAlreadyConfirmedPayment_IsNotAnnouncedAsArriving()
    {
        Assert.False(Compute(Request(), Tx("a", Coin, 1)).BecameArriving);
    }
}

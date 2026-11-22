using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which transactions count as "just arrived" for the "payment received" notice (spec 025, FR-022, FR-026, T020). This is
/// Home's rule, moved unchanged so Receive can use it too.
/// </summary>
public class NewArrivalsTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static TransactionRecord Tx(string id, TransactionDirection direction = TransactionDirection.Received,
        int confirmations = 0, int minutesAgo = 0) => new()
    {
        TxId = id,
        Direction = direction,
        AmountSatoshis = 100_000_000,
        FeeSatoshis = 0,
        Timestamp = Now.AddMinutes(-minutesAgo),
        Confirmations = confirmations,
    };

    [Fact]
    public void TheFirstList_AnnouncesNothing()
    {
        Assert.Empty(NewArrivals.Find(null, [Tx("a"), Tx("b", confirmations: 1)], Now));
    }

    [Fact]
    public void AReceivedTransactionNotSeenBefore_WithFewConfirmations_IsAnArrival()
    {
        var known = new HashSet<string> { "old" };
        var arrivals = NewArrivals.Find(known, [Tx("new", confirmations: 2, minutesAgo: 600), Tx("old")], Now);
        Assert.Equal(["new"], arrivals.Select(t => t.TxId));
    }

    [Fact]
    public void AnOlderTransactionShowingUpLate_IsHistoryFillingIn_NotAnArrival()
    {
        var known = new HashSet<string>();
        Assert.Empty(NewArrivals.Find(known, [Tx("late", confirmations: 3, minutesAgo: 16)], Now));
    }

    [Fact]
    public void AWellConfirmedTransaction_FromTheLastFifteenMinutes_IsStillAnArrival()
    {
        var known = new HashSet<string>();
        var arrivals = NewArrivals.Find(known, [Tx("quick", confirmations: 40, minutesAgo: 14)], Now);
        Assert.Single(arrivals);
    }

    [Fact]
    public void SentTransactions_AreNeverArrivals()
    {
        var known = new HashSet<string>();
        Assert.Empty(NewArrivals.Find(known, [Tx("sent", TransactionDirection.Sent)], Now));
    }

    [Fact]
    public void ATransactionAlreadySeen_IsNotReturnedAgain()
    {
        var known = new HashSet<string> { "a" };
        Assert.Empty(NewArrivals.Find(known, [Tx("a")], Now));
    }

    [Fact]
    public void LocalTimestamps_AreComparedInUtc()
    {
        var known = new HashSet<string>();
        var local = new TransactionRecord
        {
            TxId = "local", Direction = TransactionDirection.Received, AmountSatoshis = 1, FeeSatoshis = 0,
            Timestamp = Now.AddMinutes(-5).ToLocalTime(), Confirmations = 30,
        };
        Assert.Single(NewArrivals.Find(known, [local], Now));
    }

    [Fact]
    public void Find_DoesNotChangeTheKnownSet()
    {
        var known = new HashSet<string>();
        NewArrivals.Find(known, [Tx("a")], Now);
        Assert.Empty(known);
    }
}

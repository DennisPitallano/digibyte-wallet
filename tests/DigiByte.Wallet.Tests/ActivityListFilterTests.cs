using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Activity's filter and search rules, moved out of Transactions.razor so the list and the "What the list shows" export
/// (spec 031, US2) can never disagree. These pin down the rules the page had before the move.
/// </summary>
public class ActivityListFilterTests
{
    private static TransactionRecord Tx(string id, TransactionDirection direction, long sats = 150_000_000, string? memo = null,
        string? counterparty = "DQ4counterparty", string wallet = "dgb1qmine0") => new()
    {
        TxId = id,
        Direction = direction,
        AmountSatoshis = sats,
        FeeSatoshis = 1000,
        Timestamp = DateTime.UtcNow,
        Confirmations = 3,
        CounterpartyAddress = counterparty,
        Memo = memo,
        WalletAddresses = [wallet],
    };

    private static readonly Dictionary<string, string> NoLabels = new();

    private static ActivityView View(string filter = "all", string query = "", string? address = null,
        Func<string, string?>? names = null, IReadOnlyDictionary<string, string>? labels = null) =>
        new(filter, query, address, names ?? (_ => null), labels ?? NoLabels);

    [Theory]
    [InlineData("all", true, true)]
    [InlineData("received", true, false)]
    [InlineData("sent", false, true)]
    public void Filter_SelectsByDirection(string filter, bool receivedShown, bool sentShown)
    {
        Assert.Equal(receivedShown, ActivityListFilter.Matches(Tx("r", TransactionDirection.Received), View(filter)));
        Assert.Equal(sentShown, ActivityListFilter.Matches(Tx("s", TransactionDirection.Sent), View(filter)));
    }

    [Fact]
    public void EmptyQuery_MatchesEverything() =>
        Assert.True(ActivityListFilter.Matches(Tx("a", TransactionDirection.Received), View(query: "")));

    [Fact]
    public void Search_MatchesTheContactName_CaseInsensitive()
    {
        var view = View(query: "alice", names: a => a == "DQ4counterparty" ? "Customer – Alice" : null);
        Assert.True(ActivityListFilter.Matches(Tx("a", TransactionDirection.Received), view));
    }

    [Fact]
    public void Search_FallsBackToTheRecordedCounterpartyName()
    {
        var tx = Tx("a", TransactionDirection.Received);
        tx.CounterpartyName = "Bob's Shop";
        Assert.True(ActivityListFilter.Matches(tx, View(query: "bob")));
    }

    [Theory]
    [InlineData("dq4COUNTER")]       // counterparty address
    [InlineData("invoice 12")]       // memo
    [InlineData("TXABC")]            // transaction id
    [InlineData("1.5")]              // amount, invariant, trailing zeros dropped
    public void Search_MatchesAddressMemoTxIdAndAmount(string query)
    {
        var tx = Tx("txabc", TransactionDirection.Sent, sats: 150_000_000, memo: "Invoice 12");
        Assert.True(ActivityListFilter.Matches(tx, View(query: query)));
    }

    [Fact]
    public void Search_MatchesTheWalletAddressLabel()
    {
        var labels = new Dictionary<string, string> { [AddressKey.Normalize("dgb1qmine0")] = "Shop till" };
        Assert.True(ActivityListFilter.Matches(Tx("a", TransactionDirection.Received), View(query: "till", labels: labels)));
    }

    [Fact]
    public void Search_NoMatch_IsFalse() =>
        Assert.False(ActivityListFilter.Matches(Tx("a", TransactionDirection.Received), View(query: "zzz-nothing")));

    [Fact]
    public void SelectedWalletAddress_KeepsOnlyItsTransactions()
    {
        var mine = Tx("a", TransactionDirection.Received, wallet: "dgb1qmine0");
        var other = Tx("b", TransactionDirection.Received, wallet: "dgb1qother");
        var view = View(address: "dgb1qmine0");
        Assert.True(ActivityListFilter.Matches(mine, view));
        Assert.False(ActivityListFilter.Matches(other, view));
    }
}

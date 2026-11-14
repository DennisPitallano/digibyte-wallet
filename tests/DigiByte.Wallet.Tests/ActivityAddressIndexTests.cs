using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>What the activity rows show for a transaction's wallet addresses (spec 010, FR-001/FR-002).</summary>
public class ActivityAddressIndexTests
{
    private const string A1 = "dgb1qreceive0000000000000001abcdef";
    private const string A2 = "dgb1qreceive0000000000000002abcdef";
    private const string A3 = "dgb1qreceive0000000000000003abcdef";

    private static TransactionRecord Tx(params string[] wallet) => new()
    {
        TxId = Guid.NewGuid().ToString("N"),
        Direction = TransactionDirection.Received,
        AmountSatoshis = 100,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow,
        Confirmations = 1,
        WalletAddresses = wallet.ToList(),
    };

    private static IReadOnlyDictionary<string, string> Labels(params (string Address, string Label)[] items) =>
        ActivityAddressIndex.BuildLabelLookup(items.Select(i => new KeyValuePair<string, string>(i.Address, i.Label)));

    [Fact]
    public void Display_PrefersLabelOverAddress()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(A1), Labels((A1, "Donations")));

        Assert.Equal("Donations", primary);
        Assert.Equal(0, more);
    }

    [Fact]
    public void Display_NoLabel_ReturnsTheAddress()
    {
        var (primary, _) = ActivityAddressIndex.Display(Tx(A1), Labels());

        Assert.Equal(A1, primary);
    }

    [Fact]
    public void Display_SeveralAddresses_ShowsFirstAndCountsTheOthers()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(A2, A1, A3), Labels((A1, "Salary")));

        Assert.Equal(A2, primary);
        Assert.Equal(2, more);
    }

    [Fact]
    public void Display_NoAddressKnown_ReturnsNull()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(), Labels((A1, "Salary")));

        Assert.Null(primary);
        Assert.Equal(0, more);
    }

    private static TransactionRecord TxAt(DateTime time, TransactionDirection direction, string[] wallet, string[]? receivedTo = null)
        => new()
        {
            TxId = Guid.NewGuid().ToString("N"),
            Direction = direction,
            AmountSatoshis = 100,
            FeeSatoshis = 0,
            Timestamp = time,
            Confirmations = 1,
            WalletAddresses = wallet.ToList(),
            WalletReceivedAddresses = (receivedTo ?? []).ToList(),
        };

    // ── Build: the used-address list ──

    [Fact]
    public void Build_OneEntryPerNormalizedAddress_CountsBothLists_NewestFirst()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var txs = new[]
        {
            TxAt(t, TransactionDirection.Received, [A1]),
            TxAt(t.AddDays(1), TransactionDirection.Received, [A2]),
            TxAt(t.AddDays(2), TransactionDirection.Received, ["dgb1qreceive0000000000000001zzzzzz"]), // same address as A1, other checksum
            TxAt(t.AddDays(3), TransactionDirection.Sent, [A3], receivedTo: [A2]),
        };

        var used = ActivityAddressIndex.Build(txs, Labels((A2, "Salary")));

        Assert.Equal([A2, A3, A1], used.Select(u => u.Address));
        Assert.Equal(2, used.Single(u => u.Address == A1).TransactionCount);
        Assert.Equal(2, used.Single(u => u.Address == A2).TransactionCount);
        Assert.Equal(1, used.Single(u => u.Address == A3).TransactionCount);
        Assert.Equal("Salary", used.Single(u => u.Address == A2).Label);
        Assert.Null(used.Single(u => u.Address == A1).Label);
        Assert.Equal(t.AddDays(3), used.Single(u => u.Address == A2).LastUsed);
    }

    [Fact]
    public void Build_PendingTransactionsCount_AndAddressesWithoutTransactionsAreAbsent()
    {
        var pending = Tx(A1);
        pending.Confirmations = 0;

        var used = ActivityAddressIndex.Build([pending], Labels());

        Assert.Equal([A1], used.Select(u => u.Address));
    }

    [Fact]
    public void Build_EmptyHistoryOrNoAddressData_ReturnsEmpty()
    {
        Assert.Empty(ActivityAddressIndex.Build([], Labels()));
        Assert.Empty(ActivityAddressIndex.Build([Tx(), Tx()], Labels()));
    }

    // ── Matches: the address filter ──

    [Fact]
    public void Matches_NoFilter_MatchesEverything()
    {
        Assert.True(ActivityAddressIndex.Matches(Tx(), null));
        Assert.True(ActivityAddressIndex.Matches(Tx(A1), ""));
    }

    [Fact]
    public void Matches_TrueWhenAddressIsInEitherList_FalseOtherwise()
    {
        var tx = TxAt(DateTime.UtcNow, TransactionDirection.Sent, [A1], receivedTo: [A2]);

        Assert.True(ActivityAddressIndex.Matches(tx, A1));
        Assert.True(ActivityAddressIndex.Matches(tx, A2));
        Assert.False(ActivityAddressIndex.Matches(tx, A3));
        Assert.False(ActivityAddressIndex.Matches(Tx(), A1));
    }

    [Fact]
    public void Matches_IsIndependentOfBech32ChecksumVariant()
    {
        Assert.True(ActivityAddressIndex.Matches(Tx(A1), "dgb1qreceive0000000000000001zzzzzz"));
    }

    [Fact]
    public void Matches_SearchText_MatchesWalletAddressesAndTheirLabels()
    {
        var tx = Tx(A1, A2);
        var labels = Labels((A2, "Donations"));

        Assert.True(ActivityAddressIndex.MatchesText(tx, "receive0000000000000002", labels));
        Assert.True(ActivityAddressIndex.MatchesText(tx, "donat", labels));
        Assert.False(ActivityAddressIndex.MatchesText(tx, "nothing", labels));
        Assert.False(ActivityAddressIndex.MatchesText(Tx(), "donat", labels));
    }

    [Fact]
    public void BuildAndMatches_Handle500Transactions100Addresses_Quickly()
    {
        var addresses = Enumerable.Range(0, 100).Select(i => $"dgb1qperf{i:D20}abcdef").ToArray();
        var txs = Enumerable.Range(0, 500).Select(i => Tx(addresses[i % 100])).ToList();
        var labels = Labels((addresses[0], "First"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var used = ActivityAddressIndex.Build(txs, labels);
        var matched = txs.Count(t => ActivityAddressIndex.Matches(t, addresses[7]));
        sw.Stop();

        Assert.Equal(100, used.Count);
        Assert.Equal(5, matched);
        // SC-004 asks for under a second; the limit is generous so a slow machine does not fail it.
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Display_LabelMatchesAcrossBech32ChecksumVariants()
    {
        var (primary, _) = ActivityAddressIndex.Display(Tx("dgb1qreceive0000000000000001zzzzzz"), Labels((A1, "Salary")));

        Assert.Equal("Salary", primary);
    }
}

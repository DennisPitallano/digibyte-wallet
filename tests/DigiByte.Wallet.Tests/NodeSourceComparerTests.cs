using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T044: the developer comparison of our node against the public explorers
/// (specs/005-wallet-own-node-source, FR-013 and SC-008). The node is asked directly, whatever the monitor says.
/// </summary>
public class NodeSourceComparerTests
{
    /// <summary>A chain view with per-address balances and spendable outputs, usable as the node or as an explorer.</summary>
    private sealed class AddressBook(string name, int height = 1000) : IBlockchainService
    {
        public Dictionary<string, long> Balances { get; } = [];
        public Dictionary<string, List<UtxoInfo>> Utxos { get; } = [];
        public int Height { get; set; } = height;
        public bool Fails { get; set; }
        public List<string> Calls { get; } = [];

        private void Record(string call)
        {
            lock (Calls) Calls.Add(call);
            if (Fails) throw new HttpRequestException($"{name} is down");
        }

        public Task<long> GetBalanceAsync(string address)
        {
            Record($"balance:{address}");
            return Task.FromResult(Balances.GetValueOrDefault(address));
        }

        public Task<long> GetBalanceAsync(IEnumerable<string> addresses)
        {
            Record("balance:batch");
            return Task.FromResult(addresses.Sum(a => Balances.GetValueOrDefault(a)));
        }

        public Task<List<UtxoInfo>> GetUtxosAsync(string address)
        {
            Record($"utxos:{address}");
            return Task.FromResult(Utxos.GetValueOrDefault(address) ?? []);
        }

        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses)
        {
            Record("utxos:batch");
            return Task.FromResult(addresses.SelectMany(a => Utxos.GetValueOrDefault(a) ?? []).ToList());
        }

        public Task<int> GetBlockHeightAsync() { Record("height"); return Task.FromResult(Height); }

        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => throw new NotSupportedException();
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => throw new NotSupportedException();
        public Task<decimal> GetFeeRateAsync() => throw new NotSupportedException();
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => throw new NotSupportedException();

        public AddressBook Add(string address, long balance, params (string TxId, uint Index, long Amount)[] utxos)
        {
            Balances[address] = balance;
            Utxos[address] = utxos.Select(u => new UtxoInfo
                { TxId = u.TxId, OutputIndex = u.Index, AmountSatoshis = u.Amount, ScriptPubKey = "76", Confirmations = 5 }).ToList();
            return this;
        }
    }

    private static List<string> Addresses(int count) => Enumerable.Range(1, count).Select(i => $"DAddr{i:00}").ToList();

    private static (AddressBook Node, AddressBook Explorer) MatchingBooks(IEnumerable<string> addresses)
    {
        var node = new AddressBook("node");
        var explorer = new AddressBook("explorer");
        foreach (var (a, i) in addresses.Select((a, i) => (a, i)))
        {
            node.Add(a, 1_000 + i, ($"tx{i}", 0, 1_000 + i));
            explorer.Add(a, 1_000 + i, ($"tx{i}", 0, 1_000 + i));
        }
        return (node, explorer);
    }

    private static NodeSourceComparer Comparer(AddressBook node, params AddressBook[] explorers) =>
        new(node, explorers, heightTolerance: 5);

    // ---------- matching ----------

    [Fact]
    public async Task TwentyMatchingAddresses_AllMatch_AndTheNodeScansOnce()
    {
        var addresses = Addresses(20);
        var (node, explorer) = MatchingBooks(addresses);

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.True(report.AllMatch);
        Assert.True(report.BalanceMatches);
        Assert.True(report.UtxosMatch);
        Assert.True(report.HeightMatches);
        Assert.Equal(20, report.AddressCount);
        Assert.Equal(report.NodeBalance, report.ExplorerBalance);
        Assert.Equal(20, report.NodeUtxoCount);
        Assert.Empty(report.Rows);
        // one batch lookup for balances and one for outputs: the node does not scan once per address
        Assert.Equal(1, node.Calls.Count(c => c == "balance:batch"));
        Assert.Equal(1, node.Calls.Count(c => c == "utxos:batch"));
        Assert.DoesNotContain(node.Calls, c => c.StartsWith("balance:DAddr"));
    }

    [Fact]
    public async Task ChainHeights_AreReported()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);
        node.Height = 24465504;
        explorer.Height = 24465506;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.Equal(24465504, report.NodeHeight);
        Assert.Equal(24465506, report.ExplorerHeight);
        Assert.True(report.HeightMatches);   // within the tolerance of 5
    }

    // ---------- mismatches ----------

    [Fact]
    public async Task HeightBeyondTolerance_IsAMismatch()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);
        node.Height = 1000;
        explorer.Height = 1010;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.HeightMatches);
        Assert.False(report.AllMatch);
        Assert.True(report.BalanceMatches);
    }

    [Fact]
    public async Task ADifferentBalance_IsAMismatch_AndTheDifferingAddressIsFoundByDrillingDown()
    {
        var addresses = Addresses(5);
        var (node, explorer) = MatchingBooks(addresses);
        node.Balances["DAddr03"] += 500;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.AllMatch);
        Assert.False(report.BalanceMatches);
        Assert.Equal(5, report.Rows.Count);
        var bad = Assert.Single(report.Rows, r => !r.Matches);
        Assert.Equal("DAddr03", bad.Address);
        Assert.Equal(bad.ExplorerBalance + 500, bad.NodeBalance);
    }

    [Fact]
    public async Task ADifferentSetOfSpendableOutputs_IsAMismatch_EvenWithTheSameBalanceAndCount()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);
        node.Utxos["DAddr01"] = [new UtxoInfo { TxId = "other", OutputIndex = 0, AmountSatoshis = 1_000, ScriptPubKey = "76", Confirmations = 5 }];

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.True(report.BalanceMatches);
        Assert.False(report.UtxosMatch);
        Assert.False(report.AllMatch);
    }

    [Fact]
    public async Task ADifferentNumberOfOutputs_IsAMismatch()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);
        explorer.Utxos["DAddr02"].Add(new UtxoInfo { TxId = "extra", OutputIndex = 1, AmountSatoshis = 0, ScriptPubKey = "76", Confirmations = 1 });

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.UtxosMatch);
        Assert.NotEqual(report.NodeUtxoCount, report.ExplorerUtxoCount);
    }

    // ---------- failures never crash the comparison ----------

    [Fact]
    public async Task NodeFailure_IsReported_NotThrown()
    {
        var addresses = Addresses(3);
        var (node, explorer) = MatchingBooks(addresses);
        node.Fails = true;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.AllMatch);
        Assert.NotNull(report.NodeError);
        Assert.Contains("node is down", report.NodeError);
        Assert.Null(report.ExplorerError);
    }

    [Fact]
    public async Task EveryExplorerFailing_IsReported_NotThrown()
    {
        var addresses = Addresses(3);
        var (node, explorer) = MatchingBooks(addresses);
        explorer.Fails = true;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.AllMatch);
        Assert.NotNull(report.ExplorerError);
        Assert.Null(report.NodeError);
    }

    [Fact]
    public async Task FirstExplorerDown_TheNextOneIsUsed()
    {
        var addresses = Addresses(3);
        var (node, good) = MatchingBooks(addresses);
        var broken = new AddressBook("broken") { Fails = true };

        var report = await Comparer(node, broken, good).CompareAsync(addresses);

        Assert.True(report.AllMatch);
        Assert.Null(report.ExplorerError);
    }

    [Fact]
    public async Task NoAddresses_IsAMessage_NotAMatch()
    {
        var (node, explorer) = MatchingBooks([]);

        var report = await Comparer(node, explorer).CompareAsync([]);

        Assert.False(report.AllMatch);
        Assert.Equal(0, report.AddressCount);
        Assert.NotNull(report.Message);
        Assert.Empty(node.Calls);
    }

    [Fact]
    public async Task DuplicateAndBlankAddresses_AreCleanedUp()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);

        var report = await Comparer(node, explorer).CompareAsync([" DAddr01 ", "DAddr01", "", "  ", "DAddr02"]);

        Assert.Equal(2, report.AddressCount);
        Assert.True(report.AllMatch);
    }

    [Fact]
    public async Task TheNodeIsAskedDirectly_NoMonitorOrFallbackInvolved()
    {
        var addresses = Addresses(2);
        var (node, explorer) = MatchingBooks(addresses);

        await Comparer(node, explorer).CompareAsync(addresses);

        Assert.Contains("balance:batch", node.Calls);
        Assert.Contains("height", node.Calls);
    }

    [Fact]
    public async Task DrillDown_IsSkippedForVeryLargeSets()
    {
        var addresses = Addresses(60);
        var (node, explorer) = MatchingBooks(addresses);
        node.Balances["DAddr10"] += 1;

        var report = await Comparer(node, explorer).CompareAsync(addresses);

        Assert.False(report.BalanceMatches);
        Assert.Empty(report.Rows);   // too many addresses to scan one by one on the node
        Assert.NotNull(report.Message);
    }
}

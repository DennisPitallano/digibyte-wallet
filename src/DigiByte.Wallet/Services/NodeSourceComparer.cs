namespace DigiByte.Wallet.Services;

/// <summary>One address in a drill-down after the totals disagreed.</summary>
public sealed record AddressComparisonRow(
    string Address,
    long? NodeBalance,
    long? ExplorerBalance,
    int? NodeUtxoCount,
    int? ExplorerUtxoCount,
    bool Matches,
    string? Error);

/// <summary>Node versus explorer for a set of addresses (specs/005-wallet-own-node-source, data-model.md "Comparison Report").</summary>
public sealed record NodeComparisonReport(
    int AddressCount,
    int? NodeHeight,
    int? ExplorerHeight,
    bool HeightMatches,
    long? NodeBalance,
    long? ExplorerBalance,
    int? NodeUtxoCount,
    int? ExplorerUtxoCount,
    bool BalanceMatches,
    bool UtxosMatch,
    string? NodeError,
    string? ExplorerError,
    IReadOnlyList<AddressComparisonRow> Rows,
    string? Message)
{
    public bool AllMatch => NodeError is null && ExplorerError is null && HeightMatches && BalanceMatches && UtxosMatch && AddressCount > 0;
}

/// <summary>
/// The developer-view check that proves our node returns what the public explorers return, before real users depend on it (FR-013, SC-008).
/// It talks to the node client directly (pass a strict one), never through the fallback service, so it works whatever the monitor says,
/// and it changes nothing for regular views. The node answers address lookups by scanning, so the whole set is compared with one batch
/// lookup for balances and one for spendable outputs; only when the totals disagree does it drill down address by address (up to
/// <see cref="MaxDrillDown"/> addresses).
/// </summary>
public sealed class NodeSourceComparer
{
    public const int MaxDrillDown = 30;

    private readonly IBlockchainService _node;
    private readonly IReadOnlyList<IBlockchainService> _explorers;
    private readonly int _heightTolerance;

    public NodeSourceComparer(IBlockchainService node, IEnumerable<IBlockchainService> explorers, int heightTolerance)
    {
        _node = node;
        _explorers = explorers.ToList();
        _heightTolerance = heightTolerance;
    }

    public async Task<NodeComparisonReport> CompareAsync(IEnumerable<string> addresses, CancellationToken cancellationToken = default)
    {
        var list = addresses
            .Select(a => a?.Trim() ?? "")
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (list.Count == 0)
            return Empty("Enter at least one address to compare.");

        var nodeTask = ReadAsync(_node, list);
        var explorerTask = ReadFromFirstExplorerAsync(list);
        await Task.WhenAll(nodeTask, explorerTask);
        var node = nodeTask.Result;
        var explorer = explorerTask.Result;

        var heightMatches = node.Height is { } nh && explorer.Height is { } eh && Math.Abs(nh - eh) <= _heightTolerance;
        var balanceMatches = node.Balance is not null && node.Balance == explorer.Balance;
        var utxosMatch = node.Utxos is not null && explorer.Utxos is not null && SameOutputs(node.Utxos, explorer.Utxos);

        IReadOnlyList<AddressComparisonRow> rows = [];
        string? message = null;
        var bothReadable = node.Error is null && explorer.Error is null;

        if (bothReadable && (!balanceMatches || !utxosMatch))
        {
            if (list.Count <= MaxDrillDown)
                rows = await DrillDownAsync(list, explorer.Source!);
            else
                message = $"Totals differ. Too many addresses ({list.Count}) to check one by one on the node; compare at most {MaxDrillDown} to see which one differs.";
        }

        return new NodeComparisonReport(
            list.Count, node.Height, explorer.Height, heightMatches,
            node.Balance, explorer.Balance, node.Utxos?.Count, explorer.Utxos?.Count,
            balanceMatches, utxosMatch, node.Error, explorer.Error, rows, message);
    }

    // ---------------------------------------------------------------------------------------------------------

    private sealed record Reading(IBlockchainService? Source, int? Height, long? Balance, List<UtxoInfo>? Utxos, string? Error);

    private static async Task<Reading> ReadAsync(IBlockchainService source, List<string> addresses)
    {
        try
        {
            var height = await source.GetBlockHeightAsync();
            var balance = await source.GetBalanceAsync(addresses);
            var utxos = await source.GetUtxosAsync(addresses);
            return new Reading(source, height, balance, utxos, null);
        }
        catch (Exception ex)
        {
            return new Reading(source, null, null, null, ex.Message);
        }
    }

    private async Task<Reading> ReadFromFirstExplorerAsync(List<string> addresses)
    {
        string? lastError = null;
        foreach (var explorer in _explorers)
        {
            var reading = await ReadAsync(explorer, addresses);
            if (reading.Error is null)
                return reading;
            lastError = reading.Error;
        }
        return new Reading(null, null, null, null, lastError ?? "No explorer is configured.");
    }

    private static bool SameOutputs(List<UtxoInfo> a, List<UtxoInfo> b)
    {
        if (a.Count != b.Count)
            return false;
        var left = a.Select(u => $"{u.TxId}:{u.OutputIndex}:{u.AmountSatoshis}").Order(StringComparer.Ordinal);
        var right = b.Select(u => $"{u.TxId}:{u.OutputIndex}:{u.AmountSatoshis}").Order(StringComparer.Ordinal);
        return left.SequenceEqual(right);
    }

    private async Task<IReadOnlyList<AddressComparisonRow>> DrillDownAsync(List<string> addresses, IBlockchainService explorer)
    {
        var rows = new List<AddressComparisonRow>();
        foreach (var address in addresses)
        {
            try
            {
                var nodeBalance = await _node.GetBalanceAsync(address);
                var nodeUtxos = await _node.GetUtxosAsync(address);
                var explorerBalance = await explorer.GetBalanceAsync(address);
                var explorerUtxos = await explorer.GetUtxosAsync(address);
                rows.Add(new AddressComparisonRow(
                    address, nodeBalance, explorerBalance, nodeUtxos.Count, explorerUtxos.Count,
                    nodeBalance == explorerBalance && SameOutputs(nodeUtxos, explorerUtxos), null));
            }
            catch (Exception ex)
            {
                rows.Add(new AddressComparisonRow(address, null, null, null, null, false, ex.Message));
            }
        }
        return rows;
    }

    private static NodeComparisonReport Empty(string message) =>
        new(0, null, null, false, null, null, null, null, false, false, null, null, [], message);
}

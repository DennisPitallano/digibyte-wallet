using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>A watched address's saved summary and how much of its history has been fetched.</summary>
public sealed record AddressSummaryEntry(AddressSummary Summary, AddressHistoryCursor Cursor);

/// <summary>Saved summaries for one wallet and network (key <c>addr_summary_{walletId}_{network}</c>).</summary>
public sealed class AddressSummaryRecord
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, AddressSummaryEntry> Entries { get; set; } = new();

    /// <summary>When the last background full re-read finished (diagnostic only).</summary>
    public DateTime? LastFullResyncUtc { get; set; }
}

/// <summary>A saved transaction and its block height (null while pending), so confirmations can be worked out from the tip.</summary>
public sealed record SavedTransaction(TransactionInfo Tx, int? BlockHeight);

/// <summary>The merged, explorer-shaped history of one wallet and network (key <c>chain_history_{walletId}_{network}</c>).</summary>
public sealed class SavedChainHistory
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, SavedTransaction> Transactions { get; set; } = new();

    /// <summary>Adds the transaction, or replaces the saved one with the same txid (never a duplicate).</summary>
    public void Merge(TransactionInfo tx, int? blockHeight) => Transactions[tx.TxId] = new SavedTransaction(tx, blockHeight);
}

/// <summary>
/// Saved address summaries and chain history (spec 023), in the same on-device storage as the Home cache, per wallet and network.
/// A record from another version, or one that can't be read, is discarded and read as empty: the next refresh then reads in full.
/// </summary>
public sealed class AddressSummaryStore(ISecureStorage storage)
{
    private static readonly string[] Networks = ["mainnet", "testnet", "regtest"];

    public static string SummaryKey(string walletId, string network) => $"addr_summary_{walletId}_{network}";
    public static string HistoryKey(string walletId, string network) => $"chain_history_{walletId}_{network}";

    public async Task<AddressSummaryRecord> LoadSummariesAsync(string walletId, string network)
    {
        var record = await ReadAsync<AddressSummaryRecord>(SummaryKey(walletId, network));
        return record is { Version: AddressSummaryRecord.CurrentVersion } ? record : new AddressSummaryRecord();
    }

    public Task SaveSummariesAsync(string walletId, string network, AddressSummaryRecord record) =>
        storage.SetAsync(SummaryKey(walletId, network), JsonSerializer.Serialize(record));

    public async Task<SavedChainHistory> LoadHistoryAsync(string walletId, string network)
    {
        var history = await ReadAsync<SavedChainHistory>(HistoryKey(walletId, network));
        return history is { Version: SavedChainHistory.CurrentVersion } ? history : new SavedChainHistory();
    }

    public Task SaveHistoryAsync(string walletId, string network, SavedChainHistory history) =>
        storage.SetAsync(HistoryKey(walletId, network), JsonSerializer.Serialize(history));

    /// <summary>Removes both records of the wallet on every network (FR-025).</summary>
    public async Task DeleteAsync(string walletId)
    {
        foreach (var network in Networks)
        {
            await storage.RemoveAsync(SummaryKey(walletId, network));
            await storage.RemoveAsync(HistoryKey(walletId, network));
        }
    }

    private async Task<T?> ReadAsync<T>(string key) where T : class
    {
        try
        {
            var json = await storage.GetAsync(key);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

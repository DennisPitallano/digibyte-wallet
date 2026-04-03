using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Tracks wallet transactions locally in IndexedDB.
/// Used for regtest (no external explorer) and as a fast cache for all networks.
/// </summary>
public class TransactionTracker
{
    private readonly ISecureStorage _storage;
    private const string TxKey = "tx_history";
    private List<TransactionRecord>? _cache;

    public TransactionTracker(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task<List<TransactionRecord>> GetAllAsync()
    {
        if (_cache != null) return _cache;
        var json = await _storage.GetAsync(TxKey);
        _cache = json != null
            ? JsonSerializer.Deserialize<List<TransactionRecord>>(json) ?? []
            : [];
        return _cache;
    }

    /// <summary>
    /// Record a sent transaction.
    /// </summary>
    public async Task RecordSendAsync(string txId, string toAddress, long amountSatoshis, long feeSatoshis)
    {
        var all = await GetAllAsync();
        // Don't duplicate
        if (all.Any(t => t.TxId == txId)) return;

        all.Insert(0, new TransactionRecord
        {
            TxId = txId,
            Direction = TransactionDirection.Sent,
            AmountSatoshis = amountSatoshis,
            FeeSatoshis = feeSatoshis,
            Timestamp = DateTime.UtcNow,
            Confirmations = 0,
            CounterpartyAddress = toAddress,
        });

        await SaveAsync(all);
    }

    /// <summary>
    /// Record a received transaction.
    /// </summary>
    public async Task RecordReceiveAsync(string txId, string fromAddress, long amountSatoshis)
    {
        var all = await GetAllAsync();
        if (all.Any(t => t.TxId == txId)) return;

        all.Insert(0, new TransactionRecord
        {
            TxId = txId,
            Direction = TransactionDirection.Received,
            AmountSatoshis = amountSatoshis,
            FeeSatoshis = 0,
            Timestamp = DateTime.UtcNow,
            Confirmations = 0,
            CounterpartyAddress = fromAddress,
        });

        await SaveAsync(all);
    }

    /// <summary>
    /// Update confirmation count for tracked txs.
    /// </summary>
    public async Task UpdateConfirmationsAsync(IBlockchainService blockchain)
    {
        var all = await GetAllAsync();
        var changed = false;
        foreach (var tx in all.Where(t => t.Confirmations < 6))
        {
            var info = await blockchain.GetTransactionAsync(tx.TxId);
            if (info != null && info.Confirmations != tx.Confirmations)
            {
                tx.Confirmations = info.Confirmations;
                changed = true;
            }
        }
        if (changed) await SaveAsync(all);
    }

    private async Task SaveAsync(List<TransactionRecord> txs)
    {
        _cache = txs;
        var json = JsonSerializer.Serialize(txs);
        await _storage.SetAsync(TxKey, json);
    }
}

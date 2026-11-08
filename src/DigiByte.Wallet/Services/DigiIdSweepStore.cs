using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Remembers the sweeps of Digi-ID addresses, one record per swept address, per wallet, in encrypted storage. A sweep's
/// status survives a reload (FR-011a). A record holds no key, seed, raw transaction or signature (FR-015).
///
/// It is also what stops a second sweep of an address whose funds are already moving: <see cref="TryBeginAsync"/> creates a
/// record only if the address has none in flight, and does that check and the write under one lock (FR-012).
/// </summary>
public sealed class DigiIdSweepStore
{
    private const string KeyPrefix = "digiid_sweeps_";

    /// <summary>Most records kept per wallet. The oldest finished ones go first. A record that is still in flight is never dropped.</summary>
    public const int MaxRecords = 100;

    private readonly ISecureStorage _storage;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DigiIdSweepStore(ISecureStorage storage)
    {
        _storage = storage;
    }

    private static string KeyFor(string walletId) => $"{KeyPrefix}{walletId}";

    /// <summary>Sending→Pending and Sending→Failed, then Pending→Confirmed and Pending→Failed. Nothing leaves Confirmed or Failed.</summary>
    public static bool IsLegalTransition(DigiIdSweepStatus from, DigiIdSweepStatus to) => (from, to) switch
    {
        (DigiIdSweepStatus.Sending, DigiIdSweepStatus.Pending) => true,
        (DigiIdSweepStatus.Sending, DigiIdSweepStatus.Failed) => true,
        (DigiIdSweepStatus.Pending, DigiIdSweepStatus.Confirmed) => true,
        (DigiIdSweepStatus.Pending, DigiIdSweepStatus.Failed) => true,
        _ => false,
    };

    /// <summary>This wallet's records, newest first.</summary>
    public async Task<IReadOnlyList<DigiIdSweepRecord>> GetAllAsync(string walletId)
    {
        await _lock.WaitAsync();
        try
        {
            return Sorted(await LoadAsync(walletId));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The source addresses that have a record that is Sending or Pending.</summary>
    public async Task<HashSet<string>> GetInFlightAddressesAsync(string walletId)
    {
        var all = await GetAllAsync(walletId);
        return all.Where(r => r.IsInFlight).Select(r => r.SourceAddress).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Records a new sweep as Sending, unless this address already has one in flight, in which case nothing is written and the
    /// record that is in flight is returned. Check and write happen under one lock, so two callers cannot both win.
    /// </summary>
    public async Task<(bool Created, DigiIdSweepRecord Record)> TryBeginAsync(string walletId, DigiIdSweepRecord record)
    {
        if (record.Status != DigiIdSweepStatus.Sending || record.TxId is not null)
            throw new ArgumentException("A new sweep record must start as Sending, without a transaction id.", nameof(record));

        await _lock.WaitAsync();
        try
        {
            var all = await LoadAsync(walletId);

            var inFlight = all.FirstOrDefault(r => r.IsInFlight && r.SourceAddress == record.SourceAddress);
            if (inFlight is not null) return (false, inFlight);

            if (all.Any(r => r.Id == record.Id))
                throw new InvalidOperationException($"A sweep record with id {record.Id} already exists.");

            all.Add(record);
            await SaveAsync(walletId, Trim(all));
            return (true, record);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Moves a record to a new status and returns it. Throws <see cref="InvalidOperationException"/> for an unknown record, an
    /// illegal move, or a move to Pending without a transaction id; the record is left as it was.
    /// </summary>
    public async Task<DigiIdSweepRecord> TransitionAsync(string walletId, string id, DigiIdSweepStatus to,
        string? txId = null, long? feeSatoshis = null, string? failureReason = null, long? amountSatoshis = null)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadAsync(walletId);
            var index = all.FindIndex(r => r.Id == id);
            if (index < 0) throw new InvalidOperationException($"No sweep record with id {id}.");

            var current = all[index];
            if (!IsLegalTransition(current.Status, to))
                throw new InvalidOperationException($"A sweep cannot go from {current.Status} to {to}.");
            if (to == DigiIdSweepStatus.Pending && string.IsNullOrWhiteSpace(txId ?? current.TxId))
                throw new InvalidOperationException("A sweep cannot be Pending without a transaction id.");

            var now = DateTime.UtcNow;
            var updated = current with
            {
                Status = to,
                TxId = string.IsNullOrWhiteSpace(txId) ? current.TxId : txId,
                AmountSatoshis = amountSatoshis ?? current.AmountSatoshis,
                FeeSatoshis = feeSatoshis ?? current.FeeSatoshis,
                FailureReason = to == DigiIdSweepStatus.Failed ? failureReason : current.FailureReason,
                UpdatedUtc = now > current.UpdatedUtc ? now : current.UpdatedUtc.AddTicks(1),
            };

            all[index] = updated;
            await SaveAsync(walletId, all);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<DigiIdSweepRecord>> LoadAsync(string walletId)
    {
        var json = await _storage.GetAsync(KeyFor(walletId));
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<DigiIdSweepRecord>>(json) ?? [];
        }
        catch (JsonException)
        {
            // Damaged storage must not block the recovery section. The funds are still on their addresses and a fresh check finds them.
            return [];
        }
    }

    private Task SaveAsync(string walletId, List<DigiIdSweepRecord> records) =>
        _storage.SetAsync(KeyFor(walletId), JsonSerializer.Serialize(records));

    private static List<DigiIdSweepRecord> Sorted(IEnumerable<DigiIdSweepRecord> records) =>
        records.OrderByDescending(r => r.CreatedUtc).ToList();

    /// <summary>Drops the oldest finished records beyond <see cref="MaxRecords"/>. Records in flight are never dropped.</summary>
    private static List<DigiIdSweepRecord> Trim(List<DigiIdSweepRecord> records)
    {
        if (records.Count <= MaxRecords) return records;

        var droppable = records.Where(r => !r.IsInFlight).OrderBy(r => r.CreatedUtc).ToList();
        var excess = records.Count - MaxRecords;
        var drop = droppable.Take(excess).ToHashSet();
        return records.Where(r => !drop.Contains(r)).ToList();
    }
}

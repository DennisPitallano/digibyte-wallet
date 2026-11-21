using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>The outcome of one refresh cycle: the balance from current summaries and the saved history merged with what was fetched.</summary>
public sealed record SyncResult(
    long ConfirmedSatoshis,
    long PendingSatoshis,
    IReadOnlyCollection<TransactionInfo> Transactions,
    int AddressesFetched);

/// <summary>The once-per-unlock background full re-read of every watched address's history (FR-009).</summary>
public enum FullResyncState { NotStarted, Running, Done }

/// <summary>
/// Change-only history sync (spec 023). Each refresh reads one summary per watched address (reusing ones read in the last
/// 30 seconds, including address discovery's), computes the balance from them, and fetches history pages only for addresses whose
/// counts or amounts changed, merging them into the saved history. Once per unlock it re-reads every address's full history in the
/// background, which corrects what summaries can't show (a reorganisation) and completes the history of busy addresses.
/// Nothing here logs addresses, amounts or txids.
/// </summary>
public sealed class WalletHistorySync
{
    /// <summary>Summaries read this recently count as current (FR-006, FR-011).</summary>
    public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(30);

    /// <summary>Safety cap on one address's history (research R4).</summary>
    public const int MaxTransactionsPerAddress = 2000;

    private const int PageSize = 25;
    private const int SummaryConcurrency = 10;
    private const int PageConcurrency = 10;
    private const int ResyncConcurrency = 2;

    private readonly IBlockchainService _chain;
    private readonly IAddressSummarySource? _source;
    private readonly AddressSummaryStore _store;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<(string Wallet, string Network), WalletState> _states = new();

    /// <summary>How long after a failed full re-read before the next refresh may start it again.</summary>
    public TimeSpan FullResyncRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    public WalletHistorySync(IBlockchainService chain, AddressSummaryStore store, TimeProvider? time = null)
    {
        _chain = chain;
        _source = chain as IAddressSummarySource;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>False when the chain source can't give summaries at all; the wallet then always reads in full.</summary>
    public bool CanSync => _source is not null;

    private sealed class WalletState
    {
        public readonly Dictionary<string, (AddressSummary Summary, DateTimeOffset At)> Snapshot = new();
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        public readonly HashSet<string> CompletedThisSession = new();
        public Task<SyncResult>? Running;
        public FullResyncState Resync;
        public DateTimeOffset ResyncRetryAfter;
        public CancellationTokenSource? ResyncCts;
        public Task? ResyncTask;
        public bool TruncationLogged;

        // The saved records, read once per session and kept here, so a quiet refresh neither reads nor writes storage.
        public AddressSummaryRecord? Record;
        public SavedChainHistory? History;
    }

    private async Task<(AddressSummaryRecord Record, SavedChainHistory History)> LoadSavedAsync(string walletId, string network, WalletState state)
    {
        if (state.Record is { } record && state.History is { } history)
            return (record, history);
        record = await _store.LoadSummariesAsync(walletId, network);
        history = await _store.LoadHistoryAsync(walletId, network);
        state.Record = record;
        state.History = history;
        return (record, history);
    }

    private WalletState State(string walletId, string network)
    {
        if (!_states.TryGetValue((walletId, network), out var state))
            _states[(walletId, network)] = state = new WalletState();
        return state;
    }

    // ── Session hooks ──

    /// <summary>Seeds the in-memory snapshot with a summary address discovery just read (FR-011). Not saved until a refresh succeeds.</summary>
    public void Offer(string walletId, string network, AddressSummary summary)
    {
        lock (_gate) State(walletId, network).Snapshot[summary.Address] = (summary, _time.GetUtcNow());
    }

    /// <summary>Drops the wallet's in-memory summaries, so the next refresh reads every address again (after a send, FR-023).</summary>
    public void ForgetSnapshot(string walletId)
    {
        lock (_gate)
            foreach (var (key, state) in _states)
                if (key.Wallet == walletId) state.Snapshot.Clear();
    }

    /// <summary>A new unlock: drops the snapshot and cancels any full re-read, so this unlock runs its own (FR-009).</summary>
    public void ResetSession(string walletId)
    {
        lock (_gate)
            foreach (var (key, state) in _states)
                if (key.Wallet == walletId) Reset(state);
    }

    /// <summary>Locking: every wallet's session state goes.</summary>
    public void ResetAll()
    {
        lock (_gate)
            foreach (var state in _states.Values) Reset(state);
    }

    /// <summary>Switching away from a wallet cancels its running full re-read; it starts again on a later refresh.</summary>
    public void CancelFullResync(string walletId)
    {
        lock (_gate)
            foreach (var (key, state) in _states)
                if (key.Wallet == walletId && state.Resync == FullResyncState.Running)
                {
                    state.ResyncCts?.Cancel();
                    state.Resync = FullResyncState.NotStarted;
                }
    }

    private static void Reset(WalletState state)
    {
        state.ResyncCts?.Cancel();
        state.ResyncCts = null;
        state.Resync = FullResyncState.NotStarted;
        state.ResyncRetryAfter = default;
        state.Snapshot.Clear();
        state.CompletedThisSession.Clear();
        state.Record = null;
        state.History = null;
    }

    /// <summary>Drops the snapshot, saved summaries and saved chain history of a deleted wallet (FR-025).</summary>
    public async Task ForgetAsync(string walletId)
    {
        lock (_gate)
        {
            foreach (var key in _states.Keys.Where(k => k.Wallet == walletId).ToList())
            {
                Reset(_states[key]);
                _states.Remove(key);
            }
        }
        await _store.DeleteAsync(walletId);
    }

    /// <summary>The state of the full re-read (for tests and diagnostics).</summary>
    public FullResyncState GetFullResyncState(string walletId, string network)
    {
        lock (_gate) return State(walletId, network).Resync;
    }

    /// <summary>The running full re-read, if any (for tests and the benchmark).</summary>
    public Task? GetFullResyncTask(string walletId, string network)
    {
        lock (_gate) return State(walletId, network).ResyncTask;
    }

    /// <summary>Marks the full re-read done for this session without running it, so it can't add requests (the benchmark, T050).</summary>
    public void SuppressFullResync(string walletId, string network)
    {
        lock (_gate)
        {
            var state = State(walletId, network);
            state.ResyncCts?.Cancel();
            state.Resync = FullResyncState.Done;
        }
    }

    /// <summary>Lets the full re-read run again on a later refresh (undoes <see cref="SuppressFullResync"/>).</summary>
    public void AllowFullResync(string walletId, string network)
    {
        lock (_gate)
        {
            var state = State(walletId, network);
            if (state.Resync == FullResyncState.Done) state.Resync = FullResyncState.NotStarted;
        }
    }

    // ── Refresh cycle ──

    /// <summary>
    /// One refresh cycle for the given addresses. Concurrent callers for the same wallet and network share one run. Throws if any
    /// summary lookup fails (the balance must fail, FR-006); <see cref="SummaryUnavailableException"/> means use the full-read path.
    /// A history page failing for some addresses still returns the balance; those addresses keep their old summary and are
    /// fetched again next cycle (FR-010).
    /// </summary>
    public Task<SyncResult> RefreshAsync(string walletId, string network, IReadOnlyList<string> addresses, CancellationToken ct = default)
    {
        if (_source is null)
            throw new SummaryUnavailableException("This chain source can't give address summaries.");

        lock (_gate)
        {
            var state = State(walletId, network);
            if (state.Running is { IsCompleted: false } running)
                return running;
            var task = RunRefreshAsync(walletId, network, state, addresses.Distinct().ToList(), ct);
            state.Running = task;
            return task;
        }
    }

    private sealed record Fetched(
        string Address,
        bool Ok,
        bool Requested,
        List<TransactionInfo> Transactions,
        Dictionary<string, int> Heights,
        string? NewestConfirmedTxId,
        string[] PendingTxIds,
        bool Complete,
        bool Truncated);

    private static Fetched Failed(string address) => new(address, false, true, [], new(), null, [], false, false);

    private async Task<SyncResult> RunRefreshAsync(string walletId, string network, WalletState state, List<string> addresses, CancellationToken ct)
    {
        await Task.Yield();

        // 1. Summaries: reuse current ones, read the rest. Strict: any failure throws.
        var current = new Dictionary<string, AddressSummary>();
        var toRead = new List<string>();
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (var address in addresses)
            {
                if (state.Snapshot.TryGetValue(address, out var seen) && now - seen.At < SnapshotLifetime)
                    current[address] = seen.Summary;
                else
                    toRead.Add(address);
            }
        }

        using (var semaphore = new SemaphoreSlim(SummaryConcurrency))
        {
            var reads = toRead.Select(async address =>
            {
                await semaphore.WaitAsync(ct);
                try { return await _source!.GetAddressSummaryAsync(address); }
                finally { semaphore.Release(); }
            }).ToList();
            var fresh = await Task.WhenAll(reads);
            var stamp = _time.GetUtcNow();
            lock (_gate)
            {
                foreach (var summary in fresh)
                {
                    state.Snapshot[summary.Address] = (summary, stamp);
                    current[summary.Address] = summary;
                }
            }
        }

        var confirmed = addresses.Sum(a => current[a].Confirmed);
        var pending = addresses.Sum(a => current[a].PendingNet);

        // 2. History for the addresses that changed, merged into the saved history.
        await state.WriteLock.WaitAsync(ct);
        try
        {
            var (record, history) = await LoadSavedAsync(walletId, network, state);
            var tip = await ReadTipAsync();

            var changed = addresses.Where(a => NeedsFetch(current[a], record.Entries.GetValueOrDefault(a), history)).ToList();

            Fetched[] fetched;
            using (var semaphore = new SemaphoreSlim(PageConcurrency))
            {
                fetched = await Task.WhenAll(changed.Select(async address =>
                {
                    await semaphore.WaitAsync(ct);
                    try { return await FetchChangesAsync(address, current[address], record.Entries.GetValueOrDefault(address)?.Cursor, history, ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { return Failed(address); }
                    finally { semaphore.Release(); }
                }));
            }

            var ok = fetched.Where(f => f.Ok).ToList();
            var okAddresses = ok.Select(f => f.Address).ToHashSet();
            var seen = new HashSet<string>();
            foreach (var f in ok)
                foreach (var tx in f.Transactions)
                {
                    history.Merge(tx, f.Heights.TryGetValue(tx.TxId, out var h) ? h : null);
                    seen.Add(tx.TxId);
                }

            // A pending transaction no longer listed by any of its addresses, and not confirmed, was dropped or replaced.
            var stillPendingElsewhere = record.Entries
                .Where(e => !okAddresses.Contains(e.Key))
                .SelectMany(e => e.Value.Cursor.PendingTxIds)
                .ToHashSet();
            foreach (var f in ok)
            {
                foreach (var id in record.Entries.GetValueOrDefault(f.Address)?.Cursor.PendingTxIds ?? [])
                {
                    if (seen.Contains(id) || stillPendingElsewhere.Contains(id)) continue;
                    if (history.Transactions.TryGetValue(id, out var saved) && saved.BlockHeight is null)
                        history.Transactions.Remove(id);
                }
            }

            // Confirmations are worked out again on every read, so a new block alone doesn't need a write.
            Reconfirm(history, tip);
            if (ok.Count == 0)
                return new SyncResult(confirmed, pending, history.Transactions.Values.Select(s => s.Tx).ToList(), fetched.Length);

            try
            {
                await _store.SaveHistoryAsync(walletId, network, history);
                foreach (var f in ok)
                {
                    var previous = record.Entries.GetValueOrDefault(f.Address)?.Cursor;
                    record.Entries[f.Address] = new AddressSummaryEntry(current[f.Address], new AddressHistoryCursor
                    {
                        Complete = f.Complete,
                        Truncated = f.Truncated || (previous?.Truncated ?? false),
                        NewestConfirmedTxId = f.NewestConfirmedTxId ?? previous?.NewestConfirmedTxId,
                        PendingTxIds = f.PendingTxIds,
                    });
                }
                await _store.SaveSummariesAsync(walletId, network, record);

                lock (_gate)
                    foreach (var f in ok.Where(f => f.Complete))
                        state.CompletedThisSession.Add(f.Address);
            }
            catch (Exception ex)
            {
                // The balance and history are still right for this cycle. Drop the in-memory copy so the next cycle starts from what
                // storage really holds and reads the changed addresses again.
                Console.WriteLine($"[HistorySync] Could not save the history: {ex.GetType().Name}");
                state.Record = null;
                state.History = null;
            }

            return new SyncResult(confirmed, pending, history.Transactions.Values.Select(s => s.Tx).ToList(), fetched.Count(f => f.Requested));
        }
        finally
        {
            state.WriteLock.Release();
        }
    }

    /// <summary>
    /// History is fetched for an address with no saved summary that has been used, for one whose counts or amounts changed, and for
    /// one whose saved history is missing (FR-007).
    /// </summary>
    private static bool NeedsFetch(AddressSummary current, AddressSummaryEntry? saved, SavedChainHistory history)
    {
        if (saved is null) return true;
        if (saved.Summary.DiffersFrom(current)) return true;
        if (!current.IsUsed) return false;
        return saved.Cursor.NewestConfirmedTxId is { } newest
            ? !history.Transactions.ContainsKey(newest)
            : saved.Cursor.PendingTxIds.Any(id => !history.Transactions.ContainsKey(id)) || history.Transactions.Count == 0;
    }

    /// <summary>
    /// The newest page, then older pages until one holds a confirmed transaction already saved (FR-013). On an address's first load
    /// only the newest page is read: the background full re-read fetches the rest, so a busy address never holds up a refresh.
    /// </summary>
    private async Task<Fetched> FetchChangesAsync(string address, AddressSummary summary, AddressHistoryCursor? cursor,
        SavedChainHistory history, CancellationToken ct)
    {
        if (!summary.IsUsed)
            return new Fetched(address, true, false, [], new(), null, [], true, false);

        var page = await _source!.GetAddressHistoryPageAsync(address, null);
        var txs = new List<TransactionInfo>(page.Transactions);
        var heights = HeightsOf(page);
        var newestConfirmed = page.Transactions.FirstOrDefault(t => IsConfirmed(page, t))?.TxId;
        var pendingIds = page.Transactions.Where(t => !IsConfirmed(page, t)).Select(t => t.TxId).ToArray();

        bool complete;
        if (cursor is null)
        {
            complete = page.Transactions.Count(t => IsConfirmed(page, t)) >= summary.ConfirmedTxCount;
        }
        else
        {
            bool Known(AddressHistoryPage p, TransactionInfo t) =>
                IsConfirmed(p, t) && history.Transactions.TryGetValue(t.TxId, out var saved) && saved.BlockHeight is not null;

            var last = page;
            while (!last.Transactions.Any(t => Known(last, t))
                   && last.LastConfirmedTxId is { } after
                   && last.Transactions.Count(t => IsConfirmed(last, t)) >= PageSize
                   && txs.Count < MaxTransactionsPerAddress)
            {
                ct.ThrowIfCancellationRequested();
                last = await _source.GetAddressHistoryPageAsync(address, after);
                txs.AddRange(last.Transactions);
                foreach (var (id, h) in HeightsOf(last)) heights[id] = h;
            }
            complete = cursor.Complete;
        }

        return new Fetched(address, true, true, txs, heights, newestConfirmed, pendingIds, complete, false);
    }

    /// <summary>Every page of one address, up to the cap, for the full re-read.</summary>
    private async Task<Fetched> FetchAllAsync(string address, CancellationToken ct)
    {
        var page = await _source!.GetAddressHistoryPageAsync(address, null);
        var txs = new List<TransactionInfo>(page.Transactions);
        var heights = HeightsOf(page);
        var newestConfirmed = page.Transactions.FirstOrDefault(t => IsConfirmed(page, t))?.TxId;
        var pendingIds = page.Transactions.Where(t => !IsConfirmed(page, t)).Select(t => t.TxId).ToArray();

        var last = page;
        var truncated = false;
        while (last.LastConfirmedTxId is { } after && last.Transactions.Count(t => IsConfirmed(last, t)) >= PageSize)
        {
            if (txs.Count >= MaxTransactionsPerAddress) { truncated = true; break; }
            ct.ThrowIfCancellationRequested();
            last = await _source.GetAddressHistoryPageAsync(address, after);
            txs.AddRange(last.Transactions);
            foreach (var (id, h) in HeightsOf(last)) heights[id] = h;
        }

        if (txs.Count > MaxTransactionsPerAddress)
        {
            txs.RemoveRange(MaxTransactionsPerAddress, txs.Count - MaxTransactionsPerAddress);
            truncated = true;
        }

        return new Fetched(address, true, true, txs, heights, newestConfirmed, pendingIds, !truncated, truncated);
    }

    private static bool IsConfirmed(AddressHistoryPage page, TransactionInfo tx) =>
        page.BlockHeights is { } heights ? heights.ContainsKey(tx.TxId) : tx.Confirmations > 0;

    private static Dictionary<string, int> HeightsOf(AddressHistoryPage page) =>
        page.BlockHeights is { } heights ? new Dictionary<string, int>(heights) : new Dictionary<string, int>();

    private int _tip;
    private DateTimeOffset _tipAt;

    /// <summary>Shared tip lifetime here, so a balance read and the history read after it share one tip (SC-001).</summary>
    private static readonly TimeSpan TipLifetime = TimeSpan.FromSeconds(10);

    private async Task<int> ReadTipAsync()
    {
        lock (_gate)
            if (_tip > 0 && _time.GetUtcNow() - _tipAt < TipLifetime) return _tip;
        int tip;
        try { tip = await _chain.GetBlockHeightAsync(); }
        catch { return 0; }
        if (tip > 0)
            lock (_gate) { _tip = tip; _tipAt = _time.GetUtcNow(); }
        return tip;
    }

    /// <summary>Confirmations come from each transaction's block and one tip read; the stored count is never trusted. No tip, no change.</summary>
    private static void Reconfirm(SavedChainHistory history, int tip)
    {
        if (tip <= 0) return;
        foreach (var (id, saved) in history.Transactions.ToList())
        {
            var confirmations = saved.BlockHeight is { } h ? Math.Max(1, tip - h + 1) : 0;
            if (saved.Tx.Confirmations != confirmations)
                history.Transactions[id] = saved with { Tx = WithConfirmations(saved.Tx, confirmations) };
        }
    }

    private static TransactionInfo WithConfirmations(TransactionInfo tx, int confirmations) => new()
    {
        TxId = tx.TxId,
        Confirmations = confirmations,
        Timestamp = tx.Timestamp,
        FeeSatoshis = tx.FeeSatoshis,
        Inputs = tx.Inputs,
        Outputs = tx.Outputs,
    };

    // ── Background full re-read (FR-009) ──

    /// <summary>
    /// Starts the once-per-unlock background full re-read if it hasn't run in this session. Never throws and never blocks. Skipped
    /// (straight to Done) when every watched address was already read in full during this session.
    /// </summary>
    public void EnsureFullResync(string walletId, string network, IReadOnlyList<string> addresses)
    {
        if (_source is null) return;
        lock (_gate)
        {
            var state = State(walletId, network);
            if (state.Resync != FullResyncState.NotStarted) return;
            if (_time.GetUtcNow() < state.ResyncRetryAfter) return;
            if (addresses.All(state.CompletedThisSession.Contains))
            {
                state.Resync = FullResyncState.Done;
                return;
            }

            var cts = new CancellationTokenSource();
            state.ResyncCts = cts;
            state.Resync = FullResyncState.Running;
            state.ResyncTask = RunFullResyncAsync(walletId, network, state, addresses.Distinct().ToList(), cts);
        }
    }

    private async Task RunFullResyncAsync(string walletId, string network, WalletState state, List<string> addresses, CancellationTokenSource cts)
    {
        await Task.Yield();
        var ct = cts.Token;
        try
        {
            await state.WriteLock.WaitAsync(ct);
            AddressSummaryRecord startRecord;
            HashSet<string> startIds;
            try
            {
                var (savedRecord, savedHistory) = await LoadSavedAsync(walletId, network, state);
                startRecord = savedRecord;
                startIds = savedHistory.Transactions.Keys.ToHashSet();
            }
            finally
            {
                state.WriteLock.Release();
            }

            // An address the saved summary shows as never used has nothing to read.
            var toRead = addresses
                .Where(a => !(startRecord.Entries.TryGetValue(a, out var e) && !e.Summary.IsUsed))
                .ToList();

            Fetched[] results;
            using (var semaphore = new SemaphoreSlim(ResyncConcurrency))
            {
                results = await Task.WhenAll(toRead.Select(async address =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        await WaitForForegroundAsync(state, ct);
                        return await FetchAllAsync(address, ct);
                    }
                    finally { semaphore.Release(); }
                }));
            }

            await WaitForForegroundAsync(state, ct);
            await state.WriteLock.WaitAsync(ct);
            try
            {
                var (record, history) = await LoadSavedAsync(walletId, network, state);
                var tip = await ReadTipAsync();

                var found = new Dictionary<string, (TransactionInfo Tx, int? Height)>();
                foreach (var r in results)
                    foreach (var tx in r.Transactions)
                        found[tx.TxId] = (tx, r.Heights.TryGetValue(tx.TxId, out var h) ? h : null);

                // Gone from the chain: saved when this re-read started and no address lists it now.
                foreach (var id in startIds)
                    if (!found.ContainsKey(id)) history.Transactions.Remove(id);

                foreach (var (id, (tx, height)) in found)
                {
                    // Never turn a transaction a later refresh saw confirmed back into a pending one.
                    if (height is null && history.Transactions.TryGetValue(id, out var saved) && saved.BlockHeight is not null)
                        continue;
                    history.Merge(tx, height);
                }

                Reconfirm(history, tip);
                await _store.SaveHistoryAsync(walletId, network, history);

                foreach (var r in results)
                {
                    if (!record.Entries.TryGetValue(r.Address, out var entry)) continue;
                    record.Entries[r.Address] = entry with
                    {
                        Cursor = entry.Cursor with
                        {
                            Complete = r.Complete,
                            Truncated = r.Truncated,
                            NewestConfirmedTxId = r.NewestConfirmedTxId ?? entry.Cursor.NewestConfirmedTxId,
                        },
                    };
                }
                record.LastFullResyncUtc = _time.GetUtcNow().UtcDateTime;
                await _store.SaveSummariesAsync(walletId, network, record);
            }
            finally
            {
                state.WriteLock.Release();
            }

            lock (_gate)
            {
                if (results.Any(r => r.Truncated) && !state.TruncationLogged)
                {
                    state.TruncationLogged = true;
                    Console.WriteLine($"[HistorySync] An address reached the {MaxTransactionsPerAddress}-transaction cap; older history is not loaded.");
                }
                if (ReferenceEquals(state.ResyncCts, cts))
                    state.Resync = FullResyncState.Done;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
                if (ReferenceEquals(state.ResyncCts, cts) && state.Resync == FullResyncState.Running)
                    state.Resync = FullResyncState.NotStarted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HistorySync] Full re-read failed: {ex.GetType().Name}");
            lock (_gate)
            {
                // A failed write may have left the in-memory copy ahead of storage; read storage again next time.
                state.Record = null;
                state.History = null;
                if (ReferenceEquals(state.ResyncCts, cts))
                {
                    state.Resync = FullResyncState.NotStarted;
                    state.ResyncRetryAfter = _time.GetUtcNow() + FullResyncRetryDelay;
                }
            }
        }
    }

    /// <summary>Gives way to the user's own refresh: waits while one is running (FR-009).</summary>
    private async Task WaitForForegroundAsync(WalletState state, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Task? running;
            lock (_gate) running = state.Running is { IsCompleted: false } r ? r : null;
            if (running is null) return;
            try { await running; } catch { /* the foreground refresh reports its own failure */ }
        }
    }
}

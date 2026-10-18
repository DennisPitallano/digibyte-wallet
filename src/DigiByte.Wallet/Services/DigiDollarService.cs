using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// <see cref="IDigiDollarService"/>: orchestrates the encrypted local state, the NodeApi read client, and the
/// seed-derived keys. See specs/001-digidollar-wallet (research R2, R11, R12, R13, R14, R15, R16).
/// </summary>
public sealed class DigiDollarService : IDigiDollarService
{
    /// <summary>Standard gap limit for the restore scan (FR-007): stop after this many consecutive unused addresses.</summary>
    public const int GapLimit = 20;

    /// <summary>How long a send may be absent from both the mempool and the chain before it is marked Failed (FR-017).</summary>
    public static readonly TimeSpan PendingSendExpiry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// At most this much time is credited as "observed absence" per status check, so a backend outage or a closed app
    /// never pushes a send toward Failed (research R13).
    /// </summary>
    public static readonly TimeSpan MaxObservedGap = TimeSpan.FromMinutes(2);

    /// <summary>A pending send is dropped from local state once it reaches this many confirmations (research R13).</summary>
    public const int PendingSendDoneConfirmations = 6;

    /// <summary>DGB's minimum relay fee rate is 100 sat/vB; the DigiDollar fee is dominated by the 0.1 DGB floor anyway.</summary>
    private const long FeeRateSatPerVByte = 100;

    private const int MaxAddressesPerRequest = 200;
    private const int HistoryPageSize = 50;
    private const int MaxScanHistoryPages = 100;

    private readonly IDdSessionProvider _sessions;
    private readonly IDigiDollarNodeApi _api;
    private readonly DigiDollarStore _store;
    private readonly IDdFeeSource? _fees;
    private readonly Func<DateTimeOffset> _now;
    private readonly DdSendRateLimiter _limiter = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _limiterWallet;

    public DigiDollarService(
        IDdSessionProvider sessions, IDigiDollarNodeApi api, DigiDollarStore store,
        IDdFeeSource? feeSource = null, Func<DateTimeOffset>? clock = null)
    {
        _sessions = sessions;
        _api = api;
        _store = store;
        _fees = feeSource;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // ------------------------------------------------------------------ session

    private sealed record Ctx(DdWalletSession Session, DigiDollarKeyDerivation Hd, string Secret);

    private Ctx? Resolve()
    {
        var session = _sessions.GetDigiDollarSession();
        if (session?.Derivation is not { } hd) return null;
        return new Ctx(session, hd, DigiDollarStore.DeriveStoreSecret(hd.StorageKeyMaterial()));
    }

    private async Task<DigiDollarState> LoadStateAsync(Ctx c)
    {
        var state = await _store.LoadAsync(c.Session.WalletId, c.Secret);
        if (!await _store.HasUsedAsync(c.Session.WalletId))
            await _store.MarkUsedAsync(c.Session.WalletId);

        if (state is not null) return state;

        // First open on this device: persist the fresh account (ScanComplete=false) so a scan that fails midway retries next time.
        state = new DigiDollarState
        {
            Account = DigiDollarAccount.Create(
                c.Session.WalletId, c.Session.Network == "mainnet" ? DdNetwork.Mainnet : DdNetwork.Testnet, _now()),
        };
        await SaveAsync(c, state);
        return state;
    }

    private Task SaveAsync(Ctx c, DigiDollarState state) => _store.SaveAsync(state, c.Secret);

    private static NetworkHealth NoHealth => new() { Available = false };

    /// <summary>The send rate limit is per unlocked wallet session: a different wallet starts fresh.</summary>
    private void TrackLimiterWallet(string walletId)
    {
        if (_limiterWallet == walletId) return;
        _limiter.Reset();
        _limiterWallet = walletId;
    }

    public void ResetSession()
    {
        _limiter.Reset();
        _limiterWallet = null;
    }

    // ------------------------------------------------------------------ refresh

    public async Task<DdDashboard> RefreshAsync(CancellationToken ct = default, IProgress<DdScanProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            var c = Resolve();
            if (c is null)
                return new DdDashboard(DdViewState.NotSupported, NoHealth, null, [], [], 0, false, null, false, []);

            TrackLimiterWallet(c.Session.WalletId);
            var state = await LoadStateAsync(c);

            var health = await _api.GetHealthAsync(ct);
            if (!health.Available) return Fallback(state, health);
            if (!health.DeploymentActive)
                return new DdDashboard(DdViewState.NotActive, health, null, [], ReceiveOf(state), 0, false, null, state.Account.ScanComplete, []);

            try
            {
                if (!state.Account.ScanComplete)
                    state = await ScanAsync(c, state, progress, ct);

                var (reconciled, newlyFailed) = await ReconcilePendingAsync(state, ct);
                state = reconciled;

                var live = await BuildAsync(state, health, newlyFailed, ct);
                await SaveAsync(c, live.State);
                return live.Dashboard;
            }
            catch (DdBackendUnavailableException)
            {
                return Fallback(state, health);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<DigiDollarAddress> ReceiveOf(DigiDollarState state) =>
        state.Addresses.Where(a => a.Chain == DigiDollarChain.Receive).OrderBy(a => a.Index).ToList();

    private static DdDashboard Fallback(DigiDollarState state, NetworkHealth health)
    {
        if (state.Cache is { Balance: not null } cache)
            return new DdDashboard(DdViewState.UnavailableCached, health, cache.Balance, cache.RecentHistory,
                ReceiveOf(state), 0, true, cache.AsOf, state.Account.ScanComplete, []);

        return new DdDashboard(DdViewState.Unavailable, health, null, [], ReceiveOf(state), 0, false, null, state.Account.ScanComplete, []);
    }

    // ------------------------------------------------------------------ restore scan (R12)

    private sealed class ChainScan
    {
        public int Scanned;
        public int LastActive = -1;
        public bool Done => Scanned >= LastActive + 1 + GapLimit;
    }

    private async Task<DigiDollarState> ScanAsync(
        Ctx c, DigiDollarState state, IProgress<DdScanProgress>? progress, CancellationToken ct)
    {
        var chains = new Dictionary<DigiDollarChain, ChainScan>
        {
            [DigiDollarChain.Receive] = new(),
            [DigiDollarChain.Change] = new(),
        };
        var checkedCount = 0;
        var activeFound = 0;

        while (chains.Values.Any(s => !s.Done))
        {
            ct.ThrowIfCancellationRequested();

            var batch = new Dictionary<string, (DigiDollarChain Chain, int Index)>(StringComparer.Ordinal);
            foreach (var (chain, scan) in chains.Where(kv => !kv.Value.Done))
                for (var i = scan.Scanned; i < scan.Scanned + GapLimit; i++)
                    batch[c.Hd.GetAddress(c.Hd.Derive(chain, i))] = (chain, i);

            var active = await ActiveAddressesAsync(batch.Keys.ToList(), ct);

            foreach (var (chain, scan) in chains.Where(kv => !kv.Value.Done).ToList())
            {
                var found = batch.Where(b => b.Value.Chain == chain && active.Contains(b.Key)).Select(b => b.Value.Index).ToList();
                if (found.Count > 0)
                {
                    scan.LastActive = Math.Max(scan.LastActive, found.Max());
                    activeFound += found.Count;
                }
                scan.Scanned += GapLimit;
            }

            checkedCount += batch.Count;
            progress?.Report(new DdScanProgress(checkedCount, activeFound));
        }

        // Rebuild the address list: 0..lastActive on each chain, merged with anything the user already generated here.
        var known = state.Addresses.ToDictionary(a => (a.Chain, a.Index));
        foreach (var (chain, scan) in chains)
            for (var i = 0; i <= scan.LastActive; i++)
                if (!known.ContainsKey((chain, i)))
                    known[(chain, i)] = NewAddressRecord(c, chain, i);

        return state with
        {
            Account = state.Account with
            {
                NextReceiveIndex = Math.Max(state.Account.NextReceiveIndex, chains[DigiDollarChain.Receive].LastActive + 1),
                NextChangeIndex = Math.Max(state.Account.NextChangeIndex, chains[DigiDollarChain.Change].LastActive + 1),
                ScanComplete = true,
                LastScanAt = _now(),
            },
            Addresses = known.Values.OrderBy(a => a.Chain).ThenBy(a => a.Index).ToList(),
        };
    }

    /// <summary>Which of <paramref name="addresses"/> appear anywhere in their on-chain history (spent or not).</summary>
    private async Task<HashSet<string>> ActiveAddressesAsync(IReadOnlyList<string> addresses, CancellationToken ct)
    {
        var own = new HashSet<string>(addresses, StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in addresses.Chunk(MaxAddressesPerRequest))
        {
            string? cursor = null;
            for (var page = 0; page < MaxScanHistoryPages; page++)
            {
                var result = await _api.GetHistoryAsync(chunk, 200, cursor, ct);
                foreach (var item in result.Items)
                    foreach (var a in item.DdInputs.Concat(item.DdOutputs))
                        if (own.Contains(a.Address)) active.Add(a.Address);
                if (result.Next is null) break;
                cursor = result.Next;
            }
        }
        return active;
    }

    private DigiDollarAddress NewAddressRecord(Ctx c, DigiDollarChain chain, int index) => new()
    {
        WalletId = c.Session.WalletId,
        Address = c.Hd.GetAddress(c.Hd.Derive(chain, index)),
        Chain = chain,
        Index = index,
        CreatedAt = _now(),
    };

    // ------------------------------------------------------------------ pending sends (R13)

    /// <summary>
    /// Track submitted sends: still in the mempool or a block means still Pending (and dropped after 6 confirmations);
    /// absent from both for 30 minutes means Failed and its inputs are released. A backend that cannot answer never advances
    /// the absence timer (offline time is not absence). The first time a send is missing it is rebroadcast once.
    /// </summary>
    private async Task<(DigiDollarState State, IReadOnlyList<PendingDdSend> NewlyFailed)> ReconcilePendingAsync(
        DigiDollarState state, CancellationToken ct)
    {
        if (state.PendingSends.Count == 0) return (state, []);

        var now = _now();
        var kept = new List<PendingDdSend>();
        var newlyFailed = new List<PendingDdSend>();

        foreach (var p in state.PendingSends)
        {
            if (p.Status == DdTxStatus.Failed) { kept.Add(p); continue; } // waits for the user to acknowledge it

            DdTxStatusResult status;
            try { status = await _api.GetTxStatusAsync(p.Txid, ct); }
            catch (DdBackendUnavailableException) { kept.Add(p); continue; }

            switch (status.State)
            {
                case DdTxState.Confirmed when status.Confirmations >= PendingSendDoneConfirmations:
                    break; // done: it lives in the chain history now

                case DdTxState.Confirmed:
                case DdTxState.Mempool:
                    // Seen again: the absence clock starts over.
                    kept.Add(p with { FirstMissingAt = null, MissingObserved = TimeSpan.Zero, LastCheckAt = now });
                    break;

                default: // unknown
                    // Credit only the time we actually observed: from the previous successful check, capped.
                    var credit = p.FirstMissingAt is null || p.LastCheckAt is null
                        ? TimeSpan.Zero
                        : Min(now - p.LastCheckAt.Value, MaxObservedGap);
                    var updated = p with
                    {
                        FirstMissingAt = p.FirstMissingAt ?? now,
                        MissingObserved = p.MissingObserved + (credit < TimeSpan.Zero ? TimeSpan.Zero : credit),
                        LastCheckAt = now,
                    };

                    if (!p.Rebroadcasted)
                    {
                        updated = updated with { Rebroadcasted = true };
                        try { await _api.BroadcastAsync(p.RawTxHex, ct); }
                        catch (Exception ex) when (ex is DdApiException or DdBackendUnavailableException) { /* it may simply be gone */ }
                    }

                    if (updated.MissingObserved >= PendingSendExpiry)
                    {
                        updated = updated with { Status = DdTxStatus.Failed };
                        newlyFailed.Add(updated);
                    }
                    kept.Add(updated);
                    break;
            }
        }

        return (state with { PendingSends = kept }, newlyFailed);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    // ------------------------------------------------------------------ dashboard

    private sealed record Built(DigiDollarState State, DdDashboard Dashboard);

    private async Task<Built> BuildAsync(
        DigiDollarState state, NetworkHealth health, IReadOnlyList<PendingDdSend> newlyFailed, CancellationToken ct)
    {
        var addresses = state.Addresses.Select(a => a.Address).Distinct(StringComparer.Ordinal).ToList();
        var own = new HashSet<string>(addresses, StringComparer.Ordinal);

        var (utxos, entries) = await FetchAsync(addresses, ct);

        var balance = ComputeBalance(utxos, own, health.NodeHeight);
        var history = BuildHistory(entries, own, state.PendingSends);
        var available = SpendableUtxos(utxos, state).Sum(u => u.AmountCents);

        var now = _now();
        var cache = new DigiDollarCache(balance, history.Take(DigiDollarCache.MaxHistory).ToList(), now);
        var newState = state with { Cache = cache };

        var empty = balance.ConfirmedCents == 0 && balance.PendingCents == 0 && history.Count == 0;
        var dashboard = new DdDashboard(
            empty ? DdViewState.Empty : DdViewState.Ready, health, balance, cache.RecentHistory, ReceiveOf(state),
            available, false, null, state.Account.ScanComplete, newlyFailed);
        return new Built(newState, dashboard);
    }

    private async Task<(List<DigiDollarUtxo> Utxos, List<DdHistoryEntry> Entries)> FetchAsync(
        IReadOnlyList<string> addresses, CancellationToken ct)
    {
        var utxos = new List<DigiDollarUtxo>();
        var entries = new List<DdHistoryEntry>();
        foreach (var chunk in addresses.Chunk(MaxAddressesPerRequest))
        {
            foreach (var list in (await _api.GetUtxosAsync(chunk, 0, ct)).Values) utxos.AddRange(list);
            entries.AddRange((await _api.GetHistoryAsync(chunk, HistoryPageSize, null, ct)).Items);
        }
        return (utxos, entries);
    }

    /// <summary>Confirmed outputs that no pending transaction (the node's or ours) already spends.</summary>
    private static IEnumerable<DigiDollarUtxo> SpendableUtxos(IEnumerable<DigiDollarUtxo> utxos, DigiDollarState state)
    {
        var reserved = ReservedOutpoints(state);
        return utxos.Where(u => u.IsSpendable && !reserved.Contains(new DdOutpoint(u.Txid, u.Vout)));
    }

    /// <summary>Inputs held by sends that are still pending (a Failed send releases its inputs).</summary>
    private static HashSet<DdOutpoint> ReservedOutpoints(DigiDollarState state) =>
        state.PendingSends.Where(p => p.Status != DdTxStatus.Failed).SelectMany(p => p.SpentOutpoints).ToHashSet();

    private static DigiDollarBalance ComputeBalance(IReadOnlyList<DigiDollarUtxo> utxos, HashSet<string> own, int nodeHeight)
    {
        long confirmed = 0;
        long pendingIn = 0;
        long pendingOut = 0;
        var perAddress = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var u in utxos.Where(u => own.Contains(u.Address)))
        {
            if (u.Confirmations >= 1) confirmed += u.AmountCents; else pendingIn += u.AmountCents;
            if (u.SpentByMempool) pendingOut += u.AmountCents;
            else perAddress[u.Address] = perAddress.GetValueOrDefault(u.Address) + u.AmountCents;
        }
        return new DigiDollarBalance(confirmed, pendingIn - pendingOut, perAddress, nodeHeight);
    }

    private List<DigiDollarTransaction> BuildHistory(
        IReadOnlyList<DdHistoryEntry> entries, HashSet<string> own, IReadOnlyList<PendingDdSend> pendingSends)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DigiDollarTransaction>();

        // Our own submitted sends that the index has not shown yet (or that failed) come first.
        foreach (var p in pendingSends.Where(p => entries.All(e => e.Txid != p.Txid)))
        {
            if (!seen.Add(p.Txid)) continue;
            result.Add(new DigiDollarTransaction(p.Txid, DdDirection.Sent, p.Recipient, p.AmountCents, p.FeeSats, 0, p.SubmittedAt, p.Status));
        }

        // Unconfirmed first, then newest confirmed first (the NodeApi already orders them this way; merging chunks needs a sort).
        var ordered = entries
            .OrderBy(e => e.BlockHeight is null ? 0 : 1)
            .ThenByDescending(e => e.BlockHeight ?? int.MaxValue)
            .ToList();

        foreach (var e in ordered)
        {
            if (!seen.Add(e.Txid)) continue;
            result.Add(Classify(e, own));
        }
        return result;
    }

    private DigiDollarTransaction Classify(DdHistoryEntry e, HashSet<string> own)
    {
        var ownIn = e.DdInputs.Where(i => own.Contains(i.Address)).Sum(i => i.AmountCents);
        var ownOut = e.DdOutputs.Where(o => own.Contains(o.Address)).Sum(o => o.AmountCents);
        var status = e.Confirmations > 0 ? DdTxStatus.Confirmed : DdTxStatus.Pending;
        var timestamp = e.BlockTime ?? _now();

        if (ownIn == 0)
        {
            var from = e.DdInputs.Select(i => i.Address).FirstOrDefault(a => !own.Contains(a));
            return new DigiDollarTransaction(e.Txid, DdDirection.Received, from, ownOut, null, e.Confirmations, timestamp, status);
        }

        var toOthers = e.DdOutputs.Where(o => !own.Contains(o.Address)).ToList();
        if (toOthers.Count == 0)
            return new DigiDollarTransaction(e.Txid, DdDirection.SelfTransfer, null, ownOut, e.FeeSats, e.Confirmations, timestamp, status);

        return new DigiDollarTransaction(e.Txid, DdDirection.Sent, toOthers[0].Address, ownIn - ownOut, e.FeeSats, e.Confirmations, timestamp, status);
    }

    // ------------------------------------------------------------------ receive addresses

    public async Task<DigiDollarAddress> NewReceiveAddressAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            var c = Resolve() ?? throw new InvalidOperationException(
                "DigiDollar requires an unlocked wallet created from a recovery phrase.");
            var state = await LoadStateAsync(c);

            var taken = state.Addresses.Where(a => a.Chain == DigiDollarChain.Receive).Select(a => a.Index).ToHashSet();
            var index = state.Account.NextReceiveIndex;
            while (taken.Contains(index)) index++;

            var address = NewAddressRecord(c, DigiDollarChain.Receive, index);
            await SaveAsync(c, state with
            {
                Account = state.Account with { NextReceiveIndex = index + 1 },
                Addresses = state.Addresses.Append(address).OrderBy(a => a.Chain).ThenBy(a => a.Index).ToList(),
            });
            return address;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DigiDollarAddress>> GetReceiveAddressesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            var c = Resolve();
            if (c is null) return [];
            return ReceiveOf(await LoadStateAsync(c));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ sending (FR-011..FR-017, FR-026)

    private sealed record Prepared(DdSendCheck Check, DdTransferResult? Built, Ctx? Ctx, DigiDollarState? State, DigiDollarAddress? ChangeAddress);

    public async Task<DdSendCheck> CheckSendAsync(string recipient, long amountCents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try { return (await PrepareAsync(recipient, amountCents, ct)).Check; }
        finally { _gate.Release(); }
    }

    public async Task<long> GetSendAllAmountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            var c = Resolve();
            if (c is null) return 0;
            var state = await LoadStateAsync(c);
            var addresses = state.Addresses.Select(a => a.Address).Distinct(StringComparer.Ordinal).ToList();
            if (addresses.Count == 0) return 0;
            var (utxos, _) = await FetchUtxosOnlyAsync(addresses, ct);
            return SpendableUtxos(utxos, state).Sum(u => u.AmountCents);
        }
        catch (DdBackendUnavailableException)
        {
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(List<DigiDollarUtxo> Utxos, int _)> FetchUtxosOnlyAsync(IReadOnlyList<string> addresses, CancellationToken ct)
    {
        var utxos = new List<DigiDollarUtxo>();
        foreach (var chunk in addresses.Chunk(MaxAddressesPerRequest))
            foreach (var list in (await _api.GetUtxosAsync(chunk, 0, ct)).Values) utxos.AddRange(list);
        return (utxos, 0);
    }

    public async Task<DdSendResult> SendAsync(string recipient, long amountCents, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            // Revalidate with fresh data at the moment of consent; the quote the user saw may be stale.
            var p = await PrepareAsync(recipient, amountCents, ct);
            if (!p.Check.IsOk || p.Built is null || p.Ctx is null || p.State is null)
                throw new DdSendException(p.Check.Issue == DdSendIssue.None ? DdSendIssue.NotSupported : p.Check.Issue);

            var built = p.Built;
            string txid;
            try
            {
                txid = await _api.BroadcastAsync(built.Hex, ct);
            }
            catch (DdApiException ex)
            {
                throw new DdSendException(DdSendIssue.Rejected, ex.Code);
            }
            catch (DdBackendUnavailableException)
            {
                throw new DdSendException(DdSendIssue.Offline);
            }

            var now = _now();
            var pending = new PendingDdSend
            {
                Txid = string.IsNullOrEmpty(txid) ? built.Txid : txid,
                RawTxHex = built.Hex,
                AmountCents = amountCents,
                FeeSats = built.FeeSats,
                Recipient = recipient,
                SpentOutpoints = built.SpentOutpoints.Select(o => new DdOutpoint(o.Txid, o.Vout)).ToList(),
                SubmittedAt = now,
            };

            var state = p.State;
            var addressList = state.Addresses.ToList();
            var account = state.Account;
            if (built.DdChangeCents > 0 && p.ChangeAddress is not null)
            {
                addressList.Add(p.ChangeAddress);
                account = account with { NextChangeIndex = p.ChangeAddress.Index + 1 };
            }

            await SaveAsync(p.Ctx, state with
            {
                Account = account,
                Addresses = addressList.OrderBy(a => a.Chain).ThenBy(a => a.Index).ToList(),
                PendingSends = state.PendingSends.Append(pending).ToList(),
            });
            _limiter.Record(now);

            return new DdSendResult(pending.Txid, amountCents, built.FeeSats, built.DdChangeCents);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeFailedSendAsync(string txid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct);
        try
        {
            var c = Resolve();
            if (c is null) return;
            var state = await LoadStateAsync(c);
            var remaining = state.PendingSends.Where(p => !(p.Txid == txid && p.Status == DdTxStatus.Failed)).ToList();
            if (remaining.Count != state.PendingSends.Count)
                await SaveAsync(c, state with { PendingSends = remaining });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The one validation pipeline behind both <see cref="CheckSendAsync"/> and <see cref="SendAsync"/> (data-model:
    /// Draft to Confirming): address, limits, reachability, balance, change minimum, DGB fee, rate limit, then a real build.
    /// </summary>
    private async Task<Prepared> PrepareAsync(string recipient, long amountCents, CancellationToken ct)
    {
        static Prepared Fail(DdSendIssue issue, long available = 0, bool own = false, IReadOnlyList<DdDegradedCondition>? degraded = null) =>
            new(new DdSendCheck(issue, available, 0, 0, own, degraded ?? []), null, null, null, null);

        var c = Resolve();
        if (c is null) return Fail(DdSendIssue.NotSupported);
        TrackLimiterWallet(c.Session.WalletId);
        var network = c.Session.Network;

        // 1. address (FR-006)
        if (!DigiDollarAddressValidator.TryDecode(recipient, network, out _, out _))
            return Fail(DigiDollarAddressValidator.TryDecode(recipient, null, out _, out _) ? DdSendIssue.WrongNetwork : DdSendIssue.NotDdAddress);

        // 2. limits (FR-012)
        if (amountCents < DigiDollarTransferBuilder.MinAmountCents) return Fail(DdSendIssue.BelowMinimum);
        if (amountCents > DigiDollarTransferBuilder.MaxAmountCents) return Fail(DdSendIssue.AboveMaximum);

        var state = await LoadStateAsync(c);
        var own = state.Addresses.Any(a => string.Equals(a.Address, recipient, StringComparison.Ordinal));

        // 3. the network must be reachable and active (FR-003)
        var health = await _api.GetHealthAsync(ct);
        if (!health.Available) return Fail(DdSendIssue.Offline, own: own);
        if (!health.DeploymentActive) return Fail(DdSendIssue.NotActive, own: own);
        var degraded = DegradedConditions(health);

        // 4. balance (FR-011)
        var addresses = state.Addresses.Select(a => a.Address).Distinct(StringComparer.Ordinal).ToList();
        List<DigiDollarUtxo> utxos;
        try { utxos = addresses.Count == 0 ? [] : (await FetchUtxosOnlyAsync(addresses, ct)).Utxos; }
        catch (DdBackendUnavailableException) { return Fail(DdSendIssue.Offline, own: own, degraded: degraded); }

        var spendable = SpendableUtxos(utxos, state).ToList();
        var available = spendable.Sum(u => u.AmountCents);
        if (available < amountCents) return Fail(DdSendIssue.InsufficientDd, available, own, degraded);

        // 5. inputs without sub-minimum change (FR-012)
        var selection = DigiDollarCoinSelector.Select(spendable.Select(u => u.AmountCents).ToList(), amountCents);
        if (selection.Error is { } selectionError) return Fail(MapSelectionError(selectionError), available, own, degraded);

        var inputs = new List<DdInput>();
        foreach (var index in selection.Indexes)
        {
            var u = spendable[index];
            var record = state.Addresses.FirstOrDefault(a => a.Address == u.Address);
            if (record is null) return Fail(DdSendIssue.InsufficientDd, available, own, degraded); // not one of our addresses: never spend it
            inputs.Add(new DdInput(u.Txid, u.Vout, u.AmountCents, c.Hd.Derive(record.Chain, record.Index)));
        }

        // 6. DGB fee (FR-013)
        if (_fees is null) return Fail(DdSendIssue.InsufficientDgbFee, available, own, degraded);
        var reserved = ReservedOutpoints(state);
        var feeUtxos = (await _fees.GetFeeUtxosAsync(ct))
            .Where(f => !reserved.Contains(new DdOutpoint(f.Txid, (int)f.Vout)))
            .ToList();
        // Prefer confirmed DGB; fall back to unconfirmed only when confirmed cannot pay.
        var confirmedFees = feeUtxos.Where(f => f.Confirmations > 0).ToList();
        var feeInputs = (confirmedFees.Sum(f => f.ValueSats) >= DigiDollarTransferBuilder.MinFeeSats ? confirmedFees : feeUtxos)
            .Select(f => new DgbFeeInput(f.Txid, f.Vout, f.ValueSats, f.PrivateKey)).ToList();

        // 7. rate limit (FR-016): reported after the other checks so real problems show first
        var now = _now();
        if (_limiter.IsLimited(now)) return Fail(DdSendIssue.RateLimited, available, own, degraded);

        // 8. build, sign locally, and quote
        var changeNeeded = inputs.Sum(i => i.AmountCents) - amountCents > 0;
        var changeIndex = state.Account.NextChangeIndex;
        while (state.Addresses.Any(a => a.Chain == DigiDollarChain.Change && a.Index == changeIndex)) changeIndex++;
        var changeKey = changeNeeded ? c.Hd.Derive(DigiDollarChain.Change, changeIndex) : null;

        DdTransferResult built;
        try
        {
            built = DigiDollarTransferBuilder.Build(new DdTransferRequest(
                network, inputs, feeInputs, recipient, amountCents, changeKey,
                await _fees.GetDgbChangeScriptAsync(ct), FeeRateSatPerVByte));
        }
        catch (DdBuildException ex)
        {
            return Fail(MapBuildError(ex.Error), available, own, degraded);
        }

        var changeAddress = changeKey is null
            ? null
            : new DigiDollarAddress
            {
                WalletId = c.Session.WalletId,
                Address = c.Hd.GetAddress(changeKey),
                Chain = DigiDollarChain.Change,
                Index = changeIndex,
                CreatedAt = now,
            };

        return new Prepared(
            new DdSendCheck(DdSendIssue.None, available, built.FeeSats, built.DdChangeCents, own, degraded),
            built, c, state, changeAddress);
    }

    private static IReadOnlyList<DdDegradedCondition> DegradedConditions(NetworkHealth h)
    {
        var list = new List<DdDegradedCondition>();
        if (h.ErrActive) list.Add(DdDegradedCondition.Err);
        if (h.VolatilityFreeze) list.Add(DdDegradedCondition.VolatilityFreeze);
        if (h.IsOracleStale) list.Add(DdDegradedCondition.OracleStale);
        if (!h.OraclePriceAvailable) list.Add(DdDegradedCondition.PriceUnavailable);
        return list;
    }

    private static DdSendIssue MapSelectionError(DdBuildError e) => e switch
    {
        DdBuildError.ChangeBelowMinimum => DdSendIssue.ChangeBelowMinimum,
        DdBuildError.ChangeAboveMaximum => DdSendIssue.ChangeAboveMaximum,
        _ => DdSendIssue.InsufficientDd,
    };

    private static DdSendIssue MapBuildError(DdBuildError e) => e switch
    {
        DdBuildError.InvalidRecipient => DdSendIssue.NotDdAddress,
        DdBuildError.BelowMinimum => DdSendIssue.BelowMinimum,
        DdBuildError.AboveMaximum => DdSendIssue.AboveMaximum,
        DdBuildError.InsufficientDd => DdSendIssue.InsufficientDd,
        DdBuildError.ChangeBelowMinimum => DdSendIssue.ChangeBelowMinimum,
        DdBuildError.ChangeAboveMaximum => DdSendIssue.ChangeAboveMaximum,
        DdBuildError.InsufficientDgbFee => DdSendIssue.InsufficientDgbFee,
        _ => DdSendIssue.NotSupported,
    };
}

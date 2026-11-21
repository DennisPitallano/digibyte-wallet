using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Change-only history sync (spec 023): a refresh with no activity reads only summaries, new activity is fetched for the changed
/// addresses only, failures never lower the balance, the once-per-unlock full re-read, busy addresses, and pending amounts.
/// </summary>
public class HistorySyncTests
{
    private const string W = "w1";
    private const string N = "mainnet";
    private static readonly TimeSpan PastSnapshot = TimeSpan.FromSeconds(31);

    private static string A(int i) => $"dgb1qaddress{i:000}";
    private static List<string> Addresses(int count) => Enumerable.Range(0, count).Select(A).ToList();

    private static async Task SettleResync(HistorySyncEnv env, string walletId = W)
    {
        if (env.Sync.GetFullResyncTask(walletId, N) is { } task) await task;
    }

    // ═══ US1: a refresh with no new activity (T017) ═══

    [Fact]
    public async Task WarmRefresh_NoActivity_ReadsOnlySummaries()
    {
        var env = new HistorySyncEnv();
        var addresses = Addresses(80);
        env.Explorer.Pay(A(0), 5000);
        env.Explorer.Pay(A(7), 7000);
        var first = await env.Sync.RefreshAsync(W, N, addresses);

        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var second = await env.Sync.RefreshAsync(W, N, addresses);

        Assert.Equal(80, env.Explorer.SummaryRequests);
        Assert.Equal(0, env.Explorer.PageRequests);
        Assert.True(env.Explorer.TipRequests <= 1);
        Assert.Equal(12_000, second.ConfirmedSatoshis);
        Assert.Equal(first.Transactions.Select(t => t.TxId).Order(), second.Transactions.Select(t => t.TxId).Order());
        Assert.Equal(0, second.AddressesFetched);
    }

    [Fact]
    public async Task Service_WarmBalanceAndHistory_OneSummaryPerAddress_NoPages_OneTip()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var paid = env.Explorer.Pay(env.Receive(0), 5000);
        await service.GetBalanceAsync();
        await service.GetTransactionHistoryAsync();
        await SettleResync(env, service.ActiveWallet!.Id);

        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var balance = await service.GetBalanceAsync();
        var history = await service.GetTransactionHistoryAsync();

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.SummaryRequests);
        Assert.Equal(0, env.Explorer.PageRequests);
        Assert.Equal(0, env.Explorer.AddressTxRequests);
        Assert.Equal(0, env.Explorer.BalanceRequests);
        Assert.True(env.Explorer.TipRequests <= 1);
        Assert.Equal(5000, balance.ConfirmedSatoshis);
        Assert.Contains(history, r => r.TxId == paid.TxId);
    }

    [Fact]
    public async Task Service_BalanceAndHistoryAtTheSameTime_ReadEachSummaryOnce()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        await service.GetBalanceAsync();
        await service.GetTransactionHistoryAsync();
        await SettleResync(env, service.ActiveWallet!.Id);

        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        await Task.WhenAll(service.GetBalanceAsync(), service.GetTransactionHistoryAsync());

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.SummaryRequests);
        Assert.Equal(env.Explorer.SummaryAddresses.Count, env.Explorer.SummaryAddresses.Distinct().Count());
    }

    [Fact]
    public async Task Service_FirstRefreshAfterDiscovery_ReusesDiscoverysSummaries()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5000, balance.ConfirmedSatoshis);
        Assert.Equal(0, env.Explorer.HistoryLookups);   // discovery used summaries, not history lookups
        Assert.Equal(env.Explorer.SummaryAddresses.Count, env.Explorer.SummaryAddresses.Distinct().Count());
    }

    [Fact]
    public async Task Service_SwitchOff_ReadsEveryAddressInFull_AsBefore()
    {
        var env = new HistorySyncEnv();
        env.Options.ChangeOnly = false;
        var service = await env.CreateAsync();
        var paid = env.Explorer.Pay(env.Receive(0), 5000);

        var balance = await service.GetBalanceAsync();
        var history = await service.GetTransactionHistoryAsync();

        var watched = service.GetAllAddresses().Count;
        Assert.Equal(0, env.Explorer.SummaryRequests);
        Assert.Equal(watched, env.Explorer.BalanceRequests);
        Assert.Equal(watched, env.Explorer.AddressTxRequests);
        Assert.Equal(5000, balance.ConfirmedSatoshis);
        Assert.Contains(history, r => r.TxId == paid.TxId);
    }

    [Fact]
    public async Task Service_SummariesUnavailable_UsesTodaysPath_WithTheSameResults()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Unavailable = true;
        var service = await env.CreateAsync();
        var paid = env.Explorer.Pay(env.Receive(0), 5000);

        var balance = await service.GetBalanceAsync();
        var history = await service.GetTransactionHistoryAsync();

        Assert.Equal(5000, balance.ConfirmedSatoshis);
        Assert.Contains(history, r => r.TxId == paid.TxId);
        Assert.True(env.Explorer.AddressTxRequests > 0);
    }

    [Fact]
    public async Task SavedConfirmations_AdvanceWithTheTip_WithOneTipRequest()
    {
        var env = new HistorySyncEnv();
        var paid = env.Explorer.Pay(A(0), 5000, height: 900);
        var first = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(101, first.Transactions.Single(t => t.TxId == paid.TxId).Confirmations);

        env.Explorer.Tip = 1010;
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var second = await env.Sync.RefreshAsync(W, N, [A(0)]);

        Assert.Equal(111, second.Transactions.Single(t => t.TxId == paid.TxId).Confirmations);
        Assert.True(env.Explorer.TipRequests <= 1);
        Assert.Equal(0, env.Explorer.PageRequests);
    }

    // ═══ US2: new activity (T024) ═══

    [Fact]
    public async Task NewPayment_PagesOnlyThatAddress()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        await service.GetTransactionHistoryAsync();
        await SettleResync(env, service.ActiveWallet!.Id);

        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var payment = env.Explorer.Pay(env.Receive(3), 2500);
        var history = await service.GetTransactionHistoryAsync();
        var balance = await service.GetBalanceAsync();

        Assert.Equal([env.Receive(3)], env.Explorer.PagedAddresses);
        var record = history.Single(r => r.TxId == payment.TxId);
        Assert.Equal(TransactionDirection.Received, record.Direction);
        Assert.Equal(2500, record.AmountSatoshis);
        Assert.Equal(7500, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task PendingThatConfirms_IsNoticed_AndItsConfirmationsUpdate()
    {
        var env = new HistorySyncEnv();
        var tx = env.Explorer.Pay(A(0), 5000, height: null);
        var first = await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);
        Assert.Equal(0, first.Transactions.Single().Confirmations);

        env.Explorer.Confirm(tx, 1000);
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var second = await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);

        Assert.Equal([A(0)], env.Explorer.PagedAddresses);
        Assert.Equal(1, second.Transactions.Single().Confirmations);
        Assert.Equal(5000, second.ConfirmedSatoshis);
        Assert.Equal(0, second.PendingSatoshis);
    }

    [Fact]
    public async Task PendingThatIsDropped_LeavesHistoryAndThePendingSum()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        var tx = env.Explorer.Pay(A(0), 5000, height: null);
        var first = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(5000, first.PendingSatoshis);

        env.Explorer.Drop(tx);
        env.Clock.Advance(PastSnapshot);
        var second = await env.Sync.RefreshAsync(W, N, [A(0)]);

        Assert.DoesNotContain(second.Transactions, t => t.TxId == tx.TxId);
        Assert.Single(second.Transactions);
        Assert.Equal(0, second.PendingSatoshis);
    }

    [Fact]
    public async Task NewlyWatchedAddress_IsFetched()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, [A(0)]);

        var later = env.Explorer.Pay(A(1), 3000);
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var result = await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);

        Assert.Equal([A(1)], env.Explorer.PagedAddresses);
        Assert.Contains(result.Transactions, t => t.TxId == later.TxId);
    }

    [Fact]
    public async Task NewConfirmed_ArePagedUntilAPageHoldsAKnownOne()
    {
        var env = new HistorySyncEnv { };
        for (var h = 1; h <= 30; h++) env.Explorer.Pay(A(0), 100, height: h);
        await env.Sync.RefreshAsync(W, N, [A(0)]);
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        await SettleResync(env);
        Assert.Equal(30, (await env.Store.LoadHistoryAsync(W, N)).Transactions.Count);

        for (var h = 31; h <= 60; h++) env.Explorer.Pay(A(0), 100, height: h);
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var result = await env.Sync.RefreshAsync(W, N, [A(0)]);

        Assert.Equal(2, env.Explorer.PageRequests);
        Assert.Equal(60, result.Transactions.Count);
    }

    // ═══ US2: failures (T025) ═══

    [Fact]
    public async Task OneSummaryFailing_FailsTheRefresh_AndLeavesEverySavedSummaryAlone()
    {
        var env = new HistorySyncEnv();
        var addresses = Addresses(10);
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, addresses);
        var before = await env.Storage.GetAsync(AddressSummaryStore.SummaryKey(W, N));

        env.Explorer.Pay(A(1), 3000);
        env.Explorer.FailSummaryFor.Add(A(5));
        env.Clock.Advance(PastSnapshot);

        await Assert.ThrowsAsync<HttpRequestException>(() => env.Sync.RefreshAsync(W, N, addresses));
        Assert.Equal(before, await env.Storage.GetAsync(AddressSummaryStore.SummaryKey(W, N)));
    }

    [Fact]
    public async Task Service_OneSummaryFailing_FailsTheBalanceRead()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        env.Explorer.FailSummaryFor.Add(env.Receive(2));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetBalanceAsync());
    }

    [Fact]
    public async Task HistoryPageFailing_StillReturnsTheBalance_KeepsTheOldSummary_AndRetriesNextCycle()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        env.Explorer.Pay(A(1), 1000);
        await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);

        env.Explorer.Pay(A(0), 500);
        env.Explorer.Pay(A(1), 700);
        env.Explorer.FailPagesFor.Add(A(0));
        env.Clock.Advance(PastSnapshot);
        var result = await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);

        Assert.Equal(3200, result.ConfirmedSatoshis);
        var record = await env.Store.LoadSummariesAsync(W, N);
        Assert.Equal(1, record.Entries[A(0)].Summary.ConfirmedTxCount);
        Assert.Equal(2, record.Entries[A(1)].Summary.ConfirmedTxCount);

        env.Explorer.FailPagesFor.Clear();
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var retry = await env.Sync.RefreshAsync(W, N, [A(0), A(1)]);

        Assert.Equal([A(0)], env.Explorer.PagedAddresses);
        Assert.Equal(4, retry.Transactions.Count);
    }

    [Fact]
    public async Task AfterASend_TheSnapshotIsDropped_SoTheNextRefreshReflectsIt()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, [A(0)]);

        env.Explorer.Pay(A(0), 400, height: null);     // the send's change, seconds later
        var stale = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(0, stale.PendingSatoshis);          // within 30 s the snapshot is reused

        env.Sync.ForgetSnapshot(W);
        var fresh = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(400, fresh.PendingSatoshis);
    }

    // ═══ US2: background full re-read (T026) ═══

    [Fact]
    public async Task FullResync_StartsOnlyAfterTheFirstChangeOnlyHistoryResult()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var id = service.ActiveWallet!.Id;
        env.Explorer.Pay(env.Receive(0), 5000);

        await service.GetBalanceAsync();
        Assert.Equal(FullResyncState.NotStarted, env.Sync.GetFullResyncState(id, N));

        await service.GetTransactionHistoryAsync();
        Assert.NotEqual(FullResyncState.NotStarted, env.Sync.GetFullResyncState(id, N));
    }

    [Fact]
    public async Task FullResync_WaitsWhileARefreshRuns()
    {
        var env = new HistorySyncEnv();
        var addresses = Addresses(4);
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, addresses);
        env.Reload();

        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;
        env.Explorer.ResetCounts();
        var refresh = env.Sync.RefreshAsync(W, N, addresses);
        env.Sync.EnsureFullResync(W, N, addresses);
        await Task.Delay(100);

        Assert.Equal(0, env.Explorer.PageRequests);
        env.Explorer.Hold = null;
        hold.SetResult();
        await refresh;
        await SettleResync(env);
        Assert.Equal(FullResyncState.Done, env.Sync.GetFullResyncState(W, N));
    }

    [Fact]
    public async Task FullResync_UsesAtMostTwoRequestsAtATime()
    {
        var env = new HistorySyncEnv();
        var addresses = Addresses(6);
        foreach (var a in addresses) env.Explorer.Pay(a, 1000);
        await env.Sync.RefreshAsync(W, N, addresses);
        env.Reload();
        await env.Sync.RefreshAsync(W, N, addresses);

        env.Explorer.Delay = TimeSpan.FromMilliseconds(30);
        env.Explorer.ResetCounts();
        env.Sync.EnsureFullResync(W, N, addresses);
        await SettleResync(env);

        Assert.Equal(6, env.Explorer.PageRequests);
        Assert.True(env.Explorer.MaxPagesInFlight <= 2, $"{env.Explorer.MaxPagesInFlight} page requests ran at once");
    }

    [Fact]
    public async Task FullResync_CorrectsATransactionThatMovedBlock_WithNoCountChange()
    {
        var env = new HistorySyncEnv();
        var tx = env.Explorer.Pay(A(0), 1000, height: 900);
        await env.Sync.RefreshAsync(W, N, [A(0)]);
        env.Reload();

        env.Explorer.Confirm(tx, 950);                    // reorganisation: same counts and amounts
        var before = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(101, before.Transactions.Single().Confirmations);

        env.Sync.EnsureFullResync(W, N, [A(0)]);
        await SettleResync(env);
        env.Clock.Advance(PastSnapshot);
        var after = await env.Sync.RefreshAsync(W, N, [A(0)]);

        Assert.Equal(51, after.Transactions.Single().Confirmations);
        Assert.Equal(950, (await env.Store.LoadHistoryAsync(W, N)).Transactions[tx.TxId].BlockHeight);
    }

    [Fact]
    public async Task FullResync_Failing_ChangesNothing_AndRetriesOnlyAfterAMinute()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, [A(0)]);
        env.Reload();
        var shown = await env.Sync.RefreshAsync(W, N, [A(0)]);

        env.Explorer.FailPagesFor.Add(A(0));
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        var failed = env.Sync.GetFullResyncTask(W, N)!;
        await failed;
        Assert.Equal(FullResyncState.NotStarted, env.Sync.GetFullResyncState(W, N));
        Assert.Equal(shown.Transactions.Select(t => t.TxId), (await env.Store.LoadHistoryAsync(W, N)).Transactions.Keys);

        env.Sync.EnsureFullResync(W, N, [A(0)]);
        Assert.Same(failed, env.Sync.GetFullResyncTask(W, N));

        env.Explorer.FailPagesFor.Clear();
        env.Clock.Advance(TimeSpan.FromSeconds(61));
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        await SettleResync(env);
        Assert.Equal(FullResyncState.Done, env.Sync.GetFullResyncState(W, N));
    }

    [Fact]
    public async Task FullResync_RunsOncePerUnlock_AndLockingCancelsIt()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Pay(A(0), 1000);
        await env.Sync.RefreshAsync(W, N, [A(0)]);
        env.Reload();
        await env.Sync.RefreshAsync(W, N, [A(0)]);

        env.Sync.EnsureFullResync(W, N, [A(0)]);
        var first = env.Sync.GetFullResyncTask(W, N)!;
        await first;
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        Assert.Same(first, env.Sync.GetFullResyncTask(W, N));

        // A new unlock runs it again; locking while it runs cancels it.
        env.Sync.ResetSession(W);
        var hold = new TaskCompletionSource();
        env.Explorer.Hold = hold.Task;
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        var second = env.Sync.GetFullResyncTask(W, N)!;
        Assert.NotSame(first, second);
        env.Sync.ResetAll();
        hold.SetResult();
        await second;
        Assert.Equal(FullResyncState.NotStarted, env.Sync.GetFullResyncState(W, N));
    }

    [Fact]
    public async Task FullResync_EveryAddressAlreadyReadInFullThisSession_GoesStraightToDone()
    {
        var env = new HistorySyncEnv();
        var addresses = Addresses(5);
        env.Explorer.Pay(A(0), 1000);
        env.Explorer.Pay(A(3), 1000);
        await env.Sync.RefreshAsync(W, N, addresses);

        env.Explorer.ResetCounts();
        env.Sync.EnsureFullResync(W, N, addresses);

        Assert.Equal(FullResyncState.Done, env.Sync.GetFullResyncState(W, N));
        Assert.Null(env.Sync.GetFullResyncTask(W, N));
        Assert.Equal(0, env.Explorer.PageRequests);
    }

    // ═══ US3: busy addresses (T031) ═══

    [Fact]
    public async Task BusyAddress_FirstRefreshTakesTheNewestPage_TheFullReReadCompletesIt()
    {
        var env = new HistorySyncEnv();
        for (var h = 1; h <= 60; h++) env.Explorer.Pay(A(0), 100, height: h);

        var first = await env.Sync.RefreshAsync(W, N, [A(0)]);
        Assert.Equal(25, first.Transactions.Count);
        Assert.Equal(1, env.Explorer.PageRequests);

        env.Sync.EnsureFullResync(W, N, [A(0)]);
        await SettleResync(env);
        Assert.Equal(60, (await env.Store.LoadHistoryAsync(W, N)).Transactions.Count);
        Assert.True((await env.Store.LoadSummariesAsync(W, N)).Entries[A(0)].Cursor.Complete);

        env.Explorer.Pay(A(0), 100, height: 61);
        env.Clock.Advance(PastSnapshot);
        env.Explorer.ResetCounts();
        var later = await env.Sync.RefreshAsync(W, N, [A(0)]);

        Assert.Equal(1, env.Explorer.PageRequests);
        Assert.Equal(61, later.Transactions.Count);
    }

    [Fact]
    public async Task BusyAddress_PastTheCap_IsMarkedTruncated()
    {
        var env = new HistorySyncEnv();
        env.Explorer.Tip = 5000;
        for (var h = 1; h <= WalletHistorySync.MaxTransactionsPerAddress + 10; h++) env.Explorer.Pay(A(0), 1, height: h);

        await env.Sync.RefreshAsync(W, N, [A(0)]);
        env.Sync.EnsureFullResync(W, N, [A(0)]);
        await SettleResync(env);

        var cursor = (await env.Store.LoadSummariesAsync(W, N)).Entries[A(0)].Cursor;
        Assert.True(cursor.Truncated);
        Assert.False(cursor.Complete);
        Assert.Equal(WalletHistorySync.MaxTransactionsPerAddress, (await env.Store.LoadHistoryAsync(W, N)).Transactions.Count);
    }

    // ═══ US4: pending amounts (T035) ═══

    [Fact]
    public async Task PendingIncoming_ShowsAsPending_AndIsNotAvailable()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        env.Explorer.Pay(env.Receive(1), 2000, height: null);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5000, balance.ConfirmedSatoshis);
        Assert.Equal(2000, balance.UnconfirmedSatoshis);
        Assert.Equal(5000, balance.AvailableSatoshis);
    }

    [Fact]
    public async Task PendingOutgoing_IsNegative_AndAvailableExcludesWhatItSpent()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        env.Explorer.Spend(env.Receive(0), inputSats: 5000, sats: 1000, fee: 100, change: env.Change(0));

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5000, balance.ConfirmedSatoshis);              // unchanged until the send confirms
        Assert.Equal(-1100, balance.UnconfirmedSatoshis);
        Assert.Equal(5000 - 1000 - 100, balance.AvailableSatoshis);
    }

    [Fact]
    public async Task PendingThatConfirms_MovesIntoConfirmed()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var tx = env.Explorer.Pay(env.Receive(0), 2000, height: null);
        Assert.Equal(2000, (await service.GetBalanceAsync()).UnconfirmedSatoshis);

        env.Explorer.Confirm(tx, 1000);
        env.Clock.Advance(PastSnapshot);
        var balance = await service.GetBalanceAsync();

        Assert.Equal(0, balance.UnconfirmedSatoshis);
        Assert.Equal(2000, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task FullReadPath_ReportsNoPending()
    {
        var env = new HistorySyncEnv();
        env.Options.ChangeOnly = false;
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        env.Explorer.Pay(env.Receive(1), 2000, height: null);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(0, balance.UnconfirmedSatoshis);
        Assert.Equal(5000, balance.AvailableSatoshis);
    }
}

using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T019/T020 (User Story 1): hold and receive. Balances, addresses, history, the restore scan (research R12),
/// and the unavailable / cached / not-active states (FR-003, research R14, R16).
/// </summary>
public class DigiDollarServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Env
    {
        public TestMemoryStorage Storage { get; } = new();
        public FakeDdNodeApi Api { get; } = new();
        public FakeSessionProvider Sessions { get; }
        public DigiDollarStore Store { get; }
        public DigiDollarKeyDerivation Hd => Sessions.Session!.Derivation!;
        public DateTimeOffset Clock { get; set; } = Now;
        public DigiDollarService Service { get; }

        public Env(FakeSessionProvider? sessions = null)
        {
            Sessions = sessions ?? FakeSessionProvider.Seed();
            Store = new DigiDollarStore(Storage, new TestAesCrypto());
            Service = new DigiDollarService(Sessions, Api, Store, clock: () => Clock);
        }

        public string Addr(DigiDollarChain chain, int index) => Hd.GetAddress(Hd.Derive(chain, index));
        public string Recv(int index) => Addr(DigiDollarChain.Receive, index);
        public string Chg(int index) => Addr(DigiDollarChain.Change, index);

        public string Secret() => DigiDollarStore.DeriveStoreSecret(Hd.StorageKeyMaterial());

        public Task<DigiDollarState?> Saved(string walletId = "w1") => Store.LoadAsync(walletId, Secret());

        public void Fund(string address, long cents, int confirmations, string txid, bool spentByMempool = false)
        {
            if (!Api.Utxos.TryGetValue(address, out var list)) Api.Utxos[address] = list = [];
            list.Add(new DigiDollarUtxo(txid, 0, address, cents, "5120" + new string('a', 64), 0, confirmations, spentByMempool));
        }
    }

    private static DdHistoryEntry Entry(string txid, int conf, (string, long)[] inputs, (string, long)[] outputs,
        long? fee = null, DateTimeOffset? time = null) =>
        new(txid, conf > 0 ? 900 : null, conf > 0 ? time ?? Now.AddHours(-1) : null, conf, fee,
            inputs.Select(i => new DdAmountAt(i.Item1, i.Item2)).ToList(),
            outputs.Select(o => new DdAmountAt(o.Item1, o.Item2)).ToList());

    private const string External = "TDexternalexternalexternalexternalexternal00";

    // ---------- wallet support ----------

    [Fact]
    public async Task NoSession_IsNotSupported()
    {
        var env = new Env(new FakeSessionProvider());
        var d = await env.Service.RefreshAsync();
        Assert.Equal(DdViewState.NotSupported, d.State);
        Assert.Equal(0, env.Api.DataCalls);
    }

    [Theory]
    [InlineData(DdWalletKind.PrivateKey)]
    [InlineData(DdWalletKind.WatchOnly)]
    [InlineData(DdWalletKind.Multisig)]
    public async Task NonSeedWallets_AreNotSupported_AndNeverTouchTheBackend(DdWalletKind kind)
    {
        var env = new Env(FakeSessionProvider.Kind(kind));
        var d = await env.Service.RefreshAsync();
        Assert.Equal(DdViewState.NotSupported, d.State);
        Assert.Equal(0, env.Api.DataCalls);
        Assert.Empty(d.Addresses);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.NewReceiveAddressAsync());
    }

    // ---------- first open and the restore scan (FR-007, R12) ----------

    [Fact]
    public async Task FirstOpen_NothingOnChain_IsEmpty_ScanCompletes_AndMarksTheDeviceAsUsed()
    {
        var env = new Env();

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Empty, d.State);
        Assert.True(d.ScanComplete);
        Assert.Empty(d.Addresses);
        Assert.Equal(0, d.Balance!.ConfirmedCents);
        Assert.True(await env.Store.HasUsedAsync("w1"));
        var saved = await env.Saved();
        Assert.True(saved!.Account.ScanComplete);
        Assert.Equal(Now, saved.Account.LastScanAt);
        Assert.Equal(DdNetwork.Testnet, saved.Account.Network);
    }

    [Fact]
    public async Task Scan_FindsActivity_WithinTheGapLimit_AndRebuildsAddressesAndBalance()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 2500, 10, "t0");
        env.Fund(env.Recv(7), 500, 2, "t7");
        env.Fund(env.Chg(0), 100, 5, "c0");
        env.Api.History.Add(Entry("hb", 2, [(External, 500)], [(env.Recv(7), 500)]));
        env.Api.History.Add(Entry("ha", 10, [(External, 2500)], [(env.Recv(0), 2500)]));
        env.Api.History.Add(Entry("hc", 5, [(External, 100)], [(env.Chg(0), 100)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Ready, d.State);
        Assert.Equal(3100, d.Balance!.ConfirmedCents);
        var saved = await env.Saved();
        Assert.Equal(8, saved!.Addresses.Count(a => a.Chain == DigiDollarChain.Receive)); // 0..7
        Assert.Equal(1, saved.Addresses.Count(a => a.Chain == DigiDollarChain.Change));   // 0
        Assert.Equal(8, saved.Account.NextReceiveIndex);
        Assert.Equal(1, saved.Account.NextChangeIndex);
        Assert.Equal(env.Recv(0), saved.Addresses.Single(a => a.Chain == DigiDollarChain.Receive && a.Index == 0).Address);
        Assert.Equal(8, d.Addresses.Count); // the dashboard lists receive addresses only
    }

    [Fact]
    public async Task Scan_StopsAfter20ConsecutiveUnusedAddresses()
    {
        var env = new Env();
        env.Fund(env.Recv(25), 900, 3, "far"); // beyond the gap from index -1: must NOT be found
        env.Api.History.Add(Entry("hf", 3, [(External, 900)], [(env.Recv(25), 900)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Empty, d.State);
        Assert.Equal(0, (await env.Saved())!.Account.NextReceiveIndex);
    }

    [Fact]
    public async Task Scan_KeepsGoing_WhileActivityKeepsExtendingTheWindow()
    {
        var env = new Env();
        foreach (var i in new[] { 19, 35 })
        {
            env.Fund(env.Recv(i), 100 + i, 3, "t" + i);
            env.Api.History.Add(Entry("h" + i, 3, [(External, 100 + i)], [(env.Recv(i), 100 + i)]));
        }

        await env.Service.RefreshAsync();

        var saved = await env.Saved();
        Assert.Equal(36, saved!.Account.NextReceiveIndex); // 35 is the last active; 36..55 are unused
        Assert.Equal(36, saved.Addresses.Count(a => a.Chain == DigiDollarChain.Receive));
    }

    [Fact]
    public async Task Scan_AddressesAreSentInBatchesAtMost200()
    {
        var env = new Env();

        await env.Service.RefreshAsync();

        Assert.All(env.Api.HistoryBatchSizes.Concat(env.Api.UtxoBatchSizes), n => Assert.InRange(n, 1, 200));
        Assert.Equal(40, env.Api.HistoryBatchSizes[0]); // 20 receive + 20 change in the first round
    }

    [Fact]
    public async Task Scan_ActivityIncludesAddressesThatAreFullySpent()
    {
        var env = new Env();
        // Recv(3) received and then fully spent: no UTXO, but history shows it was used.
        env.Api.History.Add(Entry("spend", 8, [(env.Recv(3), 400)], [(External, 400)]));
        env.Api.History.Add(Entry("fund", 9, [(External, 400)], [(env.Recv(3), 400)]));

        await env.Service.RefreshAsync();

        Assert.Equal(4, (await env.Saved())!.Account.NextReceiveIndex);
    }

    [Fact]
    public async Task Scan_Failure_MidWay_IsNotMarkedComplete_AndTheNextRefreshRetries()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 700, 4, "t0");
        env.Api.History.Add(Entry("h0", 4, [(External, 700)], [(env.Recv(0), 700)]));
        env.Api.FailAfterDataCalls = 0; // the very first data call fails

        var failed = await env.Service.RefreshAsync();

        Assert.NotEqual(DdViewState.Ready, failed.State);
        Assert.False((await env.Saved())!.Account.ScanComplete);

        env.Api.FailAfterDataCalls = null;
        var ok = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Ready, ok.State);
        Assert.True((await env.Saved())!.Account.ScanComplete);
        Assert.Equal(700, ok.Balance!.ConfirmedCents);
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        var env = new Env();
        var reports = new List<DdScanProgress>();

        await env.Service.RefreshAsync(progress: new SyncProgress(reports));

        Assert.NotEmpty(reports);
        Assert.True(reports[^1].AddressesChecked >= 40);
    }

    private sealed class SyncProgress(List<DdScanProgress> sink) : IProgress<DdScanProgress>
    {
        public void Report(DdScanProgress value) => sink.Add(value);
    }

    [Fact]
    public async Task SecondRefresh_DoesNotScanAgain()
    {
        var env = new Env();
        await env.Service.RefreshAsync();
        var callsAfterFirst = env.Api.HistoryBatchSizes.Count;

        await env.Service.RefreshAsync();

        Assert.Equal(callsAfterFirst, env.Api.HistoryBatchSizes.Count); // no addresses known => no further calls at all
    }

    // ---------- availability states (FR-003, R14, R16) ----------

    [Fact]
    public async Task BackendDown_WithNoCache_IsUnavailable()
    {
        var env = new Env();
        env.Api.Down = true;

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Unavailable, d.State);
        Assert.False(d.Health.Available);
        Assert.Null(d.Balance);
    }

    [Fact]
    public async Task BackendDown_WithACache_ShowsCachedBalanceAndHistory_LabeledWithItsAge()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 2500, 10, "t0");
        env.Api.History.Add(Entry("ha", 10, [(External, 2500)], [(env.Recv(0), 2500)]));
        await env.Service.RefreshAsync(); // good refresh populates the cache

        env.Clock = Now.AddMinutes(30);
        env.Api.Down = true;
        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.UnavailableCached, d.State);
        Assert.True(d.Cached);
        Assert.Equal(Now, d.CachedAsOf);
        Assert.Equal(2500, d.Balance!.ConfirmedCents);
        Assert.Single(d.History);
    }

    [Fact]
    public async Task DataCallsFailing_AfterAGoodHealthCheck_FallBackToTheCache()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 100, 10, "t0");
        env.Api.History.Add(Entry("ha", 10, [(External, 100)], [(env.Recv(0), 100)]));
        await env.Service.RefreshAsync();

        env.Api.FailAfterDataCalls = env.Api.DataCalls; // the next data call fails; health still answers
        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.UnavailableCached, d.State);
        Assert.Equal(100, d.Balance!.ConfirmedCents);
    }

    [Fact]
    public async Task DeploymentNotActive_IsNotActive_AndMakesNoDataCalls()
    {
        var env = new Env();
        env.Api.Health = env.Api.Health with { DeploymentActive = false };

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.NotActive, d.State);
        Assert.Equal(0, env.Api.DataCalls);
        Assert.Empty(env.Api.Broadcasts);
    }

    [Fact]
    public async Task NoOraclePrice_StillWorks_AndIsReportedInTheHealth()
    {
        var env = new Env();
        env.Api.Health = env.Api.Health with { OraclePriceMicroUsd = null, OracleHeight = null };

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DdViewState.Empty, d.State);
        Assert.False(d.Health.OraclePriceAvailable);
        Assert.True(d.Health.IsDegraded);
    }

    // ---------- balances (FR-004) ----------

    [Fact]
    public async Task Balance_ConfirmedAndPending_AreSeparate_WithAPerAddressMap()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 2500, 10, "t0");
        env.Fund(env.Recv(1), 300, 1, "t1");
        env.Fund(env.Recv(1), 700, 0, "t2"); // unconfirmed incoming
        env.Api.History.Add(Entry("h2", 0, [(External, 700)], [(env.Recv(1), 700)]));
        env.Api.History.Add(Entry("h1", 1, [(External, 300)], [(env.Recv(1), 300)]));
        env.Api.History.Add(Entry("h0", 10, [(External, 2500)], [(env.Recv(0), 2500)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(2800, d.Balance!.ConfirmedCents);
        Assert.Equal(700, d.Balance.PendingCents);
        Assert.Equal(2500, d.Balance.PerAddress[env.Recv(0)]);
        Assert.Equal(1000, d.Balance.PerAddress[env.Recv(1)]);
        Assert.Equal(1000, d.Balance.AsOfHeight); // node tip from health
    }

    [Fact]
    public async Task Balance_AnOutputSpentByAPendingTx_CountsAsNegativePending()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 2500, 10, "t0", spentByMempool: true);
        env.Fund(env.Recv(0), 1500, 0, "change"); // our own unconfirmed change
        env.Api.History.Add(Entry("send", 0, [(env.Recv(0), 2500)], [(External, 1000), (env.Recv(0), 1500)]));
        env.Api.History.Add(Entry("fund", 10, [(External, 2500)], [(env.Recv(0), 2500)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(2500, d.Balance!.ConfirmedCents);
        Assert.Equal(-1000, d.Balance.PendingCents); // +1500 incoming change, -2500 spent
        Assert.Equal(1500, d.Balance.PerAddress[env.Recv(0)]); // effective: only the unspent output counts
    }

    [Fact]
    public async Task Available_IsConfirmed_AndNotAlreadySpentOrReserved()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 1000, 10, "a");
        env.Fund(env.Recv(0), 2000, 10, "b", spentByMempool: true);
        env.Fund(env.Recv(0), 4000, 0, "c");
        foreach (var t in new[] { ("a", 10, 1000), ("b", 10, 2000), ("c", 0, 4000) })
            env.Api.History.Add(Entry("h" + t.Item1, t.Item2, [(External, t.Item3)], [(env.Recv(0), t.Item3)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(1000, d.AvailableCents);
    }

    // ---------- history (FR-009) ----------

    [Fact]
    public async Task History_ClassifiesReceivedSentAndSelfTransfer()
    {
        var env = new Env();
        var me = env.Recv(0);
        var me2 = env.Recv(1);
        env.Api.History.Add(Entry("self", 3, [(me, 600)], [(me2, 600)], fee: 10_000_000));
        env.Api.History.Add(Entry("sent", 4, [(me, 2500)], [(External, 1000), (me, 1500)], fee: 10_000_000));
        env.Api.History.Add(Entry("recv", 5, [(External, 2500)], [(me, 2500)]));
        env.Fund(me, 100, 3, "x"); // make sure the scan sees address 0 and 1 as used through the history anyway

        var d = await env.Service.RefreshAsync();

        var byId = d.History.ToDictionary(t => t.Txid);
        var recv = byId["recv"];
        Assert.Equal(DdDirection.Received, recv.Direction);
        Assert.Equal(2500, recv.AmountCents);
        Assert.Equal(External, recv.CounterpartyAddress);
        Assert.Null(recv.FeeSats);
        Assert.Equal(DdTxStatus.Confirmed, recv.Status);

        var sent = byId["sent"];
        Assert.Equal(DdDirection.Sent, sent.Direction);
        Assert.Equal(1000, sent.AmountCents); // net to others: 2500 in - 1500 change
        Assert.Equal(External, sent.CounterpartyAddress);
        Assert.Equal(10_000_000, sent.FeeSats);

        var self = byId["self"];
        Assert.Equal(DdDirection.SelfTransfer, self.Direction);
        Assert.Equal(600, self.AmountCents);
    }

    [Fact]
    public async Task History_Unconfirmed_IsPending_WithAConfirmationCount()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 50, 0, "t");
        env.Api.History.Add(Entry("p", 0, [(External, 50)], [(env.Recv(0), 50)]));

        var d = await env.Service.RefreshAsync();

        var t = Assert.Single(d.History);
        Assert.Equal(DdTxStatus.Pending, t.Status);
        Assert.Equal(0, t.Confirmations);
        Assert.Equal(Now, t.Timestamp); // local time while pending
    }

    [Fact]
    public async Task Cache_KeepsAtMost50HistoryEntries()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 10, 5, "t");
        for (var i = 0; i < 80; i++)
            env.Api.History.Add(Entry("h" + i, 5, [(External, 10)], [(env.Recv(0), 10)]));

        var d = await env.Service.RefreshAsync();

        Assert.Equal(DigiDollarCache.MaxHistory, d.History.Count);
        var saved = await env.Saved();
        Assert.Equal(DigiDollarCache.MaxHistory, saved!.Cache!.RecentHistory.Count);
        Assert.Equal(Now, saved.Cache.AsOf);
    }

    // ---------- receive addresses (FR-005, FR-007) ----------

    [Fact]
    public async Task NewReceiveAddress_IsDerivedLocally_ValidForTheNetwork_AndAdvancesTheIndex()
    {
        var env = new Env();

        var a0 = await env.Service.NewReceiveAddressAsync();
        var a1 = await env.Service.NewReceiveAddressAsync();

        Assert.Equal(env.Recv(0), a0.Address);
        Assert.Equal(env.Recv(1), a1.Address);
        Assert.Equal([0, 1], new[] { a0.Index, a1.Index });
        Assert.All(new[] { a0, a1 }, a => Assert.Equal(DigiDollarChain.Receive, a.Chain));
        Assert.True(DigiByte.Crypto.Addresses.DigiDollarAddressValidator.IsValid(a0.Address, "testnet"));
        Assert.StartsWith("TD", a0.Address);
        Assert.Equal(0, env.Api.DataCalls); // purely local
        Assert.Equal(2, (await env.Saved())!.Account.NextReceiveIndex);
        Assert.True(await env.Store.HasUsedAsync("w1"));
    }

    [Fact]
    public async Task NewReceiveAddress_Persists_AndIsListedAfterAReload()
    {
        var env = new Env();
        await env.Service.NewReceiveAddressAsync();
        await env.Service.NewReceiveAddressAsync();

        var fresh = new DigiDollarService(env.Sessions, env.Api, env.Store, clock: () => Now);
        var listed = await fresh.GetReceiveAddressesAsync();

        Assert.Equal([env.Recv(0), env.Recv(1)], listed.Select(a => a.Address));
    }

    [Fact]
    public async Task NewReceiveAddress_ContinuesAfterTheScan_AndSkipsKnownIndexes()
    {
        var env = new Env();
        env.Fund(env.Recv(3), 100, 3, "t3");
        env.Api.History.Add(Entry("h3", 3, [(External, 100)], [(env.Recv(3), 100)]));
        await env.Service.RefreshAsync(); // finds 0..3

        var next = await env.Service.NewReceiveAddressAsync();

        Assert.Equal(4, next.Index);
        Assert.Equal(env.Recv(4), next.Address);
    }

    [Fact]
    public async Task ReceiveList_ExcludesChange_AndShowsPerAddressBalancesViaTheDashboard()
    {
        var env = new Env();
        env.Fund(env.Recv(0), 100, 3, "t0");
        env.Fund(env.Chg(0), 40, 3, "c0");
        env.Api.History.Add(Entry("h0", 3, [(External, 100)], [(env.Recv(0), 100)]));
        env.Api.History.Add(Entry("hc", 3, [(External, 40)], [(env.Chg(0), 40)]));

        var d = await env.Service.RefreshAsync();
        var listed = await env.Service.GetReceiveAddressesAsync();

        Assert.DoesNotContain(listed, a => a.Chain == DigiDollarChain.Change);
        Assert.Equal(env.Recv(0), Assert.Single(listed).Address);
        Assert.Equal(100, d.Balance!.PerAddress[env.Recv(0)]);
        Assert.Equal(40, d.Balance.PerAddress[env.Chg(0)]);
    }

    [Fact]
    public async Task NewReceiveAddress_WorksWhileTheBackendIsDown()
    {
        var env = new Env();
        env.Api.Down = true;

        var a = await env.Service.NewReceiveAddressAsync();

        Assert.Equal(env.Recv(0), a.Address);
    }

    // ---------- isolation and cancellation ----------

    [Fact]
    public async Task Wallets_HaveSeparateState()
    {
        var env = new Env(FakeSessionProvider.Seed("w1"));
        await env.Service.NewReceiveAddressAsync();
        await env.Service.NewReceiveAddressAsync();

        env.Sessions.Session = FakeSessionProvider.Seed("w2", passphrase: "other").Session;
        var listed = await env.Service.GetReceiveAddressesAsync();

        Assert.Empty(listed);
        Assert.Equal(0, (await env.Service.NewReceiveAddressAsync()).Index);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var env = new Env();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Service.RefreshAsync(cts.Token));
    }

    [Fact]
    public async Task StoredState_IsEncrypted_NoAddressesInPlaintext()
    {
        var env = new Env();
        var a = await env.Service.NewReceiveAddressAsync();

        var raw = await env.Storage.GetAsync("dd.state.w1");

        Assert.NotNull(raw);
        Assert.DoesNotContain(a.Address, raw!);
        Assert.Equal("1", await env.Storage.GetAsync("dd.used.w1"));
    }
}

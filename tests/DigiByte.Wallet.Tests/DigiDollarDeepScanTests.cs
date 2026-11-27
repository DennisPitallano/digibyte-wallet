using System.Text.Json;
using System.Text.Json.Serialization;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// specs/035 T037 (User Story 3, contracts/wallet-dd-key.md W3, FR-023): the deeper DigiDollar search. DigiPay can hand out
/// many DD addresses that are never paid, so DD paid far along the receive chain must still be found, and kept.
/// </summary>
public class DigiDollarDeepScanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private const string External = "TDexternalexternalexternalexternalexternal00";

    private sealed class Env
    {
        public TestMemoryStorage Storage { get; } = new();
        public FakeDdNodeApi Api { get; } = new();
        public FakeSessionProvider Sessions { get; }
        public DigiDollarStore Store { get; }
        public DigiDollarService Service { get; }
        public DigiDollarKeyDerivation Hd => Sessions.Session!.Derivation!;

        public Env(FakeSessionProvider? sessions = null)
        {
            Sessions = sessions ?? FakeSessionProvider.Seed();
            Store = new DigiDollarStore(Storage, new TestAesCrypto());
            Service = new DigiDollarService(Sessions, Api, Store, clock: () => Now);
        }

        public string Recv(int index) => Hd.GetAddress(Hd.Derive(DigiDollarChain.Receive, index));

        public Task<DigiDollarState?> Saved() =>
            Store.LoadAsync("w1", DigiDollarStore.DeriveStoreSecret(Hd.StorageKeyMaterial()));

        /// <summary>DD paid to receive address <paramref name="index"/>: a confirmed UTXO and its history entry.</summary>
        public void PayReceive(int index, long cents)
        {
            var address = Recv(index);
            Api.Utxos[address] = [new DigiDollarUtxo("t" + index, 0, address, cents, "5120" + new string('a', 64), 0, 3)];
            Api.History.Add(new DdHistoryEntry("t" + index, 900, Now.AddHours(-1), 3, null,
                [new DdAmountAt(External, cents)], [new DdAmountAt(address, cents)]));
        }
    }

    private sealed class SyncProgress(List<DdScanProgress> sink) : IProgress<DdScanProgress>
    {
        public void Report(DdScanProgress value) => sink.Add(value);
    }

    [Fact]
    public void TheDeepGap_Is500_AndTheNormalGap_Is20()
    {
        Assert.Equal(20, DigiDollarService.GapLimit);
        Assert.Equal(500, DigiDollarService.DeepScanGapLimit);
    }

    [Fact]
    public async Task DdPaidOnlyAtReceiveIndex300_IsMissedByANormalScan_AndFoundByTheDeepScan()
    {
        var env = new Env();
        env.PayReceive(300, 900);

        var first = await env.Service.RefreshAsync();
        Assert.Equal(DdViewState.Empty, first.State);

        var normal = await env.Service.ScanAsync();
        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 0), normal);
        Assert.DoesNotContain((await env.Saved())!.Addresses, a => a.Address == env.Recv(300));

        var deep = await env.Service.ScanAsync(receiveGapLimit: DigiDollarService.DeepScanGapLimit);

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 1), deep);
        var saved = (await env.Saved())!;
        Assert.Contains(saved.Addresses, a => a.Chain == DigiDollarChain.Receive && a.Index == 300 && a.Address == env.Recv(300));
        Assert.Equal(301, saved.Account.NextReceiveIndex);
        Assert.Equal(300, saved.Account.HighestUsedReceiveIndex);

        // The balance picks it up on the next refresh.
        var after = await env.Service.RefreshAsync();
        Assert.Equal(DdViewState.Ready, after.State);
        Assert.Equal(900, after.Balance!.ConfirmedCents);
    }

    [Fact]
    public async Task AfterTheDeepScan_ANormalScanKeepsTheFoundAddress_AndSearchesOnFromIt()
    {
        var env = new Env();
        env.PayReceive(300, 900);
        await env.Service.ScanAsync(receiveGapLimit: 500);

        // Paid later, 10 past the far address: within a normal gap of it, but far beyond a normal gap from the start.
        env.PayReceive(310, 50);
        var normal = await env.Service.ScanAsync();

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 1), normal);
        var saved = (await env.Saved())!;
        Assert.Contains(saved.Addresses, a => a.Address == env.Recv(300));
        Assert.Contains(saved.Addresses, a => a.Address == env.Recv(310));
        Assert.Equal(310, saved.Account.HighestUsedReceiveIndex);
        Assert.Equal(311, saved.Account.NextReceiveIndex);
        Assert.Equal(950, (await env.Service.RefreshAsync()).Balance!.ConfirmedCents);
    }

    [Fact]
    public async Task ANormalScanAfterTheDeepScan_FindsNothingMore_ButDoesNotLoseIt()
    {
        var env = new Env();
        env.PayReceive(300, 900);
        await env.Service.ScanAsync(receiveGapLimit: 500);

        var normal = await env.Service.ScanAsync();

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 0), normal);
        var saved = (await env.Saved())!;
        Assert.Contains(saved.Addresses, a => a.Address == env.Recv(300));
        Assert.Equal(300, saved.Account.HighestUsedReceiveIndex);
        Assert.Equal(301, saved.Account.NextReceiveIndex);
    }

    [Fact]
    public async Task TheDeepScan_Stops500UnusedAddressesPastTheLastUsedOne()
    {
        var env = new Env();
        env.PayReceive(300, 100);
        env.PayReceive(800, 200); // 301..799 is 499 unused: still within the gap
        env.PayReceive(1301, 400); // 801..1300 is 500 unused: the search stops before it

        var deep = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 2), deep);
        var saved = (await env.Saved())!;
        Assert.Equal(800, saved.Account.HighestUsedReceiveIndex);
        Assert.DoesNotContain(saved.Addresses, a => a.Address == env.Recv(1301));
    }

    [Fact]
    public async Task TheDeepGap_AppliesToTheReceiveChainOnly()
    {
        var env = new Env();
        var change = env.Hd.GetAddress(env.Hd.Derive(DigiDollarChain.Change, 300));
        env.Api.History.Add(new DdHistoryEntry("c300", 900, Now.AddHours(-1), 3, null,
            [new DdAmountAt(External, 100)], [new DdAmountAt(change, 100)]));

        var deep = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 0), deep);
        Assert.Equal(0, (await env.Saved())!.Account.NextChangeIndex);
    }

    [Fact]
    public async Task AddressesTheWalletAlreadyHad_AreNotCountedAsMore()
    {
        var env = new Env();
        await env.Service.RefreshAsync();
        for (var i = 0; i < 4; i++) await env.Service.NewReceiveAddressAsync(); // 0..3 handed out here
        env.PayReceive(2, 100);

        var deep = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(new DdScanResult(DdScanOutcome.Finished, 0), deep);
        Assert.Equal(2, (await env.Saved())!.Account.HighestUsedReceiveIndex);
    }

    [Fact]
    public async Task TheDeepScan_ReportsProgress_AndAsksInBatchesOfAtMost200()
    {
        var env = new Env();
        env.PayReceive(300, 900);
        var reports = new List<DdScanProgress>();

        await env.Service.ScanAsync(new SyncProgress(reports), receiveGapLimit: 500);

        Assert.True(reports.Count > 1);
        Assert.True(reports[^1].AddressesChecked >= 801); // receive 0..800 at least
        Assert.Equal(1, reports[^1].ActiveFound);
        Assert.All(env.Api.HistoryBatchSizes, n => Assert.InRange(n, 1, 200));
    }

    [Theory]
    [InlineData(DdWalletKind.WatchOnly)]
    [InlineData(DdWalletKind.Multisig)]
    [InlineData(DdWalletKind.PrivateKey)]
    public async Task NonSeedWallets_AreNotSupported_AndNeverTouchTheBackend(DdWalletKind kind)
    {
        var env = new Env(FakeSessionProvider.Kind(kind));

        var result = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(new DdScanResult(DdScanOutcome.NotSupported, 0), result);
        Assert.Equal(0, env.Api.DataCalls);
    }

    [Fact]
    public async Task BackendDown_IsUnavailable_AndKeepsTheSavedState()
    {
        var env = new Env();
        env.PayReceive(300, 900);
        await env.Service.RefreshAsync();
        var before = (await env.Saved())!;
        env.Api.Down = true;

        var result = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(new DdScanResult(DdScanOutcome.Unavailable, 0), result);
        var after = (await env.Saved())!;
        Assert.Equal(before.Account, after.Account);
        Assert.Equal(before.Addresses.Count, after.Addresses.Count);
    }

    [Fact]
    public async Task BackendFailingMidway_KeepsNothingHalfDone()
    {
        var env = new Env();
        env.PayReceive(300, 900);
        await env.Service.RefreshAsync();
        env.Api.FailAfterDataCalls = env.Api.DataCalls + 1;

        var result = await env.Service.ScanAsync(receiveGapLimit: 500);

        Assert.Equal(DdScanOutcome.Unavailable, result.Outcome);
        Assert.Equal(-1, (await env.Saved())!.Account.HighestUsedReceiveIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AGapBelowOne_IsRefused(int gap)
    {
        var env = new Env();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => env.Service.ScanAsync(receiveGapLimit: gap));
        Assert.Equal(0, env.Api.DataCalls);
    }

    [Fact]
    public async Task Cancelled_Throws_AndSavesNothing()
    {
        var env = new Env();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Service.ScanAsync(ct: cts.Token, receiveGapLimit: 500));
        Assert.Null(await env.Saved());
    }

    [Fact]
    public void AnAccountSavedBeforeThisChange_HasNoUsedReceiveIndexYet()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        const string old =
            """{"WalletId":"w1","Network":"Testnet","NextReceiveIndex":3,"NextChangeIndex":0,"ScanComplete":true,"LastScanAt":null,"CreatedAt":"2026-10-19T12:00:00+00:00"}""";

        var account = JsonSerializer.Deserialize<DigiDollarAccount>(old, options)!;

        Assert.Equal(-1, account.HighestUsedReceiveIndex);
        Assert.Equal(3, account.NextReceiveIndex);
    }
}

using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Services.Export;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The complete DigiDollar history the CSV export uses (spec 031, US4, research R2): every page until the node says there is
/// no more, strict on failure, and never the cached last 50 the DigiDollar screen shows offline.
/// </summary>
public class DigiDollarCompleteHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private const string External = "TDexternalexternalexternalexternalexternal00";

    private sealed class Env
    {
        public FakeDdNodeApi Api { get; } = new();
        public FakeSessionProvider Sessions { get; } = FakeSessionProvider.Seed();
        public DigiDollarService Service { get; }

        public Env()
        {
            var store = new DigiDollarStore(new TestMemoryStorage(), new TestAesCrypto());
            Service = new DigiDollarService(Sessions, Api, store, clock: () => Now);
        }

        private DigiDollarKeyDerivation Hd => Sessions.Session!.Derivation!;
        public string Recv(int index) => Hd.GetAddress(Hd.Derive(DigiDollarChain.Receive, index));

        /// <summary><paramref name="count"/> received transfers to receive address 0, newest first.</summary>
        public void Received(int count)
        {
            Api.Utxos[Recv(0)] = [new DigiDollarUtxo("seed", 0, Recv(0), 100, "5120" + new string('a', 64), 0, 10, false)];
            for (var i = count - 1; i >= 0; i--)
                Api.History.Add(new DdHistoryEntry($"dd{i:0000}", 900, Now.AddMinutes(-i), 10, null,
                    [new DdAmountAt(External, 100 + i)], [new DdAmountAt(Recv(0), 100 + i)]));
        }
    }

    [Fact]
    public async Task FollowsNextUntilTheEnd_PastTheScreensFiftyEntries()
    {
        var env = new Env();
        env.Received(450);
        await env.Service.RefreshAsync();   // finds the wallet's DD addresses

        var history = await env.Service.LoadCompleteHistoryAsync();

        Assert.Equal(450, history.Count);
        Assert.Equal(450, history.Select(h => h.Txid).Distinct().Count());
    }

    [Fact]
    public async Task AFailedPage_ThrowsHistoryIncomplete()
    {
        var env = new Env();
        env.Received(450);
        await env.Service.RefreshAsync();
        env.Api.FailAfterDataCalls = env.Api.DataCalls + 1;   // the second page fails

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => env.Service.LoadCompleteHistoryAsync());
    }

    [Fact]
    public async Task NeverUsesTheCachedHistory()
    {
        var env = new Env();
        env.Received(3);
        await env.Service.RefreshAsync();   // the cache now holds the 3 entries
        env.Api.Down = true;

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => env.Service.LoadCompleteHistoryAsync());
    }

    [Fact]
    public async Task WithoutADigiDollarWallet_ThrowsHistoryIncomplete()
    {
        var store = new DigiDollarStore(new TestMemoryStorage(), new TestAesCrypto());
        var service = new DigiDollarService(new FakeSessionProvider(), new FakeDdNodeApi(), store, clock: () => Now);

        await Assert.ThrowsAsync<HistoryIncompleteException>(() => service.LoadCompleteHistoryAsync());
    }

    [Fact]
    public async Task ReportsProgress_AndHonoursCancellation()
    {
        var env = new Env();
        env.Received(5);
        await env.Service.RefreshAsync();
        var reports = new List<HistoryExportProgress>();

        await env.Service.LoadCompleteHistoryAsync(new SyncProgress(reports.Add));
        Assert.Contains(reports, r => r.Done == r.Total && r.Total > 0);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Service.LoadCompleteHistoryAsync(null, cts.Token));
    }

    private sealed class SyncProgress(Action<HistoryExportProgress> report) : IProgress<HistoryExportProgress>
    {
        public void Report(HistoryExportProgress value) => report(value);
    }
}

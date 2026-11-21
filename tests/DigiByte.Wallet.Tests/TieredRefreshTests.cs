using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Tiered refresh and send coin planning (spec 024): a quiet refresh reads only the hot addresses plus a share of the cold ones,
/// payments still show, full checks where the user expects them, and sends read coins only where there is money.
/// </summary>
public class TieredRefreshTests
{
    private const string W = "w1";
    private const string N = "mainnet";
    private static readonly TimeSpan HotDue = TimeSpan.FromSeconds(31);

    private static string A(int i) => $"dgb1qaddress{i:000}";
    private static List<string> Addresses(int count) => Enumerable.Range(0, count).Select(A).ToList();

    private static HistorySyncEnv TieredEnv()
    {
        var env = new HistorySyncEnv();
        env.Options.Tiered = true;
        return env;
    }

    private static int Requests(HistorySyncEnv env) => env.Explorer.SummaryRequests + env.Explorer.PageRequests + env.Explorer.TipRequests;

    // ═══ US1: a wallet left open costs little (T008) ═══

    [Fact]
    public async Task QuietRefresh_ReadsOnlyTheHotAddressesPlusAShareOfCold()
    {
        var env = TieredEnv();
        var addresses = Addresses(60);
        env.Explorer.Pay(A(5), 5000);
        env.Explorer.Pay(A(8), 7000);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        env.Clock.Advance(HotDue);
        env.Explorer.ResetCounts();
        var result = await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        Assert.Contains(A(5), env.Explorer.SummaryAddresses);
        Assert.Contains(A(8), env.Explorer.SummaryAddresses);
        Assert.True(env.Explorer.SummaryRequests <= 11, $"{env.Explorer.SummaryRequests} summaries read");
        Assert.True(Requests(env) <= 12, $"{Requests(env)} requests");
        Assert.Equal(0, env.Explorer.PageRequests);
        Assert.Equal(12_000, result.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Service_QuietRefresh_ReadsAtMostFifteen()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 5_000_000);
        env.Fund(env.Receive(8), 7_000_000);
        await service.GetBalanceAsync();
        await service.GetTransactionHistoryAsync();

        env.Clock.Advance(HotDue);
        env.Explorer.ResetCounts();
        await Task.WhenAll(service.GetBalanceAsync(), service.GetTransactionHistoryAsync());

        Assert.True(service.GetAllAddresses().Count >= 60);
        Assert.True(Requests(env) <= 15, $"{Requests(env)} requests");   // SC-001: hot + the cold share for 31 s + tip
        Assert.Contains(env.Receive(5), env.Explorer.SummaryAddresses);
        Assert.Contains(env.Receive(0), env.Explorer.SummaryAddresses);   // on show
        Assert.Contains(env.Change(0), env.Explorer.SummaryAddresses);    // next change
    }

    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    public async Task ColdAddresses_AreSpread_AndNoneWaitsFiveMinutes(int paceSeconds)
    {
        var env = TieredEnv();
        var addresses = Addresses(60);
        env.Explorer.Pay(A(5), 5000);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);
        var lastRead = addresses.ToDictionary(a => a, _ => env.Clock.GetUtcNow());

        for (var step = 0; step < 600 / paceSeconds; step++)
        {
            env.Clock.Advance(TimeSpan.FromSeconds(paceSeconds));
            env.Explorer.ResetCounts();
            await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);
            var now = env.Clock.GetUtcNow();
            foreach (var a in env.Explorer.SummaryAddresses) lastRead[a] = now;

            Assert.All(addresses, a => Assert.True(now - lastRead[a] < WalletHistorySync.ColdCheckInterval, $"{a} not read for {now - lastRead[a]}"));
            var coldRead = env.Explorer.SummaryAddresses.Count(a => a != A(5));
            Assert.True(coldRead <= (int)Math.Ceiling(59 * paceSeconds / 300.0), $"{coldRead} cold read at once");
        }
    }

    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    public async Task TenMinutes_AtLeastSixtyPercentFewerRequests(int paceSeconds)
    {
        async Task<int> Run(bool tiered)
        {
            var env = TieredEnv();
            var addresses = Addresses(60);
            env.Explorer.Pay(A(5), 5000);
            env.Explorer.Pay(A(8), 7000);
            HotAddressHints? hints = tiered ? HotAddressHints.Empty : null;
            await env.Sync.RefreshAsync(W, N, addresses, hints);
            env.Explorer.ResetCounts();
            for (var t = paceSeconds; t <= 600; t += paceSeconds)
            {
                env.Clock.Advance(TimeSpan.FromSeconds(paceSeconds));
                await env.Sync.RefreshAsync(W, N, addresses, hints);
            }
            return Requests(env);
        }

        var off = await Run(tiered: false);
        var on = await Run(tiered: true);

        Assert.True(on <= off * 0.4, $"tiered {on} against {off}");
    }

    [Fact]
    public async Task AFailedHotRead_FailsTheRefresh_AndLeavesSavedSummariesAlone()
    {
        var env = TieredEnv();
        var addresses = Addresses(60);
        env.Explorer.Pay(A(5), 5000);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);
        var before = await env.Storage.GetAsync(AddressSummaryStore.SummaryKey(W, N));

        env.Clock.Advance(HotDue);
        env.Explorer.FailSummaryFor.Add(A(5));

        await Assert.ThrowsAsync<HttpRequestException>(() => env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty));
        Assert.Equal(before, await env.Storage.GetAsync(AddressSummaryStore.SummaryKey(W, N)));
    }

    [Fact]
    public async Task FewerThanTenAddresses_AreNotTiered()
    {
        var env = TieredEnv();
        var addresses = Addresses(8);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        env.Clock.Advance(HotDue);
        env.Explorer.ResetCounts();
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        Assert.Equal(8, env.Explorer.SummaryRequests);
    }

    [Fact]
    public async Task Service_TieredSwitchOff_ReadsEveryAddress()
    {
        var env = new HistorySyncEnv();   // Tiered = false
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 5_000_000);
        await service.GetBalanceAsync();

        env.Clock.Advance(HotDue);
        env.Explorer.ResetCounts();
        await service.GetBalanceAsync();

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.SummaryRequests);
    }

    [Fact]
    public async Task Service_ChangeOnlyOff_IgnoresTiering()
    {
        var env = TieredEnv();
        env.Options.ChangeOnly = false;
        var service = await env.CreateAsync();
        await service.GetBalanceAsync();

        env.Explorer.ResetCounts();
        await service.GetBalanceAsync();

        Assert.Equal(0, env.Explorer.SummaryRequests);
        Assert.Null(service.GetFullyCheckedAt());
    }

    [Fact]
    public async Task Service_FirstRefreshAfterDiscovery_StillReusesDiscoverysReads()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 5_000_000);

        await service.GetBalanceAsync();

        Assert.Equal(env.Explorer.SummaryAddresses.Count, env.Explorer.SummaryAddresses.Distinct().Count());
    }

    // ═══ US2: payments still show up (T012) ═══

    [Fact]
    public async Task Service_PaymentsToTheAddressOnShow_InEitherFormat_ShowOnTheNextRefresh()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 5_000_000);
        await service.GetBalanceAsync();

        env.Explorer.Pay(env.Receive(0), 1_000);
        env.Explorer.Pay(env.Hd.DeriveReceivingAddress(0, ScriptPubKeyType.Legacy).ToString(), 2_000);
        env.Clock.Advance(HotDue);
        var balance = await service.GetBalanceAsync();

        Assert.Equal(5_003_000, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Service_HandedOutAddress_IsHot()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        var (_, handedOut) = await service.GenerateNewReceivingAddressAsync();   // index 1 on show; index 0 handed out
        await service.GetBalanceAsync();

        env.Explorer.Pay(env.Receive(0), 3_000);
        env.Clock.Advance(HotDue);
        var balance = await service.GetBalanceAsync();

        Assert.NotEqual(env.Receive(0), handedOut);
        Assert.Equal(3_000, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task PaymentToAColdAddress_ShowsWithinFiveMinutes_AndThenStaysHot()
    {
        var env = TieredEnv();
        var addresses = Addresses(60);
        env.Explorer.Pay(A(5), 5000);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        env.Explorer.Pay(A(40), 2500);
        var paidAt = env.Clock.GetUtcNow();
        DateTimeOffset? seenAt = null;
        for (var step = 0; step < 40 && seenAt is null; step++)
        {
            env.Clock.Advance(TimeSpan.FromSeconds(15));
            var r = await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);
            if (r.ConfirmedSatoshis == 7500) seenAt = env.Clock.GetUtcNow();
        }

        Assert.NotNull(seenAt);
        Assert.True(seenAt - paidAt <= WalletHistorySync.ColdCheckInterval);

        env.Clock.Advance(HotDue);
        env.Explorer.ResetCounts();
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);
        Assert.Contains(A(40), env.Explorer.SummaryAddresses);
    }

    [Fact]
    public async Task Service_RequestFullCheck_ReadsEveryAddress_AndShowsAColdPayment()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 5_000_000);
        await service.GetBalanceAsync();

        env.Explorer.Pay(env.Receive(15), 1_000);
        env.Clock.Advance(TimeSpan.FromSeconds(20));
        service.RequestFullCheck();
        env.Explorer.ResetCounts();
        var balance = await service.GetBalanceAsync();

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.SummaryRequests);
        Assert.Equal(5_001_000, balance.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Service_FullyCheckedAt_NeverOlderThanFiveMinutes_AndResetsAfterAFullCheck()
    {
        var env = TieredEnv();
        var service = await env.CreateAsync();
        Assert.Null(service.GetFullyCheckedAt());

        env.Fund(env.Receive(5), 5_000_000);
        await service.GetBalanceAsync();
        Assert.NotNull(service.GetFullyCheckedAt());

        for (var step = 0; step < 40; step++)
        {
            env.Clock.Advance(TimeSpan.FromSeconds(15));
            await service.GetBalanceAsync();
            Assert.True(env.Clock.GetUtcNow() - service.GetFullyCheckedAt()!.Value < WalletHistorySync.ColdCheckInterval);
        }

        service.RequestFullCheck();
        await service.GetBalanceAsync();
        Assert.Equal(env.Clock.GetUtcNow(), service.GetFullyCheckedAt());
    }

    // ═══ US3: sends read only the coins they need (T016) ═══

    private static async Task<(HistorySyncEnv Env, WalletService Service)> FundedWalletAsync(bool tiered = true)
    {
        var env = new HistorySyncEnv();
        env.Options.Tiered = tiered;
        var service = await env.CreateAsync();
        env.Fund(env.Receive(5), 500_000_000);
        env.Fund(env.Receive(8), 300_000_000);
        await service.GetBalanceAsync();
        return (env, service);
    }

    [Fact]
    public async Task Send_TwoMinutesAfterTheLastRefresh_ReadsCoinsOnlyFromTheFundedAddresses()
    {
        var (env, service) = await FundedWalletAsync();
        env.Clock.Advance(TimeSpan.FromMinutes(2));
        env.Explorer.ResetCoinReads();

        await service.SendAsync(ChangeAddressTests.Stranger(), 1m, feeRateSatPerByte: 100);

        Assert.Equal(2, env.Explorer.CoinReads);
        Assert.Equal(new[] { env.Receive(5), env.Receive(8) }.Order(), env.Explorer.CoinReadAddresses.Order());
    }

    [Fact]
    public async Task Send_WithTieringOff_ReadsEveryAddress()
    {
        var (env, service) = await FundedWalletAsync(tiered: false);
        env.Explorer.ResetCoinReads();

        await service.SendAsync(ChangeAddressTests.Stranger(), 1m, feeRateSatPerByte: 100);

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.CoinReads);
    }

    [Fact]
    public async Task Send_SummaryUnderReports_ReadsTheRestBeforeFailing()
    {
        var (env, service) = await FundedWalletAsync();
        // Coins the summaries don't know about yet, on an address they show as empty.
        var hidden = env.Receive(12);
        env.Explorer.Coins[hidden] = [new UtxoInfo
        {
            TxId = new string('7', 64), OutputIndex = 0, AmountSatoshis = 2_000_000_000, Confirmations = 6,
            ScriptPubKey = BitcoinAddress.Create(hidden, ChangeAddressTests.Network).ScriptPubKey.ToHex(),
        }];
        env.Explorer.ResetCoinReads();

        await service.SendAsync(ChangeAddressTests.Stranger(), 15m, feeRateSatPerByte: 100);

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.CoinReads);
    }

    [Fact]
    public async Task Send_TrulyShort_ReportsInsufficientFundsOnlyAfterReadingEveryAddress()
    {
        var (env, service) = await FundedWalletAsync();
        env.Explorer.ResetCoinReads();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(ChangeAddressTests.Stranger(), 100m, feeRateSatPerByte: 100));

        Assert.Contains("Insufficient funds", ex.Message);
        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.CoinReads);
    }

    [Fact]
    public async Task Send_InAFreshSession_WithNoSummaries_ReadsEveryAddress()
    {
        var (env, service) = await FundedWalletAsync();
        env.Sync.ResetSession(service.ActiveWallet!.Id);
        env.Explorer.ResetCoinReads();

        await service.SendAsync(ChangeAddressTests.Stranger(), 1m, feeRateSatPerByte: 100);

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.CoinReads);
    }

    [Fact]
    public async Task BatchSend_AndConsolidate_FollowThePlan()
    {
        var (env, service) = await FundedWalletAsync();
        env.Explorer.ResetCoinReads();
        await service.SendBatchAsync([(ChangeAddressTests.Stranger(), 1m), (ChangeAddressTests.Stranger(), 1m)], feeRateSatPerByte: 100);
        Assert.Equal(2, env.Explorer.CoinReads);

        await service.GetBalanceAsync();
        env.Explorer.ResetCoinReads();
        await service.ConsolidateUtxosAsync();
        Assert.Equal(2, env.Explorer.CoinReads);
    }

    [Fact]
    public async Task AfterASend_TheNextRefreshReadsEveryAddress()
    {
        var (env, service) = await FundedWalletAsync();
        await service.SendAsync(ChangeAddressTests.Stranger(), 1m, feeRateSatPerByte: 100);

        env.Explorer.ResetCounts();
        await service.GetBalanceAsync();

        Assert.Equal(service.GetAllAddresses().Count, env.Explorer.SummaryRequests);
    }

    [Fact]
    public async Task PlanSendCoinReads_CountsWithoutReading()
    {
        var (env, service) = await FundedWalletAsync();
        env.Explorer.ResetCoinReads();

        Assert.Equal(2, service.PlanSendCoinReads());
        Assert.Equal(0, env.Explorer.CoinReads);
    }

    // ═══ US4: the benchmark's quiet refresh (T019) ═══

    [Fact]
    public async Task AgeSnapshot_MakesTheNextRefreshAQuietOne()
    {
        var env = TieredEnv();
        var addresses = Addresses(60);
        env.Explorer.Pay(A(5), 5000);
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        env.Sync.AgeSnapshot(W, HotDue);
        env.Explorer.ResetCounts();
        await env.Sync.RefreshAsync(W, N, addresses, HotAddressHints.Empty);

        Assert.Contains(A(5), env.Explorer.SummaryAddresses);
        Assert.True(env.Explorer.SummaryRequests <= 11);
    }
}

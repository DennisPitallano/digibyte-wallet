using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Payments past the normal search (spec 027): the addresses a wallet checks once a far-off payment is known, the search
/// that finds such payments, and how the automatic search keeps up afterwards.
/// </summary>
public class ReceiveSearchTests
{
    private static WatchedReceiveIndex P(int index) => new(index, true);
    private static WatchedReceiveIndex U(int index) => new(index, false);

    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        return service;
    }

    /// <summary>A wallet whose stored record was changed as if an earlier session had saved it that way, unlocked afresh.</summary>
    private static async Task<(WalletService Service, string WalletId)> SeededAsync(Env env, Action<WalletInfo> change)
    {
        var walletId = (await CreateAsync(env)).ActiveWallet!.Id;
        await SeedWalletInfoAsync(env.Storage, walletId, change);
        return (await env.ReloadAsync(walletId), walletId);
    }

    private static ReceiveScanSet ScanSetOf(WalletInfo info) =>
        ReceiveScanSet.From(info.NextReceivingIndex, info.UsedReceivingCount, info.ReceiveBaseFloor, info.WatchedReceiveIndexes);

    // ─── The checked set, through the wallet (T024) ───

    [Fact]
    public async Task APaidWatchedAddress_IsInTheBalanceAndTheHistory_AndCanBeSpent()
    {
        var env = new Env();
        env.Chain.DetailedHistory = true;
        env.Chain.Credit(env.ReceiveAddress(3000), 5 * OneDgb);
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        Assert.Equal(5 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
        var history = (await service.LoadTransactionHistoryAsync()).Transactions;
        Assert.Contains(history, t => t.Direction == TransactionDirection.Received && t.AmountSatoshis == 5 * OneDgb);

        await service.SendAsync(Stranger(), 1m);
        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(env.ReceiveAddress(3000)));
    }

    [Fact]
    public async Task APaidWatchedAddress_IsStillCountedAfterAReload_WithTheExplorerDown()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3000), 5 * OneDgb);
        var (_, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        env.Chain.HistoryDown = true;
        var reloaded = await env.ReloadAsync(walletId);

        Assert.Equal(5 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task TheWalletChecksTheBaseRun_TheWatchedEntryAndItsTail_AndNothingInBetween()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        var addresses = service.GetAllAddresses();

        // 20 SegWit + 20 Legacy base entries, the watched entry and its 20-address tail in SegWit only, then change (20).
        Assert.Equal(40 + 21 + 20, addresses.Count);
        Assert.Contains(env.ReceiveAddress(19), addresses);
        Assert.Contains(env.ReceiveAddress(3000), addresses);
        Assert.Contains(env.ReceiveAddress(3020), addresses);
        Assert.DoesNotContain(env.ReceiveAddress(20), addresses);
        Assert.DoesNotContain(env.ReceiveAddress(2999), addresses);
        Assert.DoesNotContain(env.ReceiveAddress(3021), addresses);
        Assert.DoesNotContain(env.Hd.DeriveReceivingAddress(3000, NBitcoin.ScriptPubKeyType.Legacy).ToString(), addresses);
    }

    [Fact]
    public async Task AnUnpaidWatchedAddress_IsChecked_AndAPaymentToItIsInTheBalance()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(3000)]);

        Assert.Contains(env.ReceiveAddress(3000), service.GetAllAddresses());
        Assert.DoesNotContain(env.ReceiveAddress(3001), service.GetAllAddresses()); // unpaid: no tail

        env.Chain.Credit(env.ReceiveAddress(3000), 2 * OneDgb);
        Assert.Equal(2 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task TheFloor_KeepsEarlierHandedOutAddressesChecked()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i =>
        {
            i.NextReceivingIndex = 3001;
            i.ReceiveBaseFloor = 35;
            i.WatchedReceiveIndexes = [P(3000), U(3001)];
        });

        var addresses = service.GetAllAddresses();

        Assert.All(Enumerable.Range(20, 15), index => Assert.Contains(env.ReceiveAddress(index), addresses));
        Assert.DoesNotContain(env.ReceiveAddress(35), addresses);
    }

    [Fact]
    public async Task RenamingAndRecolouring_KeepTheSearchFields()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i =>
        {
            i.WatchedReceiveIndexes = [P(3000), U(3001)];
            i.ReceiveBaseFloor = 20;
            i.ReceiveSearchedTo = 5000;
            i.ReceiveSearchNoticeShown = true;
        });

        await service.RenameWalletAsync(walletId, "Renamed");
        var stored = (await (await env.ReloadAsync(walletId)).GetWalletAsync(walletId))!;

        Assert.Equal("Renamed", stored.Name);
        Assert.Equal([P(3000), U(3001)], stored.WatchedReceiveIndexes);
        Assert.Equal(20, stored.ReceiveBaseFloor);
        Assert.Equal(5000, stored.ReceiveSearchedTo);
        Assert.True(stored.ReceiveSearchNoticeShown);
    }

    [Fact]
    public async Task WithNothingWatched_TheAddressesAreWhatTheyAlwaysWere()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        var addresses = service.GetAllAddresses();

        Assert.Equal(60, addresses.Count);
        Assert.Equal(
            Enumerable.Range(0, 20).SelectMany(i => new[]
            {
                env.ReceiveAddress(i), env.Hd.DeriveReceivingAddress(i, NBitcoin.ScriptPubKeyType.Legacy).ToString(),
            }),
            addresses.Take(40));
    }

    // ─── Handing out a new address after a far-off find (T025) ───

    [Fact]
    public async Task ANewAddress_IsOnePastTheHighestFound_AndIsWatched()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);

        var (index, address) = await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(3002, index);
        Assert.Equal(env.ReceiveAddress(3002), address);
        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Equal(3002, stored.NextReceivingIndex);
        Assert.Equal(20, stored.ReceiveBaseFloor);
        Assert.Contains(U(3002), stored.WatchedReceiveIndexes);
        Assert.Equal(20, ScanSetOf(stored).BaseCount);
    }

    [Fact]
    public async Task NewAddresses_StopOnceTwentyUnpaidAreOutPastTheAnchor()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);

        for (var expected = 3002; expected <= 3021; expected++)
            Assert.Equal(expected, (await service.GenerateNewReceivingAddressAsync()).Index);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateNewReceivingAddressAsync());
        Assert.Equal(3021, service.ActiveWallet!.NextReceivingIndex);
    }

    [Fact]
    public async Task ANewAddress_ThatCannotBeSaved_ChangesNothing()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);
        await service.GetBalanceAsync(); // the unlock's address search has finished
        env.Storage.FailWalletInfoWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateNewReceivingAddressAsync());

        var info = service.ActiveWallet!;
        Assert.Equal(0, info.NextReceivingIndex);
        Assert.Equal(0, info.ReceiveBaseFloor);
        Assert.Equal([P(3000), P(3001)], info.WatchedReceiveIndexes);
    }

    [Fact]
    public async Task AFailedSave_RemovesOnlyWhatTheHandOutAdded()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);
        await service.GetBalanceAsync();
        env.Storage.HoldWalletInfoWrites = true;

        var handOut = service.GenerateNewReceivingAddressAsync();
        // While the save is pending, something else adds an entry (nothing in the wallet can yet; a search will).
        service.ActiveWallet!.WatchedReceiveIndexes.Add(P(4000));
        env.Storage.FailWalletInfoWrites = true;
        env.Storage.ReleaseWalletInfoWrites();
        await Assert.ThrowsAsync<InvalidOperationException>(() => handOut);

        var watched = service.ActiveWallet!.WatchedReceiveIndexes;
        Assert.Contains(P(4000), watched);
        Assert.DoesNotContain(watched, w => w.Index == 3002);
        Assert.Equal(0, service.ActiveWallet!.NextReceivingIndex);
    }

    [Fact]
    public async Task AddressesHandedOutEarlier_StayChecked_AfterTheAddressOnShowMovesFarAway()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3000), 5 * OneDgb);
        var (service, walletId) = await SeededAsync(env, i =>
        {
            i.NextReceivingIndex = 34;
            i.WatchedReceiveIndexes = [P(3000)];
        });

        Assert.Equal(3001, (await service.GenerateNewReceivingAddressAsync()).Index);
        env.Chain.Credit(env.ReceiveAddress(30), 2 * OneDgb);

        Assert.Equal(35, service.ActiveWallet!.ReceiveBaseFloor);
        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(7 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task TheAutomaticSearch_DoesNotWalkTheGapBeforeAFarOffFind()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3000), 5 * OneDgb);
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        await service.GetBalanceAsync();

        // The base walk reaches 20 past the base run (index 39). Everything else looked up is at or past the find.
        var allowed = Enumerable.Range(0, 40).Concat(Enumerable.Range(3000, 600)).Select(env.ReceiveAddress)
            .Concat(Enumerable.Range(0, 60).Select(env.ChangeAddress)).ToHashSet();
        Assert.NotEmpty(env.Chain.HistoryAddresses);
        Assert.All(env.Chain.HistoryAddresses, address => Assert.Contains(address, allowed));
    }

    [Fact]
    public async Task WatchedEntriesTheBaseRunGrowsOver_AreDropped()
    {
        var env = new Env();
        foreach (var index in new[] { 25, 44, 45, 46 }) env.Chain.Credit(env.ReceiveAddress(index), OneDgb);
        var (service, walletId) = await SeededAsync(env, i =>
        {
            i.NextReceivingIndex = 46;
            i.ReceiveBaseFloor = 20;
            i.WatchedReceiveIndexes = [P(45), U(46)];
        });

        Assert.Equal(4 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Equal(47, stored.UsedReceivingCount);
        Assert.Empty(stored.WatchedReceiveIndexes);
        Assert.Equal(47, ScanSetOf(stored).BaseCount);
    }

    // ─── The search (T029) ───

    /// <summary>A wallet whose unlock search has finished, so the lookups counted afterwards are the search's own.</summary>
    private static async Task<WalletService> SettledAsync(Env env)
    {
        var service = await CreateAsync(env);
        await service.GetBalanceAsync();
        return service;
    }

    /// <summary>The addresses looked up since <paramref name="from"/>: the control address first, then the rest.</summary>
    private static (string Control, List<string> Others) LookupsSince(Env env, int from)
    {
        var made = env.Chain.HistoryAddresses.Skip(from).ToList();
        return (made[0], made.Skip(1).ToList());
    }

    [Fact]
    public async Task Search_OfTheFirstFiveThousand_FindsAFarPayment_AndTheWalletKeepsIt()
    {
        var env = new Env();
        env.Chain.MaxHistoryLookups = 20_000;
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var hd = env.Hd;
        string AddressAt(int i) => hd.DeriveReceivingAddress(i, NBitcoin.ScriptPubKeyType.Segwit).ToString();
        env.Chain.Credit(AddressAt(3000), 5 * OneDgb);
        var before = env.Chain.HistoryAddresses.Count;

        var result = await service.SearchReceiveAddressesAsync(5000);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.Equal([3000], result.AddedIndexes);
        Assert.Equal(5 * OneDgb, result.AddedSatoshis);
        Assert.Equal(5000, result.RequestedUpTo);
        Assert.Equal(5000, result.ReachedUpTo);
        Assert.True(result.LeavesUnwatched);
        Assert.False(result.SuggestSearchingFurther);
        Assert.Equal(5000, (await service.GetWalletAsync(walletId))!.ReceiveSearchedTo);
        Assert.Equal(5000, service.ReceiveSearchedTo);
        Assert.Equal(5000, service.DefaultReceiveSearchUpTo);

        // One control address, then every address from the 21st to the 5,000th, and nothing else.
        var (control, others) = LookupsSince(env, before);
        var expected = Enumerable.Range(20, 4980).Select(AddressAt).ToList();
        Assert.DoesNotContain(control, expected);
        Assert.Equal(expected.Order(), others.Order());

        // From now on the wallet checks the find and the 20 after it, not everything before it.
        Assert.Equal(40 + 21 + 20, service.GetAllAddresses().Count);
        Assert.Equal(5 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        // A narrower search afterwards does not pull the furthest search back.
        Assert.Equal(ReceiveSearchOutcome.Finished, (await service.SearchReceiveAddressesAsync(600)).Outcome);
        Assert.Equal(5000, service.ReceiveSearchedTo);

        var reloaded = await env.ReloadAsync(walletId);
        Assert.Equal(5 * OneDgb, (await reloaded.GetBalanceAsync()).ConfirmedSatoshis);
        await reloaded.SendAsync(Stranger(), 1m);
        Assert.Equal(0, env.Chain.UnspentOutputsOwnedBy(AddressAt(3000)));
    }

    [Fact]
    public async Task Search_WithTheDefaultCount_DoesNotReachAFarPayment_AndSaysNothingWasFound()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(3000), 5 * OneDgb);
        Assert.Equal(520, service.DefaultReceiveSearchUpTo);

        var result = await service.SearchReceiveAddressesAsync(service.DefaultReceiveSearchUpTo);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.Empty(result.AddedIndexes);
        Assert.Equal(0, result.AddedSatoshis);
        Assert.False(result.ShowForeignIssuerNotice);
        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Equal(520, stored.ReceiveSearchedTo);
        Assert.Empty(stored.WatchedReceiveIndexes);
        Assert.Equal(1020, service.DefaultReceiveSearchUpTo);
    }

    [Fact]
    public async Task Search_InsideWhatTheWalletAlreadyChecks_HasNothingToCheck_AndLooksNothingUp()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var before = env.Chain.HistoryAddresses.Count;

        var result = await service.SearchReceiveAddressesAsync(15);

        Assert.Equal(ReceiveSearchOutcome.NothingToCheck, result.Outcome);
        Assert.Equal(20, result.AlreadyCheckedUpTo);
        Assert.Equal(before, env.Chain.HistoryAddresses.Count);
        Assert.Equal(0, service.ReceiveSearchedTo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public async Task Search_WithACountOutsideOneToFiveThousand_IsRefusedBeforeAnyLookup(int upTo)
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var before = env.Chain.HistoryAddresses.Count;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SearchReceiveAddressesAsync(upTo));

        Assert.Equal(before, env.Chain.HistoryAddresses.Count);
    }

    [Fact]
    public async Task Search_RunAgain_FindsAPaymentThatArrivedSinceTheLastOne()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        Assert.Empty((await service.SearchReceiveAddressesAsync(100)).AddedIndexes);

        env.Chain.Credit(env.ReceiveAddress(60), 3 * OneDgb);
        var again = await service.SearchReceiveAddressesAsync(100);

        Assert.Equal([60], again.AddedIndexes);
        Assert.Equal(3 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Search_KeepsAPaymentThatWasOnlyInTheTail_WhenAFindFurtherOnMovesTheTail()
    {
        var env = new Env();
        env.Chain.MaxHistoryLookups = 20_000;
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);
        await service.GetBalanceAsync();
        // Paid after the unlock's own search, so recording 3,010 is the wider search's doing.
        foreach (var index in new[] { 3000, 3010, 3500 }) env.Chain.Credit(env.ReceiveAddress(index), OneDgb);

        var result = await service.SearchReceiveAddressesAsync(3600);

        Assert.Contains(3500, result.AddedIndexes);
        var watched = (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes;
        Assert.Contains(P(3010), watched);
        Assert.Contains(P(3500), watched);
        Assert.Equal(3 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Search_MarksAnUnpaidWatchedAddressThatHasSinceBeenPaid()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(100)]);
        await service.GetBalanceAsync();
        env.Chain.Credit(env.ReceiveAddress(100), OneDgb);

        var result = await service.SearchReceiveAddressesAsync(101);

        Assert.Equal([100], result.AddedIndexes);
        Assert.Equal([P(100)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
    }

    [Fact]
    public async Task Search_ThatLeavesNoAddressUnchecked_SaysSo()
    {
        var env = new Env();
        // Recorded as found by an earlier search. (Not paid on this chain: the unlock search would then take it into the base run.)
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(20)]);
        await service.GetBalanceAsync();

        // The wallet checks its first 20, the 21st and the 20 after it: the first 41 addresses.
        var result = await service.SearchReceiveAddressesAsync(41);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.False(result.LeavesUnwatched);
        Assert.True((await service.SearchReceiveAddressesAsync(42)).LeavesUnwatched);
    }

    [Fact]
    public async Task TheDefaultCount_StartsPastTheTailOfTheHighestFound()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        Assert.Equal(3521, service.DefaultReceiveSearchUpTo);
    }

    [Fact]
    public async Task TheDefaultCount_MovesOnFromTheFurthestFinishedSearch()
    {
        var env = new Env();
        var service = await SettledAsync(env);

        await service.SearchReceiveAddressesAsync(2000);

        Assert.Equal(2500, service.DefaultReceiveSearchUpTo);
    }

    [Fact]
    public async Task Search_AsksOnlyWhetherEachAddressWasUsed_AndLeavesTheSavedSummariesAlone()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        await service.GetBalanceAsync();
        env.Explorer.ResetCounts();

        var result = await service.SearchReceiveAddressesAsync(60);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.Empty(env.Explorer.SummaryAddresses);
        Assert.Equal(0, env.Explorer.SummaryRequests);
        Assert.Equal(1 + 40, env.Explorer.HistoryLookups);
    }

    [Fact]
    public async Task Search_OnASingleKeyWallet_HasNothingToCheck()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWalletFromWifAsync("Wif", new NBitcoin.Key().GetWif(Network).ToString(), Pin);
        var before = env.Chain.HistoryAddresses.Count;

        var result = await service.SearchReceiveAddressesAsync(500);

        Assert.Equal(ReceiveSearchOutcome.NothingToCheck, result.Outcome);
        Assert.Equal(before, env.Chain.HistoryAddresses.Count);
    }

    [Fact]
    public async Task Search_WorksOnAWatchOnlyWallet()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWatchOnlyWalletAsync("Watch", env.Hd.GetAccountExtPubKey().ToString(Network), Pin);
        await service.GetBalanceAsync();
        env.Chain.Credit(env.ReceiveAddress(60), 4 * OneDgb);

        var result = await service.SearchReceiveAddressesAsync(100);

        Assert.Equal([60], result.AddedIndexes);
        Assert.Equal(4 * OneDgb, result.AddedSatoshis);
        Assert.Equal(4 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task Search_WhenTheAmountCannotBeRead_StillAddsWhatItFound()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(60), OneDgb);
        env.Chain.BalanceDown = true;

        var result = await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.Equal([60], result.AddedIndexes);
        Assert.Null(result.AddedSatoshis);
        Assert.Equal([P(60)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
    }

    // ─── Searching further, the one-time notice, the next new address (T030) ───

    [Fact]
    public async Task Search_ThatFindsAPaymentNearTheEnd_SuggestsSearchingFurther_ButDoesNotLookPastTheCount()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var hd = env.Hd;
        env.Chain.Credit(env.ReceiveAddress(495), OneDgb);

        var result = await service.SearchReceiveAddressesAsync(500);

        Assert.True(result.SuggestSearchingFurther);
        var past = Enumerable.Range(500, 40).Select(i => hd.DeriveReceivingAddress(i, NBitcoin.ScriptPubKeyType.Segwit).ToString()).ToHashSet();
        Assert.DoesNotContain(env.Chain.HistoryAddresses, past.Contains);
    }

    [Fact]
    public async Task Search_WhoseLastFindIsFarFromTheEnd_DoesNotSuggestSearchingFurther()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        env.Chain.Credit(env.ReceiveAddress(300), OneDgb);

        Assert.False((await service.SearchReceiveAddressesAsync(500)).SuggestSearchingFurther);
    }

    [Fact]
    public async Task Search_OfTheWholeRange_NeverSuggestsSearchingFurther()
    {
        var env = new Env();
        env.Chain.MaxHistoryLookups = 20_000;
        var service = await SettledAsync(env);
        env.Chain.Credit(env.ReceiveAddress(4995), OneDgb);

        var result = await service.SearchReceiveAddressesAsync(5000);

        Assert.Equal([4995], result.AddedIndexes);
        Assert.False(result.SuggestSearchingFurther);
    }

    [Fact]
    public async Task TheNoticeThatTheKeyIsUsedElsewhere_IsRaisedOnce()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(60), OneDgb);

        var first = await service.SearchReceiveAddressesAsync(100);
        env.Chain.Credit(env.ReceiveAddress(150), OneDgb);
        var second = await service.SearchReceiveAddressesAsync(200);

        Assert.True(first.ShowForeignIssuerNotice);
        Assert.True((await service.GetWalletAsync(walletId))!.ReceiveSearchNoticeShown);
        Assert.Equal([150], second.AddedIndexes);
        Assert.False(second.ShowForeignIssuerNotice);
    }

    [Fact]
    public async Task AfterASearch_ANewAddressIsOnePastTheHighestFound()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        env.Chain.Credit(env.ReceiveAddress(101), OneDgb);
        await service.SearchReceiveAddressesAsync(200);

        Assert.Equal(102, (await service.GenerateNewReceivingAddressAsync()).Index);
    }

    // ─── The control address (T031) ───

    [Fact]
    public async Task Search_WhenTheSourceReportsHistoryForAnAddressNobodyUsed_ChangesNothing()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.EveryAddressHasHistory = true;
        var before = env.Chain.HistoryAddresses.Count;

        var result = await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(ReceiveSearchOutcome.Untrusted, result.Outcome);
        Assert.Equal(0, result.ReachedUpTo);
        Assert.Equal(before + 1, env.Chain.HistoryAddresses.Count);
        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Empty(stored.WatchedReceiveIndexes);
        Assert.Equal(0, stored.ReceiveSearchedTo);
    }

    [Fact]
    public async Task Search_WhenTheSourceIsDownFromTheStart_Fails_AndSavesNothing()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.HistoryDown = true;

        var result = await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(ReceiveSearchOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.ReachedUpTo);
        Assert.NotNull(result.Error);
        Assert.Null(result.AddedSatoshis);
        Assert.Equal(0, (await service.GetWalletAsync(walletId))!.ReceiveSearchedTo);
    }

    [Fact]
    public async Task Search_WhereEveryAddressAtTheEndIsPaid_AddsThemAll()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        foreach (var index in Enumerable.Range(100, 5)) env.Chain.Credit(env.ReceiveAddress(index), OneDgb);

        var result = await service.SearchReceiveAddressesAsync(105);

        Assert.Equal(ReceiveSearchOutcome.Finished, result.Outcome);
        Assert.Equal([100, 101, 102, 103, 104], result.AddedIndexes.Order());
        Assert.Equal(5 * OneDgb, result.AddedSatoshis);
    }

    [Fact]
    public async Task TheControlAddress_IsNewEachTime_AndIsNeverOneOfTheWallets()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var hd = env.Hd;
        var own = Enumerable.Range(0, 60).SelectMany(i => new[]
        {
            hd.DeriveReceivingAddress(i, NBitcoin.ScriptPubKeyType.Segwit).ToString(),
            hd.DeriveChangeAddress(i, NBitcoin.ScriptPubKeyType.Segwit).ToString(),
        }).ToHashSet();

        var before = env.Chain.HistoryAddresses.Count;
        await service.SearchReceiveAddressesAsync(30);
        var first = LookupsSince(env, before).Control;
        before = env.Chain.HistoryAddresses.Count;
        await service.SearchReceiveAddressesAsync(30);
        var second = LookupsSince(env, before).Control;

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(first, own);
        Assert.DoesNotContain(second, own);
    }

    // ─── Found funds are old payments, not a first payment (T032, FR-013a) ───

    [Theory]
    [InlineData(true)]   // recorded as empty by an earlier completed load
    [InlineData(false)]  // not decided yet
    public async Task Search_ThatFindsFunds_RecordsThatTheWalletHasHistory(bool recordedAsEmpty)
    {
        var env = new Env();
        env.Chain.DetailedHistory = true;
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var firstReceive = new FirstReceiveStore(env.Storage);
        if (recordedAsEmpty) await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);
        env.Chain.Credit(env.ReceiveAddress(60), 5 * OneDgb);

        await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(FirstReceiveState.History, await firstReceive.GetAsync(walletId));
        // The history really holds the found payment, and a wallet still recorded as empty would have celebrated it.
        var history = (await service.LoadTransactionHistoryAsync()).Transactions;
        Assert.Contains(history, t => t.Direction == TransactionDirection.Received && t.AmountSatoshis == 5 * OneDgb);
        Assert.NotNull(FirstReceiveRule.Evaluate(FirstReceiveState.Empty, history, HistorySource.CompleteLoad).Payment);
    }

    [Theory]
    [InlineData(FirstReceiveState.History)]
    [InlineData(FirstReceiveState.Celebrated)]
    public async Task Search_ThatFindsFunds_LeavesAWalletThatAlreadyHadHistoryAsItWas(FirstReceiveState state)
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);
        await firstReceive.SetAsync(walletId, state);
        env.Chain.Credit(env.ReceiveAddress(60), OneDgb);

        await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(state, await firstReceive.GetAsync(walletId));
    }

    [Fact]
    public async Task Search_ThatFindsNothing_LeavesAnEmptyWalletRecordedAsEmpty()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);

        await service.SearchReceiveAddressesAsync(100);

        Assert.Equal(FirstReceiveState.Empty, await firstReceive.GetAsync(walletId));
    }

    [Fact]
    public async Task Search_SavesWhatItFinds_BatchByBatch_AndRecordsHistoryFirst()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);
        env.Chain.Credit(env.ReceiveAddress(25), OneDgb);
        env.Chain.HoldAfterLookups = 1 + WalletService.ReceiveSearchBatchSize; // the control address and the first batch

        var search = service.SearchReceiveAddressesAsync(100);
        for (var i = 0; i < 1000 && !(await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes.Contains(P(25)); i++)
            await Task.Delay(5);

        Assert.False(search.IsCompleted);
        Assert.Contains(P(25), (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
        Assert.Equal(FirstReceiveState.History, await firstReceive.GetAsync(walletId));

        env.Chain.Release();
        Assert.Equal(ReceiveSearchOutcome.Finished, (await search).Outcome);
    }

    [Fact]
    public async Task HandingOutAnAddress_DoesNotRecordHistory()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);

        await service.GenerateNewReceivingAddressAsync();

        Assert.Equal(FirstReceiveState.Empty, await firstReceive.GetAsync(walletId));
    }

    // ─── The automatic search keeps up after a find (T034, FR-014b) ───

    /// <summary>The receive indexes at or past <paramref name="from"/> that were looked up, read from the fake chain.</summary>
    private static List<int> ReceiveLookups(Env env, int from, int count)
    {
        var hd = env.Hd;
        var byAddress = Enumerable.Range(from, count)
            .ToDictionary(i => hd.DeriveReceivingAddress(i, NBitcoin.ScriptPubKeyType.Segwit).ToString(), i => i);
        return [.. env.Chain.HistoryAddresses.Where(byAddress.ContainsKey).Select(a => byAddress[a]).Order()];
    }

    [Fact]
    public async Task ALaterPaymentNearAFind_ShowsAtTheNextUnlock_WithNoSearch()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3000), OneDgb);
        env.Chain.Credit(env.ReceiveAddress(3005), 2 * OneDgb);
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        Assert.Equal(3 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);

        Assert.Equal([P(3000), P(3005)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
        // 3,001 to 3,020, then 20 more after the find at 3,005, and no further.
        Assert.Equal(Enumerable.Range(3001, 25), ReceiveLookups(env, 3000, 200));
    }

    [Fact]
    public async Task TheWalkPastTheHighestFound_StopsAfterTwentyUnused()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);

        await service.GetBalanceAsync();

        Assert.Equal(Enumerable.Range(3001, 20), ReceiveLookups(env, 3000, 200));
    }

    [Fact]
    public async Task AHandedOutAddressThatGetsPaid_IsMarkedAtTheNextUnlock_AndNewAddressesOpenUpAgain()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000)]);
        for (var i = 0; i < 20; i++) await service.GenerateNewReceivingAddressAsync(); // 3,001 to 3,020: the limit
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateNewReceivingAddressAsync());

        env.Chain.Credit(env.ReceiveAddress(3020), OneDgb);
        var reloaded = await env.ReloadAsync(walletId);
        await reloaded.GetBalanceAsync();

        Assert.Contains(P(3020), reloaded.ActiveWallet!.WatchedReceiveIndexes);
        Assert.Equal(3021, (await reloaded.GenerateNewReceivingAddressAsync()).Index);
    }

    [Fact]
    public async Task AnUnpaidWatchedAddressFarFromTheTail_IsMarkedWhenPaid()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(100), OneDgb);
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(100), P(3000)]);

        await service.GetBalanceAsync();

        Assert.Equal([P(100), P(3000)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
    }

    [Fact]
    public async Task TheAddressOnShow_FoundPaidAtUnlock_DoesNotTurnEverythingBeforeItIntoCheckedAddresses()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3003), OneDgb);
        var (service, walletId) = await SeededAsync(env, i =>
        {
            i.NextReceivingIndex = 3003;
            i.ReceiveBaseFloor = 20;
            i.WatchedReceiveIndexes = [P(3000), P(3001), U(3002), U(3003)];
        });

        await service.GetBalanceAsync();

        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Contains(P(3003), stored.WatchedReceiveIndexes);
        Assert.Equal(20, ScanSetOf(stored).BaseCount);
        // Base run (20 in two formats), four watched, the 20 after 3,003, and change.
        Assert.Equal(40 + 4 + 20 + 20, service.GetAllAddresses().Count);
    }

    [Fact]
    public async Task AnAddressHandedOutBeforeALaterFind_StaysChecked()
    {
        var env = new Env();
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);
        Assert.Equal(3002, (await service.GenerateNewReceivingAddressAsync()).Index);

        // An outside payment far enough on that 3,002 is no longer within 20 of the highest found.
        env.Chain.Credit(env.ReceiveAddress(3015), OneDgb);
        var reloaded = await env.ReloadAsync(walletId);
        await reloaded.GetBalanceAsync();
        env.Chain.Credit(env.ReceiveAddress(3030), OneDgb);
        reloaded = await env.ReloadAsync(walletId);
        await reloaded.GetBalanceAsync();
        env.Chain.Credit(env.ReceiveAddress(3002), 4 * OneDgb);

        Assert.Equal(6 * OneDgb, (await (await env.ReloadAsync(walletId)).GetBalanceAsync()).ConfirmedSatoshis);
    }

    [Fact]
    public async Task OneUnlock_FindsAnOutsidePaymentInTheTail_AndAPaidHandedOutAddressFurtherOn()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3005), OneDgb);
        env.Chain.Credit(env.ReceiveAddress(3015), OneDgb);
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [P(3000), U(3015)]);

        await service.GetBalanceAsync();

        // The walk starts after 3,000, the highest found when the pass began: starting after 3,015 would miss 3,005.
        Assert.Equal([P(3000), P(3005), P(3015)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
    }

    [Fact]
    public async Task WithOnlyUnpaidWatchedAddresses_ThereIsNoWalk()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(100)]);

        await service.GetBalanceAsync();

        Assert.Equal([100], ReceiveLookups(env, 40, 400));
    }

    [Fact]
    public async Task AWalkThatNeverEnds_AppliesNothing_NotEvenWatchedAddressesReportedPaid()
    {
        var env = new Env();
        var hd = env.Hd;
        env.Chain.Credit(env.ReceiveAddress(100), OneDgb);
        foreach (var index in Enumerable.Range(3001, 600))
            env.Chain.Credit(hd.DeriveReceivingAddress(index, NBitcoin.ScriptPubKeyType.Segwit).ToString(), OneDgb);
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(100), P(3000)]);

        await service.GetBalanceAsync();

        Assert.Equal([U(100), P(3000)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
        Assert.Equal([U(100), P(3000)], service.ActiveWallet!.WatchedReceiveIndexes);
        Assert.Equal(500, ReceiveLookups(env, 3001, 1000).Count);
    }

    [Fact]
    public async Task WhenTheFirstPartOfTheAutomaticSearchGivesUp_TheWatchedPassDoesNotRun()
    {
        var env = new Env();
        env.Chain.EveryAddressHasHistory = true;
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(3000)]);

        await service.GetBalanceAsync();

        Assert.Equal([U(3000)], (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes);
        Assert.Empty(ReceiveLookups(env, 3000, 50));
    }

    [Fact]
    public async Task AnAddressHandedOutWhileTheAutomaticSearchRuns_IsKept()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(3005), OneDgb);
        var walletId = (await CreateAsync(env)).ActiveWallet!.Id;
        await SeedWalletInfoAsync(env.Storage, walletId, i => i.WatchedReceiveIndexes = [P(3000), P(3001)]);
        var service = env.NewService();
        service.AddressDiscoverySendWait = TimeSpan.FromMilliseconds(50); // do not wait a minute for the held search
        Assert.True(await service.UnlockWalletAsync(walletId, Pin));
        env.Chain.HoldAfterLookups = 0;

        var handedOut = await service.GenerateNewReceivingAddressAsync(); // starts the search, which is held
        env.Chain.Release();
        await service.GetBalanceAsync(); // waits for the search

        Assert.Equal(3002, handedOut.Index);
        var watched = (await service.GetWalletAsync(walletId))!.WatchedReceiveIndexes;
        Assert.Contains(U(3002), watched);
        Assert.Contains(P(3005), watched);
    }

    [Fact]
    public async Task AFirstPaidEntryFromTheAutomaticSearch_RecordsThatTheWalletHasHistory()
    {
        var env = new Env();
        env.Chain.Credit(env.ReceiveAddress(100), OneDgb);
        var (service, walletId) = await SeededAsync(env, i => i.WatchedReceiveIndexes = [U(100)]);
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);

        await service.GetBalanceAsync();

        Assert.Equal(FirstReceiveState.History, await firstReceive.GetAsync(walletId));
    }

    [Fact]
    public async Task WithNothingWatched_UnlockLooksUpWhatItAlwaysDid()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        await service.GetBalanceAsync();

        // The first 40 receive addresses, once each, and nothing further along.
        Assert.Equal(Enumerable.Range(0, 40), ReceiveLookups(env, 0, 600));
    }

    // ─── What "Your addresses" lists (T036, FR-015b) ───

    [Fact]
    public async Task ListedIndexes_WithNothingWatched_AreEveryIndexUpToTheAddressOnShow()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        for (var i = 0; i < 3; i++) await service.GenerateNewReceivingAddressAsync();

        Assert.Equal([0, 1, 2, 3], service.GetListedReceiveIndexes());
    }

    [Fact]
    public async Task ListedIndexes_AfterAFarOffFind_AreTheBaseRunAndTheWatchedAddresses_WithNothingInBetween()
    {
        var env = new Env();
        var (service, _) = await SeededAsync(env, i =>
        {
            i.NextReceivingIndex = 3002;
            i.ReceiveBaseFloor = 20;
            i.WatchedReceiveIndexes = [P(3000), P(3001), U(3002)];
        });

        Assert.Equal([.. Enumerable.Range(0, 20), 3000, 3001, 3002], service.GetListedReceiveIndexes());
    }

    // ─── Stopping and failing (T040, T041) ───

    /// <summary>Reports on the calling thread, so a test sees each report as soon as it is made.</summary>
    private sealed class Reports : IProgress<ReceiveSearchProgress>
    {
        private readonly List<ReceiveSearchProgress> _made = [];
        public void Report(ReceiveSearchProgress value) { lock (_made) _made.Add(value); }
        public List<ReceiveSearchProgress> Made { get { lock (_made) return [.. _made]; } }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition(), "The search did not get as far as the test needs.");
    }

    [Fact]
    public async Task ACancelledSearch_KeepsWhatItFound_SaysHowFarItGot_AndStartsNoFurtherBatch()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(25), 3 * OneDgb);
        var before = env.Chain.HistoryAddresses.Count;
        var reports = new Reports();
        using var cancel = new CancellationTokenSource();
        env.Chain.HoldAfterLookups = 1 + WalletService.ReceiveSearchBatchSize; // the control address and the first batch

        var search = service.SearchReceiveAddressesAsync(100, reports, cancel.Token);
        await UntilAsync(() => reports.Made.Count == 1); // first batch done, second one held
        cancel.Cancel();
        env.Chain.Release();
        var result = await search; // does not throw

        Assert.Equal(ReceiveSearchOutcome.Cancelled, result.Outcome);
        Assert.Equal([25], result.AddedIndexes);
        Assert.Equal(3 * OneDgb, result.AddedSatoshis);
        Assert.Equal(60, result.ReachedUpTo); // the batch that was out when cancel came is the last complete one
        Assert.Equal(1 + 2 * WalletService.ReceiveSearchBatchSize, env.Chain.HistoryAddresses.Count - before);
        Assert.Equal(0, (await service.GetWalletAsync(walletId))!.ReceiveSearchedTo);
        Assert.Equal(3 * OneDgb, (await service.GetBalanceAsync()).ConfirmedSatoshis);
        Assert.Equal(
            [new ReceiveSearchProgress(40, 100, 1), new ReceiveSearchProgress(60, 100, 1)],
            reports.Made);
    }

    [Fact]
    public async Task AFinishedSearch_ReportsProgressOncePerBatch()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var reports = new Reports();

        await service.SearchReceiveAddressesAsync(100, reports);

        Assert.Equal([40, 60, 80, 100], reports.Made.Select(r => r.CheckedUpTo));
        Assert.All(reports.Made, r => Assert.Equal(100, r.RequestedUpTo));
    }

    [Fact]
    public async Task ASearchWhoseLookupsStartFailing_Fails_KeepsWhatItFound_AndDoesNotThrow()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(25), 3 * OneDgb);
        var reports = new Reports();
        env.Chain.HoldAfterLookups = 1 + 2 * WalletService.ReceiveSearchBatchSize; // the third batch waits

        var search = service.SearchReceiveAddressesAsync(100, reports);
        await UntilAsync(() => reports.Made.Count == 2);
        env.Chain.HistoryDown = true;
        env.Chain.Release();
        var result = await search;

        Assert.Equal(ReceiveSearchOutcome.Failed, result.Outcome);
        Assert.Equal(60, result.ReachedUpTo);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal([25], result.AddedIndexes);
        Assert.False(result.SuggestSearchingFurther);
        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Equal([P(25)], stored.WatchedReceiveIndexes);
        Assert.Equal(0, stored.ReceiveSearchedTo);
    }

    [Fact]
    public async Task LockingTheWalletDuringASearch_StopsIt_AndNothingIsHalfSaved()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var walletId = service.ActiveWallet!.Id;
        var firstReceive = new FirstReceiveStore(env.Storage);
        await firstReceive.SetAsync(walletId, FirstReceiveState.Empty);
        env.Chain.Credit(env.ReceiveAddress(25), OneDgb);
        env.Chain.HoldAfterLookups = 1; // the first batch waits

        var search = service.SearchReceiveAddressesAsync(100);
        await service.LockWalletAsync();
        env.Chain.Release();
        var result = await search;

        Assert.Equal(ReceiveSearchOutcome.Cancelled, result.Outcome);
        Assert.Empty(result.AddedIndexes);
        var stored = (await service.GetWalletAsync(walletId))!;
        Assert.Empty(stored.WatchedReceiveIndexes);
        Assert.Equal(0, stored.ReceiveSearchedTo);
        Assert.Equal(FirstReceiveState.Empty, await firstReceive.GetAsync(walletId));
    }

    [Fact]
    public async Task ASecondSearchWhileOneRuns_JoinsIt_AndMakesNoLookupsOfItsOwn()
    {
        var env = new Env();
        var service = await SettledAsync(env);
        var before = env.Chain.HistoryAddresses.Count;
        env.Chain.HoldAfterLookups = 1;

        var first = service.SearchReceiveAddressesAsync(100);
        var second = service.SearchReceiveAddressesAsync(200);
        env.Chain.Release();

        Assert.Same(first, second);
        Assert.Equal(100, (await second).RequestedUpTo);
        Assert.Equal(1 + 80, env.Chain.HistoryAddresses.Count - before);
    }
}

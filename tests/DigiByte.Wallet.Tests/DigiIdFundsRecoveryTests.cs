using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Finding, and later recovering, funds sent to Digi-ID addresses (spec 009). These run the real WalletService against
/// <see cref="DigiIdFakeChain"/>, which can fail per address and records every call it receives.
/// </summary>
public class DigiIdFundsRecoveryTests
{
    private const string SiteA = "example.com";
    private const string SiteB = "other.org";
    private const string SiteC = "third.net";

    private static IEnumerable<DigiIdFakeChain.Call> VerifiedLookups(DigiIdEnv env) =>
        env.Chain.Calls.Where(c => c.Method == nameof(IBlockchainService.GetVerifiedUtxosAsync));

    // ─── Scan: what is found (FR-004, FR-005) ───

    [Fact]
    public async Task Scan_FundsOnOneOfThreeSites_GivesOneFundsAndTwoEmpty_WithConfirmedAndPendingAmounts()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 5_000_000, confirmations: 3);
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 1_250_000, confirmations: 10);
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 700_000, confirmations: 0);

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB, SiteC]);

        Assert.Equal(3, findings.Count);
        var a = Assert.Single(findings, f => f.Domain == SiteA);
        Assert.Equal(DigiIdFundOutcome.Funds, a.Outcome);
        Assert.Equal(6_250_000, a.ConfirmedSatoshis);
        Assert.Equal(700_000, a.PendingSatoshis);
        Assert.True(a.CanSweep);
        Assert.All(findings.Where(f => f.Domain != SiteA), f =>
        {
            Assert.Equal(DigiIdFundOutcome.Empty, f.Outcome);
            Assert.Equal(0, f.ConfirmedSatoshis);
            Assert.False(f.CanSweep);
        });
    }

    [Fact]
    public async Task Scan_OnlyPendingFunds_IsFunds_ButNothingToSweepYet()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 900_000, confirmations: 0);

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(DigiIdFundOutcome.Funds, finding.Outcome);
        Assert.Equal(0, finding.ConfirmedSatoshis);
        Assert.Equal(900_000, finding.PendingSatoshis);
        Assert.False(finding.CanSweep);
    }

    [Fact]
    public async Task Scan_NothingAnywhere_IsEmptyForEverySite()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB]);

        Assert.All(findings, f => Assert.Equal(DigiIdFundOutcome.Empty, f.Outcome));
    }

    [Fact]
    public async Task Scan_NoDomains_GivesAnEmptyListAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var findings = await service.ScanDigiIdFundsAsync([]);

        Assert.Empty(findings);
        Assert.Empty(VerifiedLookups(env));
    }

    // ─── Scan: the address is derived, never taken from history (FR-004, R-3) ───

    [Fact]
    public async Task Scan_AddressIsTheOneTheWalletDerivesForTheSite()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(env.DigiIdAddress(SiteA), finding.Address);
        Assert.Equal(service.GetDigiIdAddress(SiteA), finding.Address);
        Assert.StartsWith("D", finding.Address); // Legacy P2PKH on mainnet
    }

    [Fact]
    public async Task Scan_ADifferentWalletDerivesDifferentAddresses_SoAnotherWalletsHistoryFindsNothing()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var other = new DigiIdEnv();
        // Funds sit on the address the OTHER wallet would use for the site, as a login history from another wallet would name it.
        env.Chain.AddUtxo(other.DigiIdAddress(SiteA), 5_000_000);

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(DigiIdFundOutcome.Empty, finding.Outcome);
        Assert.NotEqual(other.DigiIdAddress(SiteA), finding.Address);
    }

    [Theory]
    [InlineData("Example.com")]
    [InlineData("https://example.com/some/path?x=1")]
    [InlineData("example.com:8443")]
    public async Task Scan_DifferentSpellingsOfASite_FindTheSameFunds(string typed)
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress("example.com"), 5_000_000);

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([typed]));

        Assert.Equal("example.com", finding.Domain);
        Assert.Equal(DigiIdFundOutcome.Funds, finding.Outcome);
        Assert.Equal(5_000_000, finding.ConfirmedSatoshis);
    }

    [Fact]
    public async Task Scan_TheSameSiteGivenSeveralWays_IsCheckedOnce()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var findings = await service.ScanDigiIdFundsAsync([SiteA, "Example.com", "https://example.com/x", SiteB]);

        Assert.Equal(2, findings.Count);
        Assert.Equal(2, VerifiedLookups(env).Count());
    }

    // ─── Scan: failures are never "nothing found" (FR-007) ───

    [Fact]
    public async Task Scan_OneLookupFails_ThatSiteIsCouldNotCheck_AndTheOthersAreNormal()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 5_000_000);
        env.Chain.ThrowForAddresses.Add(env.DigiIdAddress(SiteB));

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB, SiteC]);

        Assert.Equal(DigiIdFundOutcome.Funds, findings.Single(f => f.Domain == SiteA).Outcome);
        var failed = findings.Single(f => f.Domain == SiteB);
        Assert.Equal(DigiIdFundOutcome.CouldNotCheck, failed.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        Assert.Equal(DigiIdFundOutcome.Empty, findings.Single(f => f.Domain == SiteC).Outcome);
    }

    [Fact]
    public async Task Scan_EveryLookupFails_GivesOnlyCouldNotCheck_NeverEmpty()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.ThrowForEverything = true;

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB, SiteC]);

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Equal(DigiIdFundOutcome.CouldNotCheck, f.Outcome));
        Assert.DoesNotContain(findings, f => f.Outcome == DigiIdFundOutcome.Empty);
    }

    [Fact]
    public async Task Scan_CouldNotCheckMessage_NeverCarriesTheRawExceptionText()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.ThrowForEverything = true;

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.DoesNotContain("explorer cannot answer", finding.Error);
        Assert.DoesNotContain(finding.Address, finding.Error);
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("")]
    [InlineData("bob@example.com")]
    public async Task Scan_InvalidSiteName_IsCouldNotCheck_WithNoLookupAndNoAddress(string typed)
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([typed]));

        Assert.Equal(DigiIdFundOutcome.CouldNotCheck, finding.Outcome);
        Assert.Equal("not a valid site name", finding.Error);
        Assert.Equal("", finding.Address);
        Assert.Empty(VerifiedLookups(env));
    }

    // ─── Scan: one request per address (FR-004b) ───

    [Fact]
    public async Task Scan_EachAddressIsItsOwnVerifiedLookup_NeverBatched()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var sites = new[] { SiteA, SiteB, SiteC, "fourth.io", "fifth.dev" };
        var addresses = sites.Select(env.DigiIdAddress).ToList();

        await service.ScanDigiIdFundsAsync(sites);

        var lookups = VerifiedLookups(env).ToList();
        Assert.Equal(sites.Length, lookups.Count);
        Assert.All(lookups, c => Assert.Single(c.Addresses));
        Assert.Equal(addresses.OrderBy(a => a), lookups.Select(c => c.Addresses[0]).OrderBy(a => a));

        // Nothing that carries a Digi-ID address went through any other call, and above all not through a batched one.
        var touching = env.Chain.CallsTouching(addresses);
        Assert.All(touching, c => Assert.Equal(nameof(IBlockchainService.GetVerifiedUtxosAsync), c.Method));
        Assert.DoesNotContain(env.Chain.Calls, c => c.Method.Contains("batch") && c.Addresses.Any(addresses.Contains));
    }

    // ─── Scan: other kinds of output are left alone (FR-016) ───

    [Fact]
    public async Task Scan_AnOutputWithADifferentScript_IsLeftOutOfTheAmountsAndCounted()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var address = env.DigiIdAddress(SiteA);
        env.Chain.AddUtxo(address, 5_000_000);
        // An output the address's own standard script does not describe, such as a token carrier.
        env.Chain.AddUtxo(address, 9_000_000, scriptHex: "6a0b68656c6c6f20776f726c64");

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(DigiIdFundOutcome.Funds, finding.Outcome);
        Assert.Equal(5_000_000, finding.ConfirmedSatoshis);
        Assert.Equal(0, finding.PendingSatoshis);
        Assert.Equal(1, finding.OtherOutputsCount);
    }

    [Fact]
    public async Task Scan_OnlyOtherOutputs_IsEmpty_ButTheCountIsKept()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 9_000_000, scriptHex: "6a0b68656c6c6f20776f726c64");

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(DigiIdFundOutcome.Empty, finding.Outcome);
        Assert.Equal(0, finding.ConfirmedSatoshis);
        Assert.Equal(1, finding.OtherOutputsCount);
        Assert.False(finding.CanSweep);
    }

    [Fact]
    public async Task Scan_AnOutputWithNoScriptReturned_CountsAsPlain()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        env.Chain.AddUtxo(env.DigiIdAddress(SiteA), 5_000_000, scriptHex: "");

        var finding = Assert.Single(await service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Equal(5_000_000, finding.ConfirmedSatoshis);
        Assert.Equal(0, finding.OtherOutputsCount);
    }

    // ─── Scan: in flight (FR-012) ───

    [Fact]
    public async Task Scan_AddressWithASendingOrPendingSweep_IsInFlightAndNotSweepable()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var address = env.DigiIdAddress(SiteA);
        env.Chain.AddUtxo(address, 5_000_000);
        await env.SweepStore.TryBeginAsync(service.ActiveWallet!.Id, new DigiIdSweepRecord
        {
            Id = "s1", Domain = SiteA, SourceAddress = address, DestinationAddress = env.ReceiveAddress(1),
            AmountSatoshis = 5_000_000, FeeSatoshis = 19_200, Status = DigiIdSweepStatus.Sending,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
        });

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB]);

        var a = findings.Single(f => f.Domain == SiteA);
        Assert.True(a.InFlight);
        Assert.False(a.CanSweep);
        Assert.False(findings.Single(f => f.Domain == SiteB).InFlight);
    }

    // ─── Scan: who may run it (FR-006, SC-005) ───

    [Fact]
    public async Task Scan_LockedWallet_ThrowsAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateLockedHdAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(VerifiedLookups(env));
        Assert.Empty(env.Chain.CallsTouching([env.DigiIdAddress(SiteA)]));
    }

    [Fact]
    public async Task Scan_WatchOnlyWallet_ThrowsAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateWatchOnlyAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Empty(VerifiedLookups(env));
    }

    [Fact]
    public async Task Scan_SingleKeyWifWallet_ThrowsAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateWifAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Empty(VerifiedLookups(env));
    }

    [Fact]
    public async Task Scan_WithoutASweepStore_SaysRecoveryIsNotConfigured()
    {
        var env = new DigiIdEnv();
        var service = new WalletService(new Storage.WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(), env.Chain,
            new TransactionTracker(env.Storage));
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("Test", env.Words, DigiIdEnv.Pin);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ScanDigiIdFundsAsync([SiteA]));

        Assert.Contains("not configured", ex.Message);
    }

    // ─── Scan: progress and cancellation ───

    [Fact]
    public async Task Scan_ReportsProgressUpToOneHundred()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var reported = new List<int>();

        await service.ScanDigiIdFundsAsync([SiteA, SiteB, SiteC, "not a domain"], new SyncProgress(reported.Add));

        Assert.NotEmpty(reported);
        Assert.Equal(100, reported.Max());
        Assert.Equal(reported.OrderBy(p => p), reported); // never goes backwards
    }

    [Fact]
    public async Task Scan_CancelledBeforeItStarts_ReturnsNothingAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var findings = await service.ScanDigiIdFundsAsync([SiteA, SiteB], ct: cts.Token);

        Assert.Empty(findings);
        Assert.Empty(VerifiedLookups(env));
    }

    [Fact]
    public async Task Scan_CancelledPartWay_ReturnsTheFindingsThatFinished()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        using var cts = new CancellationTokenSource();
        var seen = 0;
        env.Chain.OnVerifiedLookup = _ => { if (++seen == 2) cts.Cancel(); };
        var sites = Enumerable.Range(0, 8).Select(i => $"site{i}.example").ToArray();

        var findings = await service.ScanDigiIdFundsAsync(sites, ct: cts.Token);

        Assert.InRange(findings.Count, 1, sites.Length - 1);
        Assert.All(findings, f => Assert.Equal(DigiIdFundOutcome.Empty, f.Outcome));
    }

    // ─── Nothing is looked up unless the user asks (FR-014) ───

    [Fact]
    public async Task CreatingLockingAndUnlockingTheWallet_MakesNoDigiIdLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var walletId = service.ActiveWallet!.Id;
        await service.LockWalletAsync();
        await service.UnlockWalletAsync(walletId, DigiIdEnv.Pin);

        Assert.True(service.IsUnlocked);
        Assert.Empty(VerifiedLookups(env));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Plan, execute and refresh (User Story 3)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    private static readonly Network Net = DigiByteNetwork.Mainnet;

    private sealed record Funded(DigiIdEnv Env, WalletService Service, string WalletId, string[] Sites,
        IReadOnlyList<DigiIdFundFinding> Findings, Dictionary<string, List<DigiIdFakeChain.FakeUtxo>> Utxos);

    /// <summary>An unlocked HD wallet whose first <paramref name="count"/> sites each hold funds, already scanned.</summary>
    private static async Task<Funded> FundSitesAsync(int count, long amount = 5_000_000, int utxosPerSite = 1)
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var sites = Enumerable.Range(1, count).Select(i => $"site{i}.example").ToArray();
        var utxos = new Dictionary<string, List<DigiIdFakeChain.FakeUtxo>>();
        foreach (var site in sites)
        {
            var address = env.DigiIdAddress(site);
            utxos[address] = Enumerable.Range(0, utxosPerSite).Select(_ => env.Chain.AddUtxo(address, amount)).ToList();
        }

        var findings = await service.ScanDigiIdFundsAsync(sites);
        return new Funded(env, service, service.ActiveWallet!.Id, sites, findings, utxos);
    }

    private static DigiIdSweepPlan OneItemPlan(DigiIdEnv env) => new()
    {
        Items =
        [
            new DigiIdSweepPlanItem
            {
                Domain = "example.com", SourceAddress = env.DigiIdAddress("example.com"), AmountSatoshis = 5_000_000,
                EstimatedFeeSatoshis = 19_200, NetSatoshis = 4_980_800, DestinationAddress = env.ReceiveAddress(5), DestinationIndex = 5,
            },
        ],
        FeeRateSatPerVByte = 100,
    };

    private static Transaction BroadcastFor(Funded f, DigiIdSweepRecord record) =>
        f.Env.Chain.Broadcasts.Single(t => t.GetHash().ToString() == record.TxId);

    // ─── Plan ───

    [Fact]
    public async Task Plan_ReChecksEachSelectedAddress_WithItsOwnRequest()
    {
        var f = await FundSitesAsync(3);
        var before = VerifiedLookups(f.Env).Count();

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var added = VerifiedLookups(f.Env).Skip(before).ToList();
        Assert.Equal(3, added.Count);
        Assert.All(added, c => Assert.Single(c.Addresses));
        Assert.Equal(3, plan.Items.Count);
    }

    [Fact]
    public async Task Plan_OneReCheckFails_ExcludesOnlyThatAddress()
    {
        var f = await FundSitesAsync(3);
        var failing = f.Findings[1].Address;
        f.Env.Chain.ThrowForAddresses.Add(failing);

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        Assert.Equal(2, plan.Items.Count);
        Assert.DoesNotContain(plan.Items, i => i.SourceAddress == failing);
        var excluded = Assert.Single(plan.Excluded);
        Assert.Equal(DigiIdSweepExclusionKind.LookupFailed, excluded.Kind);
        Assert.Equal(failing, excluded.SourceAddress);
    }

    [Fact]
    public async Task Plan_GivesEachItemItsOwnFreshUnusedReceiveAddress()
    {
        var f = await FundSitesAsync(3);
        var shown = await f.Service.GetReceivingAddressAsync();
        var nextIndex = f.Service.ActiveWallet!.NextReceivingIndex;

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        Assert.Equal(3, plan.Items.Select(i => i.DestinationAddress).Distinct().Count());
        Assert.DoesNotContain(shown, plan.Items.Select(i => i.DestinationAddress));
        Assert.All(plan.Items, i =>
        {
            Assert.True(i.DestinationIndex > nextIndex);
            Assert.Equal(f.Env.ReceiveAddress(i.DestinationIndex), i.DestinationAddress);
            Assert.StartsWith("dgb1", i.DestinationAddress);
        });
        Assert.Equal(plan.Items.Select(i => i.DestinationIndex).OrderBy(x => x), plan.Items.Select(i => i.DestinationIndex));
    }

    [Fact]
    public async Task Plan_SavesNothingAndSendsNothing_SoCancellingBurnsNoAddress()
    {
        var f = await FundSitesAsync(2);
        var indexBefore = f.Service.ActiveWallet!.NextReceivingIndex;
        var storedBefore = (await f.Service.GetWalletAsync(f.WalletId))!.NextReceivingIndex;

        await f.Service.PlanDigiIdSweepAsync(f.Findings);

        Assert.Equal(indexBefore, f.Service.ActiveWallet!.NextReceivingIndex);
        Assert.Equal(storedBefore, (await f.Service.GetWalletAsync(f.WalletId))!.NextReceivingIndex);
        Assert.Empty(f.Env.Chain.Broadcasts);
        Assert.Empty(await f.Env.SweepStore.GetAllAsync(f.WalletId));
    }

    [Fact]
    public async Task Plan_OtherOutputsAreNotInTheAmountOrTheFee()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var address = env.DigiIdAddress("example.com");
        env.Chain.AddUtxo(address, 5_000_000);
        env.Chain.AddUtxo(address, 9_000_000, scriptHex: "6a0b68656c6c6f20776f726c64");
        var findings = await service.ScanDigiIdFundsAsync(["example.com"]);

        var plan = await service.PlanDigiIdSweepAsync(findings);

        var item = Assert.Single(plan.Items);
        Assert.Equal(5_000_000, item.AmountSatoshis);
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(1, 100), item.EstimatedFeeSatoshis);
    }

    [Fact]
    public async Task Plan_FeeFollowsTheNumberOfOutputsOnTheAddress()
    {
        var f = await FundSitesAsync(1, amount: 2_000_000, utxosPerSite: 3);

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var item = Assert.Single(plan.Items);
        Assert.Equal(6_000_000, item.AmountSatoshis);
        Assert.Equal(DigiIdSweepPlanner.EstimateFee(3, 100), item.EstimatedFeeSatoshis);
    }

    [Fact]
    public async Task Plan_MoreAddressesThanFit_TakesWhatFitsAndReportsTheRest()
    {
        var f = await FundSitesAsync(25);

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        // The address the Receive page already shows is one of the 20 unused addresses a restore can reach, and the sweep must
        // leave a fresh one after the last destination, so a fresh wallet has room for 18 destinations, never more than 19.
        Assert.Equal(18, plan.Items.Count);
        Assert.InRange(plan.Items.Count, 1, DigiIdSweepPlanner.MaxItemsPerRound);
        Assert.Equal(7, plan.Remaining);
        Assert.Equal(25, plan.Items.Count + plan.Remaining);
        Assert.Equal(f.Service.ActiveWallet!.NextReceivingIndex + 1, plan.Items.Min(i => i.DestinationIndex));
    }

    [Fact]
    public async Task Plan_UnusedAddressesAlreadyHandedOut_LeaveLessRoom()
    {
        var f = await FundSitesAsync(12);
        for (var i = 0; i < 10; i++) await f.Service.GenerateNewReceivingAddressAsync(); // 10 unused addresses handed out

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        // The highest destination plus one must stay inside the 20-address window a restore scans past the last used address.
        Assert.True(plan.Items.Count < 12);
        Assert.Equal(12, plan.Items.Count + plan.Remaining);
        var info = f.Service.ActiveWallet!;
        Assert.True(plan.Items.Max(i => i.DestinationIndex) + 1 <= info.UsedReceivingCount + WalletService.ReceiveGapLimit - 1);
    }

    [Fact]
    public async Task Plan_TwentyUnusedAddressesInARow_SaysToUseOneFirst()
    {
        var f = await FundSitesAsync(2);
        try
        {
            for (var i = 0; i < 40; i++) await f.Service.GenerateNewReceivingAddressAsync();
        }
        catch (InvalidOperationException) { /* the wallet's own limit, reached */ }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PlanDigiIdSweepAsync(f.Findings));

        Assert.Contains("unused addresses in a row", ex.Message);
    }

    [Fact]
    public async Task Plan_FindingsThatCannotBeSwept_AreExcludedWithAReason()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var pendingOnly = env.DigiIdAddress("pending.example");
        var inFlight = env.DigiIdAddress("busy.example");
        env.Chain.AddUtxo(pendingOnly, 900_000, confirmations: 0);
        env.Chain.AddUtxo(inFlight, 5_000_000);
        await env.SweepStore.TryBeginAsync(service.ActiveWallet!.Id, new DigiIdSweepRecord
        {
            Id = "busy", Domain = "busy.example", SourceAddress = inFlight, DestinationAddress = env.ReceiveAddress(3),
            AmountSatoshis = 5_000_000, FeeSatoshis = 19_200, Status = DigiIdSweepStatus.Sending,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
        });
        var findings = await service.ScanDigiIdFundsAsync(["pending.example", "busy.example"]);

        var plan = await service.PlanDigiIdSweepAsync(findings);

        Assert.Empty(plan.Items);
        Assert.Contains(plan.Excluded, e => e.SourceAddress == pendingOnly && e.Kind == DigiIdSweepExclusionKind.NothingConfirmed);
        Assert.Contains(plan.Excluded, e => e.SourceAddress == inFlight && e.Kind == DigiIdSweepExclusionKind.InFlight);
    }

    [Fact]
    public async Task Plan_FundsTooSmallToSweep_AreExcluded_AndNothingIsPlanned()
    {
        var f = await FundSitesAsync(1, amount: 25_000);

        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        Assert.Empty(plan.Items);
        Assert.Equal(DigiIdSweepExclusionKind.TooSmall, Assert.Single(plan.Excluded).Kind);
        Assert.Empty(f.Env.Chain.Broadcasts);
    }

    [Fact]
    public async Task Plan_LockedWatchOnlyAndWifWallets_ThrowAndMakeNoLookup()
    {
        var locked = new DigiIdEnv();
        var lockedService = await locked.CreateLockedHdAsync();
        var watch = new DigiIdEnv();
        var watchService = await watch.CreateWatchOnlyAsync();
        var wif = new DigiIdEnv();
        var wifService = await wif.CreateWifAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => lockedService.PlanDigiIdSweepAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => watchService.PlanDigiIdSweepAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => wifService.PlanDigiIdSweepAsync([]));

        Assert.Empty(VerifiedLookups(locked));
        Assert.Empty(VerifiedLookups(watch));
        Assert.Empty(VerifiedLookups(wif));
    }

    // ─── Execute ───

    [Fact]
    public async Task Execute_SweepsEachAddressInItsOwnTransaction_ToItsOwnDestination()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(DigiIdSweepStatus.Pending, r.Status);
            Assert.False(string.IsNullOrEmpty(r.TxId));
        });
        Assert.Equal(2, f.Env.Chain.Broadcasts.Count);

        foreach (var item in plan.Items)
        {
            var record = records.Single(r => r.SourceAddress == item.SourceAddress);
            var tx = BroadcastFor(f, record);

            // Inputs come from this one address only, and there is one output, to its own destination, with no change.
            Assert.Equal(f.Utxos[item.SourceAddress].Select(u => u.TxId).OrderBy(x => x), tx.Inputs.Select(i => i.PrevOut.Hash.ToString()).OrderBy(x => x));
            var output = Assert.Single(tx.Outputs);
            Assert.Equal(item.DestinationAddress, output.ScriptPubKey.GetDestinationAddress(Net)!.ToString());
            Assert.Equal(record.AmountSatoshis - record.FeeSatoshis, output.Value.Satoshi);
            Assert.Equal(item.DestinationAddress, record.DestinationAddress);

            // The funds really moved.
            Assert.Equal(0, f.Env.Chain.UnspentCount(item.SourceAddress));
            Assert.Equal(output.Value.Satoshi, f.Env.Chain.UnspentSats(item.DestinationAddress));
        }

        // No transaction mixes the two addresses, and no destination is shared.
        Assert.Equal(2, f.Env.Chain.Broadcasts.SelectMany(t => t.Outputs.Select(o => o.ScriptPubKey.ToHex())).Distinct().Count());
    }

    [Fact]
    public async Task Execute_SignsWithTheKeyOfTheSite_SoTheNetworkWouldAcceptIt()
    {
        var f = await FundSitesAsync(1);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var record = Assert.Single(await f.Service.ExecuteDigiIdSweepAsync(plan));

        var tx = BroadcastFor(f, record);
        var utxo = f.Utxos[plan.Items[0].SourceAddress].Single();
        var coin = new Coin(new OutPoint(uint256.Parse(utxo.TxId), utxo.Vout),
            new TxOut(Money.Satoshis(utxo.Sats), BitcoinAddress.Create(plan.Items[0].SourceAddress, Net).ScriptPubKey));
        Assert.True(new DigiByteTransactionBuilder(Net).Verify(tx, [coin]));
    }

    [Fact]
    public async Task Execute_FeeIsAtOrAboveTheFloor_AndWithinTenPercentOfTheEstimate()
    {
        var f = await FundSitesAsync(2, utxosPerSite: 2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        foreach (var item in plan.Items)
        {
            var record = records.Single(r => r.SourceAddress == item.SourceAddress);
            var tx = BroadcastFor(f, record);
            Assert.True(record.FeeSatoshis >= tx.GetVirtualSize() * 99L, "fee rate is below the floor");
            Assert.InRange(record.FeeSatoshis, item.EstimatedFeeSatoshis * 0.9, item.EstimatedFeeSatoshis * 1.1);
            Assert.Equal(item.AmountSatoshis, record.AmountSatoshis);
        }
    }

    [Fact]
    public async Task Execute_SavesTheHighestDestinationPlusOne_BeforeTheFirstBroadcast_AndTheReceivePageNeverShowsADestination()
    {
        var f = await FundSitesAsync(3);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var highest = plan.Items.Max(i => i.DestinationIndex);
        int? savedAtFirstBroadcast = null;
        f.Env.Chain.OnBeforeBroadcast = n =>
        {
            if (n == 1) savedAtFirstBroadcast = f.Service.GetWalletAsync(f.WalletId).GetAwaiter().GetResult()!.NextReceivingIndex;
        };

        await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(highest + 1, savedAtFirstBroadcast);
        Assert.Equal(highest + 1, f.Service.ActiveWallet!.NextReceivingIndex);
        Assert.DoesNotContain(await f.Service.GetReceivingAddressAsync(), plan.Items.Select(i => i.DestinationAddress));
    }

    [Fact]
    public async Task Execute_CannotSaveTheNewAddressIndex_SendsNothingAndRecordsNothing()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var indexBefore = f.Service.ActiveWallet!.NextReceivingIndex;
        f.Env.Storage.FailWalletInfoWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ExecuteDigiIdSweepAsync(plan));

        Assert.Empty(f.Env.Chain.Broadcasts);
        Assert.Empty(await f.Env.SweepStore.GetAllAsync(f.WalletId));
        Assert.Equal(indexBefore, f.Service.ActiveWallet!.NextReceivingIndex);
        Assert.All(f.Utxos.Keys, a => Assert.Equal(1, f.Env.Chain.UnspentCount(a)));
    }

    [Fact]
    public async Task Execute_OneBroadcastRejected_ThatRecordFails_TheFundsStay_AndTheOtherStillCompletes()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var rejected = plan.Items[0].SourceAddress;
        var fine = plan.Items[1].SourceAddress;
        f.Env.Chain.RejectBroadcastFrom.Add(rejected);

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        var failed = records.Single(r => r.SourceAddress == rejected);
        Assert.Equal(DigiIdSweepStatus.Failed, failed.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.FailureReason));
        Assert.DoesNotContain("rejected by the network", failed.FailureReason); // never the raw exception text
        Assert.Equal(1, f.Env.Chain.UnspentCount(rejected));
        Assert.Equal(DigiIdSweepStatus.Pending, records.Single(r => r.SourceAddress == fine).Status);
        Assert.Equal(0, f.Env.Chain.UnspentCount(fine));
        Assert.Equal(2, (await f.Env.SweepStore.GetAllAsync(f.WalletId)).Count);
    }

    [Fact]
    public async Task Execute_AFailedAddress_CanBeSweptAgainLater()
    {
        var f = await FundSitesAsync(1);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var source = plan.Items[0].SourceAddress;
        f.Env.Chain.RejectBroadcastFrom.Add(source);
        await f.Service.ExecuteDigiIdSweepAsync(plan);
        f.Env.Chain.RejectBroadcastFrom.Clear();

        var findings = await f.Service.ScanDigiIdFundsAsync(f.Sites);
        Assert.False(findings.Single().InFlight);
        var again = await f.Service.PlanDigiIdSweepAsync(findings);
        var records = await f.Service.ExecuteDigiIdSweepAsync(again);

        Assert.Equal(DigiIdSweepStatus.Pending, Assert.Single(records).Status);
        Assert.Equal(0, f.Env.Chain.UnspentCount(source));
    }

    [Fact]
    public async Task Execute_ForFundsThatAreGoneByNow_FailsThatAddressWithoutSendingAnything()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var gone = plan.Items[0].SourceAddress;
        foreach (var u in f.Utxos[gone]) f.Env.Chain.Drop(u.TxId);

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(DigiIdSweepStatus.Failed, records.Single(r => r.SourceAddress == gone).Status);
        Assert.Single(f.Env.Chain.Broadcasts);
    }

    [Fact]
    public async Task Execute_OtherOutputsOnTheAddress_AreNeverSpent()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();
        var address = env.DigiIdAddress("example.com");
        env.Chain.AddUtxo(address, 5_000_000);
        env.Chain.AddUtxo(address, 9_000_000, scriptHex: "6a0b68656c6c6f20776f726c64");
        var findings = await service.ScanDigiIdFundsAsync(["example.com"]);
        var plan = await service.PlanDigiIdSweepAsync(findings);

        var record = Assert.Single(await service.ExecuteDigiIdSweepAsync(plan));

        Assert.Equal(DigiIdSweepStatus.Pending, record.Status);
        var tx = Assert.Single(env.Chain.Broadcasts);
        Assert.Single(tx.Inputs);
        Assert.Equal(1, env.Chain.UnspentCount(address));
        Assert.Equal(9_000_000, env.Chain.UnspentSats(address));
    }

    [Fact]
    public async Task Execute_ASecondRunForAddressesAlreadyBeingSwept_SendsNothingAndReturnsTheExistingRecords()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var first = await f.Service.ExecuteDigiIdSweepAsync(plan);

        var second = await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(2, f.Env.Chain.Broadcasts.Count);
        Assert.Equal(first.Select(r => r.Id).OrderBy(x => x), second.Select(r => r.Id).OrderBy(x => x));
        Assert.Equal(2, (await f.Env.SweepStore.GetAllAsync(f.WalletId)).Count);
    }

    [Fact]
    public async Task Execute_TwoRunsAtTheSameTime_SendOneTransactionPerAddress()
    {
        var f = await FundSitesAsync(3);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        await Task.WhenAll(f.Service.ExecuteDigiIdSweepAsync(plan), f.Service.ExecuteDigiIdSweepAsync(plan));

        Assert.Equal(3, f.Env.Chain.Broadcasts.Count);
        Assert.Equal(3, (await f.Env.SweepStore.GetAllAsync(f.WalletId)).Count);
    }

    [Fact]
    public async Task Execute_RecordsEachBroadcastInTheNormalTransactionHistory()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        var tracker = new TransactionTracker(f.Env.Storage);
        tracker.SetActiveWallet(f.WalletId);
        var history = await tracker.GetAllAsync();
        foreach (var record in records)
        {
            var entry = Assert.Single(history, t => t.TxId == record.TxId);
            Assert.Equal("Digi-ID recovery sweep", entry.Memo);
            Assert.Equal(record.FeeSatoshis, entry.FeeSatoshis);
            Assert.Equal(record.DestinationAddress, entry.CounterpartyAddress);
        }
    }

    [Fact]
    public async Task Execute_ReportsEachFinishedRecord()
    {
        var f = await FundSitesAsync(3);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var reported = new List<DigiIdSweepRecord>();

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan, new RecordProgress(reported.Add));

        Assert.Equal(records.Count, reported.Count);
        Assert.All(reported, r => Assert.NotEqual(DigiIdSweepStatus.Sending, r.Status));
    }

    [Fact]
    public async Task Execute_EmptyPlan_DoesNothing()
    {
        var f = await FundSitesAsync(1);
        var indexBefore = f.Service.ActiveWallet!.NextReceivingIndex;

        var records = await f.Service.ExecuteDigiIdSweepAsync(new DigiIdSweepPlan { Items = [], FeeRateSatPerVByte = 100 });

        Assert.Empty(records);
        Assert.Equal(indexBefore, f.Service.ActiveWallet!.NextReceivingIndex);
        Assert.Empty(f.Env.Chain.Broadcasts);
    }

    [Fact]
    public async Task Execute_LogsNoKeyAndNoRawTransaction()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        IReadOnlyList<DigiIdSweepRecord> records;
        try { records = await f.Service.ExecuteDigiIdSweepAsync(plan); }
        finally { Console.SetOut(original); }

        var output = captured.ToString();
        foreach (var tx in f.Env.Chain.Broadcasts) Assert.DoesNotContain(tx.ToHex(), output);
        foreach (var site in f.Sites)
        {
            var key = f.Env.Hd.DeriveDigiIdKey(DigiByte.Crypto.DigiId.DigiIdService.DeriveSiteIndex(site)).PrivateKey;
            Assert.DoesNotContain(key.GetWif(Net).ToString(), output);
            Assert.DoesNotContain(Convert.ToHexString(key.ToBytes()), output, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task Execute_TheWalletLocksPartWay_NothingMoreIsSent_TheRestFailWithWalletLocked_AndCanBeSweptAfterUnlocking()
    {
        var f = await FundSitesAsync(3);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        f.Env.Chain.OnBeforeBroadcast = n => { if (n == 1) f.Service.LockWalletAsync().GetAwaiter().GetResult(); };

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(3, records.Count);
        Assert.Equal(DigiIdSweepStatus.Pending, records[0].Status);
        Assert.All(records.Skip(1), r =>
        {
            Assert.Equal(DigiIdSweepStatus.Failed, r.Status);
            Assert.Equal("wallet locked", r.FailureReason);
        });
        Assert.Single(f.Env.Chain.Broadcasts);

        f.Env.Chain.OnBeforeBroadcast = null;
        Assert.True(await f.Service.UnlockWalletAsync(f.WalletId, DigiIdEnv.Pin));
        var findings = await f.Service.ScanDigiIdFundsAsync(f.Sites);
        Assert.Equal(2, findings.Count(x => x.CanSweep));
        var again = await f.Service.ExecuteDigiIdSweepAsync(await f.Service.PlanDigiIdSweepAsync(findings));

        Assert.Equal(2, again.Count);
        Assert.All(again, r => Assert.Equal(DigiIdSweepStatus.Pending, r.Status));
        Assert.Equal(3, f.Env.Chain.Broadcasts.Count);
    }

    [Fact]
    public async Task Execute_TheWalletIsLockedBeforeItStarts_ThrowsSendsNothingAndRecordsNothing()
    {
        var f = await FundSitesAsync(2);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        await f.Service.LockWalletAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ExecuteDigiIdSweepAsync(plan));

        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Env.Chain.Broadcasts);
        Assert.Empty(await f.Env.SweepStore.GetAllAsync(f.WalletId));
    }

    [Fact]
    public async Task Execute_WatchOnlyAndWifWallets_ThrowAndSendNothing()
    {
        var watch = new DigiIdEnv();
        var watchService = await watch.CreateWatchOnlyAsync();
        var wif = new DigiIdEnv();
        var wifService = await wif.CreateWifAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => watchService.ExecuteDigiIdSweepAsync(OneItemPlan(watch)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => wifService.ExecuteDigiIdSweepAsync(OneItemPlan(wif)));

        Assert.Empty(watch.Chain.Broadcasts);
        Assert.Empty(wif.Chain.Broadcasts);
    }

    [Fact]
    public async Task Execute_ThePlannedSourceIsNotTheSitesAddress_FailsWithoutSigning()
    {
        // A plan edited to point at an address the wallet does not derive for that site must not be signed for.
        var f = await FundSitesAsync(1);
        var other = f.Env.DigiIdAddress("some-other-site.example");
        f.Env.Chain.AddUtxo(other, 5_000_000);
        var item = (await f.Service.PlanDigiIdSweepAsync(f.Findings)).Items[0];
        var forged = new DigiIdSweepPlan { Items = [item with { SourceAddress = other }], FeeRateSatPerVByte = 100 };

        var records = await f.Service.ExecuteDigiIdSweepAsync(forged);

        Assert.Equal(DigiIdSweepStatus.Failed, Assert.Single(records).Status);
        Assert.Empty(f.Env.Chain.Broadcasts);
    }

    // ─── Refresh ───

    private static async Task<(Funded F, DigiIdSweepRecord Record)> SweptOnceAsync()
    {
        var f = await FundSitesAsync(1);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        var record = Assert.Single(await f.Service.ExecuteDigiIdSweepAsync(plan));
        return (f, record);
    }

    [Fact]
    public async Task Refresh_AConfirmedTransaction_BecomesConfirmed()
    {
        var (f, record) = await SweptOnceAsync();
        f.Env.Chain.Confirm(record.TxId!, 1);

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Confirmed, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_StillUnconfirmed_StaysPending()
    {
        var (f, _) = await SweptOnceAsync();

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Pending, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_ALookupFailure_LeavesItPending_AndDoesNotThrow()
    {
        var (f, record) = await SweptOnceAsync();
        f.Env.Chain.Confirm(record.TxId!, 3);
        f.Env.Chain.TransactionLookupFails = true;

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Pending, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_ATransactionTheNetworkNoLongerKnows_FailsOnceTheGraceHasPassed()
    {
        var (f, record) = await SweptOnceAsync();
        f.Service.DigiIdSweepDropGrace = TimeSpan.Zero;
        f.Env.Chain.Drop(record.TxId!);

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        var failed = Assert.Single(records);
        Assert.Equal(DigiIdSweepStatus.Failed, failed.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.FailureReason));
    }

    [Fact]
    public async Task Refresh_ATransactionNotYetKnown_StaysPendingDuringTheGrace()
    {
        var (f, record) = await SweptOnceAsync();
        f.Env.Chain.Drop(record.TxId!); // a slow explorer has not seen it yet; the default grace applies

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Pending, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_LooksUpEachPendingTransactionOnItsOwn_AndNeverAFinishedOne()
    {
        var f = await FundSitesAsync(3);
        var records = await f.Service.ExecuteDigiIdSweepAsync(await f.Service.PlanDigiIdSweepAsync(f.Findings));
        f.Env.Chain.Confirm(records[0].TxId!, 2);
        f.Env.Chain.Calls.Clear();

        await f.Service.RefreshDigiIdSweepsAsync();
        var lookups = f.Env.Chain.Calls.Where(c => c.Method == nameof(IBlockchainService.GetTransactionAsync)).ToList();
        Assert.Equal(3, lookups.Count);
        Assert.All(lookups, c => Assert.Single(c.Addresses));

        f.Env.Chain.Calls.Clear();
        await f.Service.RefreshDigiIdSweepsAsync();
        // One is Confirmed now and is not asked about again.
        Assert.Equal(2, f.Env.Chain.Calls.Count(c => c.Method == nameof(IBlockchainService.GetTransactionAsync)));
    }

    [Fact]
    public async Task Refresh_AnInterruptedRun_WithTheFundsStillThere_BecomesFailedAndTheAddressIsOfferedAgain()
    {
        var f = await FundSitesAsync(1);
        var source = f.Findings[0].Address;
        await f.Env.SweepStore.TryBeginAsync(f.WalletId, new DigiIdSweepRecord
        {
            Id = "interrupted", Domain = f.Sites[0], SourceAddress = source, DestinationAddress = f.Env.ReceiveAddress(4),
            AmountSatoshis = 5_000_000, FeeSatoshis = 19_200, Status = DigiIdSweepStatus.Sending,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
        });

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Failed, Assert.Single(records).Status);
        Assert.False((await f.Service.ScanDigiIdFundsAsync(f.Sites)).Single().InFlight);
    }

    [Fact]
    public async Task Refresh_AnInterruptedRun_WithTheFundsGone_IsLeftForTheUserToCheckInHistory()
    {
        var f = await FundSitesAsync(1);
        var source = f.Findings[0].Address;
        foreach (var u in f.Utxos[source]) f.Env.Chain.Drop(u.TxId);
        await f.Env.SweepStore.TryBeginAsync(f.WalletId, new DigiIdSweepRecord
        {
            Id = "interrupted", Domain = f.Sites[0], SourceAddress = source, DestinationAddress = f.Env.ReceiveAddress(4),
            AmountSatoshis = 5_000_000, FeeSatoshis = 19_200, Status = DigiIdSweepStatus.Sending,
            CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
        });

        var records = await f.Service.RefreshDigiIdSweepsAsync();

        Assert.Equal(DigiIdSweepStatus.Sending, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_WhileASweepIsRunning_DoesNotFailTheRecordThatIsBeingSent()
    {
        var f = await FundSitesAsync(1);
        var plan = await f.Service.PlanDigiIdSweepAsync(f.Findings);
        IReadOnlyList<DigiIdSweepRecord>? during = null;
        f.Env.Chain.OnBeforeBroadcast = _ => during = f.Service.RefreshDigiIdSweepsAsync().GetAwaiter().GetResult();

        var records = await f.Service.ExecuteDigiIdSweepAsync(plan);

        Assert.Equal(DigiIdSweepStatus.Sending, Assert.Single(during!).Status);
        Assert.Equal(DigiIdSweepStatus.Pending, Assert.Single(records).Status);
    }

    [Fact]
    public async Task Refresh_AfterAReload_StillShowsTheSweep()
    {
        var (f, record) = await SweptOnceAsync();
        await f.Service.LockWalletAsync();

        var reloaded = f.Env.NewService();
        Assert.True(await reloaded.UnlockWalletAsync(f.WalletId, DigiIdEnv.Pin));
        var records = await reloaded.RefreshDigiIdSweepsAsync();

        Assert.Equal(record.Id, Assert.Single(records).Id);
        Assert.Equal(DigiIdSweepStatus.Pending, records[0].Status);
    }

    [Fact]
    public async Task GetSweeps_ReturnsTheSavedRecordsAtOnce_WithNoNetworkLookup()
    {
        var (f, record) = await SweptOnceAsync();
        f.Env.Chain.Calls.Clear();

        var records = await f.Service.GetDigiIdSweepsAsync();

        Assert.Equal(record.Id, Assert.Single(records).Id);
        Assert.Empty(f.Env.Chain.Calls);
    }

    [Fact]
    public async Task GetSweeps_NothingSaved_IsEmpty()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        Assert.Empty(await service.GetDigiIdSweepsAsync());
    }

    [Fact]
    public async Task GetSweeps_LockedWallet_Throws()
    {
        var (f, _) = await SweptOnceAsync();
        await f.Service.LockWalletAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.GetDigiIdSweepsAsync());
    }

    [Fact]
    public async Task Refresh_LockedWallet_Throws()
    {
        var (f, _) = await SweptOnceAsync();
        await f.Service.LockWalletAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.RefreshDigiIdSweepsAsync());
    }

    [Fact]
    public async Task Refresh_NothingSweptYet_IsEmptyAndMakesNoLookup()
    {
        var env = new DigiIdEnv();
        var service = await env.CreateUnlockedHdAsync();

        var records = await service.RefreshDigiIdSweepsAsync();

        Assert.Empty(records);
        Assert.Empty(env.Chain.Calls);
    }

    private sealed class RecordProgress(Action<DigiIdSweepRecord> report) : IProgress<DigiIdSweepRecord>
    {
        public void Report(DigiIdSweepRecord value) => report(value);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}

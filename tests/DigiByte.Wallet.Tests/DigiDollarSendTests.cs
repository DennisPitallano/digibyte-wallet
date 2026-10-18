using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T030 (User Story 2): validation, quoting and sending, input reservation, the rate limit, and the pending-send lifecycle
/// (FR-011..FR-017, FR-026, FR-027; research R13, R15).
/// </summary>
public class DigiDollarSendTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Env
    {
        public TestMemoryStorage Storage { get; } = new();
        public FakeDdNodeApi Api { get; } = new();
        public FakeDdFeeSource Fees { get; } = new();
        public FakeSessionProvider Sessions { get; }
        public DigiDollarStore Store { get; }
        public DigiDollarKeyDerivation Hd => Sessions.Session!.Derivation!;
        public DateTimeOffset Clock { get; set; } = Now;
        public DigiDollarService Service { get; }
        private int _funded;

        public Env(FakeSessionProvider? sessions = null, bool feeSource = true)
        {
            Sessions = sessions ?? FakeSessionProvider.Seed();
            Store = new DigiDollarStore(Storage, new TestAesCrypto());
            Service = new DigiDollarService(Sessions, Api, Store, feeSource ? Fees : null, () => Clock);
        }

        public string Recv(int i) => Hd.GetAddress(Hd.Derive(DigiDollarChain.Receive, i));
        public string Chg(int i) => Hd.GetAddress(Hd.Derive(DigiDollarChain.Change, i));

        /// <summary>Someone else's valid testnet DigiDollar address.</summary>
        public static string Recipient()
        {
            var other = FakeSessionProvider.Derivation("testnet", passphrase: "someone-else");
            return other.GetAddress(other.Derive(DigiDollarChain.Receive, 0));
        }

        public string Secret() => DigiDollarStore.DeriveStoreSecret(Hd.StorageKeyMaterial());
        public Task<DigiDollarState?> Saved() => Store.LoadAsync("w1", Secret());

        /// <summary>A DD output on a receive address, plus the history entry that lets the restore scan find the address.</summary>
        public string Fund(int index, long cents, int confirmations = 6, bool spentByMempool = false)
        {
            var txid = (++_funded).ToString("x").PadLeft(64, 'a');
            var address = Recv(index);
            if (!Api.Utxos.TryGetValue(address, out var list)) Api.Utxos[address] = list = [];
            list.Add(new DigiDollarUtxo(txid, 0, address, cents, "5120" + new string('a', 64), 0, confirmations, spentByMempool));
            Api.History.Add(new DdHistoryEntry(txid, confirmations > 0 ? 900 : null, confirmations > 0 ? Now.AddHours(-1) : null, confirmations, null,
                [new DdAmountAt("TDexternalexternalexternalexternalexternal00", cents)], [new DdAmountAt(address, cents)]));
            return txid;
        }

        public async Task<Env> ReadyAsync()
        {
            await Service.RefreshAsync(); // restore scan: learns the funded addresses
            return this;
        }
    }

    private static Transaction Parse(string hex) => Transaction.Parse(hex, DigiByteNetwork.Testnet);

    // ---------- validation (FR-006, FR-011..013, FR-016) ----------

    [Fact]
    public async Task NoWalletOrNonSeedWallet_IsNotSupported()
    {
        foreach (var sessions in new[] { new FakeSessionProvider(), FakeSessionProvider.Kind(DdWalletKind.WatchOnly) })
        {
            var env = new Env(sessions);
            Assert.Equal(DdSendIssue.NotSupported, (await env.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);
            var ex = await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync(Env.Recipient(), 1000));
            Assert.Equal(DdSendIssue.NotSupported, ex.Issue);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("dgbt1qw508d6qejxtdg4y5r3zarvary0c5xw7kxxxxxx")]
    [InlineData("DGBFakeLegacyAddress1234567890123456")]
    public async Task NotADigiDollarAddress_IsRejected(string recipient)
    {
        var env = await new Env().ReadyAsync();
        Assert.Equal(DdSendIssue.NotDdAddress, (await env.Service.CheckSendAsync(recipient, 1000)).Issue);
    }

    [Fact]
    public async Task AddressWithSurroundingWhitespace_IsRejected()
    {
        var env = await new Env().ReadyAsync();
        Assert.Equal(DdSendIssue.NotDdAddress, (await env.Service.CheckSendAsync(" " + Env.Recipient(), 1000)).Issue);
        Assert.Equal(DdSendIssue.NotDdAddress, (await env.Service.CheckSendAsync(Env.Recipient() + "\n", 1000)).Issue);
    }

    [Fact]
    public async Task AddressForTheOtherNetwork_IsAWrongNetworkError_NamingTheMismatch()
    {
        var env = await new Env().ReadyAsync();
        var mainnet = FakeSessionProvider.Derivation("mainnet");
        var mainnetAddress = mainnet.GetAddress(mainnet.Derive(DigiDollarChain.Receive, 0));

        Assert.Equal(DdSendIssue.WrongNetwork, (await env.Service.CheckSendAsync(mainnetAddress, 1000)).Issue);
    }

    [Theory]
    [InlineData(99, DdSendIssue.BelowMinimum)]
    [InlineData(0, DdSendIssue.BelowMinimum)]
    [InlineData(-5, DdSendIssue.BelowMinimum)]
    [InlineData(10_000_001, DdSendIssue.AboveMaximum)]
    public async Task AmountOutsideTheLimits_IsRejected_BeforeAnyNetworkCall(long amount, DdSendIssue expected)
    {
        var env = await new Env().ReadyAsync();
        var callsBefore = env.Api.DataCalls;

        Assert.Equal(expected, (await env.Service.CheckSendAsync(Env.Recipient(), amount)).Issue);
        Assert.Equal(callsBefore, env.Api.DataCalls);
    }

    [Fact]
    public async Task BackendUnreachable_IsOffline()
    {
        var env = new Env();
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        env.Api.Down = true;

        Assert.Equal(DdSendIssue.Offline, (await env.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);
    }

    [Fact]
    public async Task DeploymentNotActive_IsNotActive()
    {
        var env = new Env();
        env.Api.Health = env.Api.Health with { DeploymentActive = false };
        Assert.Equal(DdSendIssue.NotActive, (await env.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);
    }

    [Fact]
    public async Task MoreThanAvailable_IsInsufficient_ReportingWhatIsAvailable()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 3000);

        Assert.Equal(DdSendIssue.InsufficientDd, check.Issue);
        Assert.Equal(2500, check.AvailableCents);
    }

    [Fact]
    public async Task UnconfirmedAndMempoolSpentOutputs_AreNotSpendable()
    {
        var env = new Env();
        env.Fund(0, 1000, confirmations: 0);
        env.Fund(0, 2000, confirmations: 6, spentByMempool: true);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.Equal(DdSendIssue.InsufficientDd, check.Issue);
        Assert.Equal(0, check.AvailableCents);
    }

    [Fact]
    public async Task ChangeUnderOneDollar_IsBlocked_AndTheSendAllAmountIsOffered()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 2450);

        Assert.Equal(DdSendIssue.ChangeBelowMinimum, check.Issue);
        Assert.Equal(2500, check.AvailableCents);
        Assert.Equal(2500, await env.Service.GetSendAllAmountAsync());
    }

    [Fact]
    public async Task NotEnoughDgbForTheFee_IsBlocked()
    {
        var none = new Env(feeSource: false);
        none.Fund(0, 2500);
        await none.ReadyAsync();
        Assert.Equal(DdSendIssue.InsufficientDgbFee, (await none.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);

        var low = new Env();
        low.Fund(0, 2500);
        low.Fees.Add(1, 9_999_999);
        await low.ReadyAsync();
        Assert.Equal(DdSendIssue.InsufficientDgbFee, (await low.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);
    }

    [Fact]
    public async Task ValidSend_IsQuoted_WithTheAutomaticFee_AndChange()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.True(check.IsOk);
        Assert.Equal(DigiDollarTransferBuilder.MinFeeSats, check.FeeSats); // 0.1 DGB, never user-set
        Assert.Equal(1500, check.ChangeCents);
        Assert.Equal(2500, check.AvailableCents);
        Assert.False(check.IsOwnAddress);
        Assert.Empty(check.Degraded);
    }

    [Fact]
    public async Task Check_DoesNotBroadcast_AndDoesNotPersistAPendingSend()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.Empty(env.Api.Broadcasts);
        Assert.Empty((await env.Saved())!.PendingSends);
    }

    [Fact]
    public async Task SendingToOneOfYourOwnAddresses_IsFlagged()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(env.Recv(0), 1000);

        Assert.True(check.IsOk);
        Assert.True(check.IsOwnAddress);
    }

    [Fact]
    public async Task DegradedNetwork_StillAllowsTheSend_ButNamesEveryCondition()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        env.Api.Health = env.Api.Health with
        {
            ErrActive = true, VolatilityFreeze = true, OracleHeight = 900, OraclePriceMicroUsd = null,
        };
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.True(check.IsOk); // FR-027: allowed, with extra warnings
        Assert.Contains(DdDegradedCondition.Err, check.Degraded);
        Assert.Contains(DdDegradedCondition.VolatilityFreeze, check.Degraded);
        Assert.Contains(DdDegradedCondition.OracleStale, check.Degraded);
        Assert.Contains(DdDegradedCondition.PriceUnavailable, check.Degraded);
    }

    [Fact]
    public async Task StaleOracleAlone_IsReported()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        env.Api.Health = env.Api.Health with { NodeHeight = 1000, OracleHeight = 970 };
        await env.ReadyAsync();

        var check = await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.Equal([DdDegradedCondition.OracleStale], check.Degraded);
    }

    // ---------- sending (FR-011, FR-017, FR-026) ----------

    [Fact]
    public async Task Send_BuildsSignsLocally_Broadcasts_AndRecordsAPendingSend()
    {
        var env = new Env();
        var fundingTxid = env.Fund(0, 2500);
        var feeUtxo = env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        var recipient = Env.Recipient();

        var result = await env.Service.SendAsync(recipient, 1000);

        Assert.Equal(1000, result.AmountCents);
        Assert.Equal(1500, result.ChangeCents);
        Assert.Equal(DigiDollarTransferBuilder.MinFeeSats, result.FeeSats);

        // what was broadcast: a DigiDollar transfer paying the recipient and returning change
        var tx = Parse(Assert.Single(env.Api.Broadcasts));
        Assert.Equal(0x02000770u, tx.Version);
        Assert.True(DigiDollarScripts.TryParseOpReturn(tx.Outputs[^1].ScriptPubKey, out var type, out var amounts));
        Assert.Equal(DigiDollarTxType.Transfer, type);
        Assert.Equal(new long[] { 1000, 1500 }, amounts);
        Assert.Equal(fundingTxid, tx.Inputs[0].PrevOut.Hash.ToString());
        Assert.Equal(feeUtxo.Txid, tx.Inputs[1].PrevOut.Hash.ToString());

        // what was recorded
        var state = (await env.Saved())!;
        var pending = Assert.Single(state.PendingSends);
        Assert.Equal(result.Txid, pending.Txid);
        Assert.Equal(1000, pending.AmountCents);
        Assert.Equal(recipient, pending.Recipient);
        Assert.Equal(DdTxStatus.Pending, pending.Status);
        Assert.Equal(Now, pending.SubmittedAt);
        Assert.Contains(new DdOutpoint(fundingTxid, 0), pending.SpentOutpoints);
        Assert.Contains(new DdOutpoint(feeUtxo.Txid, 0), pending.SpentOutpoints);
        Assert.Equal(Convert.ToHexString(tx.ToBytes()).ToLowerInvariant(), pending.RawTxHex);

        // the change address is remembered and the change index advances (a fresh change key every send)
        Assert.Equal(1, state.Account.NextChangeIndex);
        Assert.Contains(state.Addresses, a => a.Chain == DigiDollarChain.Change && a.Index == 0 && a.Address == env.Chg(0));
    }

    [Fact]
    public async Task Send_PaysTheChangeToAFreshKey_EachTime()
    {
        var env = new Env();
        env.Fund(0, 5000);
        env.Fund(1, 5000);
        env.Fees.Add(1, 50_000_000);
        env.Fees.Add(2, 50_000_000);
        await env.ReadyAsync();

        await env.Service.SendAsync(Env.Recipient(), 1000);
        await env.Service.SendAsync(Env.Recipient(), 1000);

        var changeKeys = env.Api.Broadcasts.Select(h => Parse(h).Outputs[1].ScriptPubKey.ToHex()).ToList();
        Assert.Equal(2, changeKeys.Distinct().Count());
        Assert.Equal(2, (await env.Saved())!.Account.NextChangeIndex);
    }

    [Fact]
    public async Task SendAll_UsesTheWholeBalance_WithNoChange()
    {
        var env = new Env();
        env.Fund(0, 1000);
        env.Fund(1, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var all = await env.Service.GetSendAllAmountAsync();
        var result = await env.Service.SendAsync(Env.Recipient(), all);

        Assert.Equal(3500, all);
        Assert.Equal(0, result.ChangeCents);
        var tx = Parse(Assert.Single(env.Api.Broadcasts));
        Assert.True(DigiDollarScripts.TryParseOpReturn(tx.Outputs[^1].ScriptPubKey, out _, out var amounts));
        Assert.Equal(new long[] { 3500 }, amounts);
        Assert.Equal(0, (await env.Saved())!.Account.NextChangeIndex); // no DD change address was used
    }

    [Fact]
    public async Task Send_AvoidsSubMinimumChange_ByUsingAnotherOutput()
    {
        var env = new Env();
        env.Fund(0, 1000);
        env.Fund(1, 1450);
        env.Fund(2, 400);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var result = await env.Service.SendAsync(Env.Recipient(), 2400);

        Assert.True(result.ChangeCents == 0 || result.ChangeCents >= 100);
    }

    [Fact]
    public async Task Send_ReservesItsInputs_SoTheSameOutputCannotBeSpentTwice()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        env.Fees.Add(2, 50_000_000);
        await env.ReadyAsync();

        await env.Service.SendAsync(Env.Recipient(), 1000);
        var second = await env.Service.CheckSendAsync(Env.Recipient(), 1000);

        Assert.Equal(DdSendIssue.InsufficientDd, second.Issue); // the only output is reserved by the pending send
        Assert.Equal(0, second.AvailableCents);
        var dashboard = await env.Service.RefreshAsync();
        Assert.Equal(0, dashboard.AvailableCents);
    }

    [Fact]
    public async Task Send_ReservesItsDgbFeeInput_Too()
    {
        var env = new Env();
        env.Fund(0, 5000);
        env.Fund(1, 5000);
        env.Fees.Add(1, 50_000_000); // only one fee output
        await env.ReadyAsync();

        await env.Service.SendAsync(Env.Recipient(), 5000);
        var second = await env.Service.CheckSendAsync(Env.Recipient(), 5000);

        Assert.Equal(DdSendIssue.InsufficientDgbFee, second.Issue); // its fee output is held by the pending send
    }

    [Fact]
    public async Task Send_ShowsAsAPendingEntryInHistory_Immediately()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        var recipient = Env.Recipient();

        var result = await env.Service.SendAsync(recipient, 1000);
        var dashboard = await env.Service.RefreshAsync();

        var entry = dashboard.History.First(t => t.Txid == result.Txid);
        Assert.Equal(DdDirection.Sent, entry.Direction);
        Assert.Equal(DdTxStatus.Pending, entry.Status);
        Assert.Equal(1000, entry.AmountCents);
        Assert.Equal(recipient, entry.CounterpartyAddress);
        Assert.Equal(DigiDollarTransferBuilder.MinFeeSats, entry.FeeSats);
    }

    [Fact]
    public async Task Send_ThatFailsValidation_ThrowsWithTheIssue_AndBroadcastsNothing()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();

        var ex = await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync("nope", 1000));

        Assert.Equal(DdSendIssue.NotDdAddress, ex.Issue);
        Assert.Empty(env.Api.Broadcasts);
        Assert.Empty((await env.Saved())!.PendingSends);
    }

    [Fact]
    public async Task NodeRejection_SurfacesTheReason_AndRecordsNothing()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        env.Api.BroadcastError = "transfer-dd-conservation-violation";

        var ex = await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync(Env.Recipient(), 1000));

        Assert.Equal(DdSendIssue.Rejected, ex.Issue);
        Assert.Equal("transfer-dd-conservation-violation", ex.Detail);
        var state = (await env.Saved())!;
        Assert.Empty(state.PendingSends);
        Assert.Equal(0, state.Account.NextChangeIndex);

        // nothing was reserved, so the user can simply try again
        env.Api.BroadcastError = null;
        await env.Service.SendAsync(Env.Recipient(), 1000);
        Assert.Single(env.Api.Broadcasts);
    }

    [Fact]
    public async Task BackendDroppingAtBroadcast_IsOffline_AndRecordsNothing()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        env.Api.BroadcastDown = true;

        var ex = await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync(Env.Recipient(), 1000));

        Assert.Equal(DdSendIssue.Offline, ex.Issue);
        Assert.Empty((await env.Saved())!.PendingSends);
    }

    [Fact]
    public async Task PrivateKeys_AreNeverInTheStoredState()
    {
        var env = new Env();
        env.Fund(0, 2500);
        var fee = env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        await env.Service.SendAsync(Env.Recipient(), 1000);

        var json = System.Text.Json.JsonSerializer.Serialize(await env.Saved());

        Assert.DoesNotContain(Convert.ToHexString(fee.PrivateKey.ToBytes()), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToHexString(env.Hd.Derive(DigiDollarChain.Receive, 0).OutputKey.ToBytes()), json.Replace("\"", ""), StringComparison.Ordinal);
    }

    // ---------- rate limit (FR-016) ----------

    private static async Task<Env> SixOutputsAsync()
    {
        var env = new Env();
        for (var i = 0; i < 6; i++)
        {
            env.Fund(i, 1000);
            env.Fees.Add(i + 1, 50_000_000);
        }
        await env.ReadyAsync();
        return env;
    }

    [Fact]
    public async Task FiveSendsInAMinute_AreAllowed_TheSixthIsBlocked_UntilTheWindowPasses()
    {
        var env = await SixOutputsAsync();
        for (var i = 0; i < 5; i++)
        {
            env.Clock = Now.AddSeconds(i * 5);
            await env.Service.SendAsync(Env.Recipient(), 1000);
        }

        env.Clock = Now.AddSeconds(30);
        var blocked = await env.Service.CheckSendAsync(Env.Recipient(), 1000);
        Assert.Equal(DdSendIssue.RateLimited, blocked.Issue);
        var ex = await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync(Env.Recipient(), 1000));
        Assert.Equal(DdSendIssue.RateLimited, ex.Issue);

        env.Clock = Now.AddSeconds(61);
        Assert.True((await env.Service.CheckSendAsync(Env.Recipient(), 1000)).IsOk);
    }

    [Fact]
    public async Task FailedValidations_DoNotUseUpTheRateLimit()
    {
        var env = await SixOutputsAsync();
        for (var i = 0; i < 8; i++)
            await Assert.ThrowsAsync<DdSendException>(() => env.Service.SendAsync("bad-address", 1000));

        for (var i = 0; i < 5; i++) await env.Service.SendAsync(Env.Recipient(), 1000);

        Assert.Equal(5, env.Api.Broadcasts.Count);
    }

    [Fact]
    public async Task RateLimit_ResetsOnResetSession()
    {
        var env = await SixOutputsAsync();
        for (var i = 0; i < 5; i++) await env.Service.SendAsync(Env.Recipient(), 1000);
        Assert.Equal(DdSendIssue.RateLimited, (await env.Service.CheckSendAsync(Env.Recipient(), 1000)).Issue);

        env.Service.ResetSession();

        Assert.True((await env.Service.CheckSendAsync(Env.Recipient(), 1000)).IsOk);
    }

    // ---------- pending sends over time (FR-017, research R13) ----------

    private static async Task<(Env Env, string Txid)> OneSendAsync()
    {
        var env = new Env();
        env.Fund(0, 2500);
        env.Fees.Add(1, 50_000_000);
        await env.ReadyAsync();
        var result = await env.Service.SendAsync(Env.Recipient(), 1000);
        return (env, result.Txid);
    }

    [Fact]
    public async Task PendingSend_StaysWhileInTheMempool_AndIsDroppedAfterSixConfirmations()
    {
        var (env, txid) = await OneSendAsync();

        env.Api.Statuses[txid] = new DdTxStatusResult(DdTxState.Mempool, 0);
        await env.Service.RefreshAsync();
        Assert.Single((await env.Saved())!.PendingSends);

        env.Api.Statuses[txid] = new DdTxStatusResult(DdTxState.Confirmed, 3);
        await env.Service.RefreshAsync();
        Assert.Single((await env.Saved())!.PendingSends);

        env.Api.Statuses[txid] = new DdTxStatusResult(DdTxState.Confirmed, 6);
        await env.Service.RefreshAsync();
        Assert.Empty((await env.Saved())!.PendingSends);
    }

    [Fact]
    public async Task MissingSend_IsRebroadcastOnce_ThenStaysPendingWhileLessThan30MinutesObserved()
    {
        var (env, txid) = await OneSendAsync();
        var broadcastsAfterSend = env.Api.Broadcasts.Count; // 1

        await env.Service.RefreshAsync(); // first sight of "unknown"
        for (var i = 0; i < 5; i++)
        {
            env.Clock = env.Clock.AddMinutes(2);
            await env.Service.RefreshAsync();
        }

        var pending = Assert.Single((await env.Saved())!.PendingSends);
        Assert.Equal(DdTxStatus.Pending, pending.Status);
        Assert.Equal(TimeSpan.FromMinutes(10), pending.MissingObserved);
        Assert.NotNull(pending.FirstMissingAt);
        Assert.Equal(broadcastsAfterSend + 1, env.Api.Broadcasts.Count); // exactly one rebroadcast
        Assert.True(pending.Rebroadcasted);
    }

    [Fact]
    public async Task AbsentFor30MinutesObserved_IsMarkedFailed_AndTheFundsAreReleased()
    {
        var (env, _) = await OneSendAsync();
        await env.Service.RefreshAsync();
        Assert.Equal(0, (await env.Service.RefreshAsync()).AvailableCents); // reserved while pending

        IReadOnlyList<PendingDdSend> failedNow = [];
        for (var i = 0; i < 15 && failedNow.Count == 0; i++)
        {
            env.Clock = env.Clock.AddMinutes(2);
            var d = await env.Service.RefreshAsync();
            failedNow = d.NewlyFailedSends;
            if (failedNow.Count == 0) Assert.Equal(0, d.AvailableCents);
        }

        var failed = Assert.Single(failedNow); // reported exactly once, at the moment it crosses 30 minutes
        Assert.Equal(DdTxStatus.Failed, failed.Status);

        var after = await env.Service.RefreshAsync();
        Assert.Empty(after.NewlyFailedSends);      // not announced again
        Assert.Equal(2500, after.AvailableCents);   // the inputs are free again
        var entry = after.History.First(t => t.Txid == failed.Txid);
        Assert.Equal(DdTxStatus.Failed, entry.Status);
    }

    [Fact]
    public async Task Acknowledging_AFailedSend_RemovesIt()
    {
        var (env, _) = await OneSendAsync();
        await env.Service.RefreshAsync();
        PendingDdSend? failed = null;
        for (var i = 0; i < 16 && failed is null; i++)
        {
            env.Clock = env.Clock.AddMinutes(2);
            failed = (await env.Service.RefreshAsync()).NewlyFailedSends.FirstOrDefault();
        }
        Assert.NotNull(failed);

        await env.Service.AcknowledgeFailedSendAsync(failed!.Txid);

        Assert.Empty((await env.Saved())!.PendingSends);
        Assert.DoesNotContain((await env.Service.RefreshAsync()).History, t => t.Txid == failed.Txid);
    }

    [Fact]
    public async Task AcknowledgingASendThatIsStillPending_DoesNothing()
    {
        var (env, txid) = await OneSendAsync();
        await env.Service.AcknowledgeFailedSendAsync(txid);
        Assert.Single((await env.Saved())!.PendingSends);
    }

    [Fact]
    public async Task OfflineTime_IsNotCountedAsAbsence()
    {
        var (env, _) = await OneSendAsync();
        await env.Service.RefreshAsync();
        for (var i = 0; i < 5; i++) { env.Clock = env.Clock.AddMinutes(2); await env.Service.RefreshAsync(); } // 10 minutes observed

        env.Api.Down = true;
        env.Clock = env.Clock.AddHours(3);
        await env.Service.RefreshAsync(); // backend unreachable: nothing is observed

        env.Api.Down = false;
        env.Clock = env.Clock.AddSeconds(1);
        var d = await env.Service.RefreshAsync();

        var pending = Assert.Single((await env.Saved())!.PendingSends);
        Assert.Equal(DdTxStatus.Pending, pending.Status); // 3 hours offline did not fail it
        Assert.Equal(TimeSpan.FromMinutes(12), pending.MissingObserved); // the gap is capped at 2 minutes
        Assert.Empty(d.NewlyFailedSends);
    }

    [Fact]
    public async Task StatusEndpointFailing_LeavesThePendingSendUntouched()
    {
        var (env, _) = await OneSendAsync();
        await env.Service.RefreshAsync();
        var before = (await env.Saved())!.PendingSends.Single();

        env.Api.StatusDown = true;
        env.Clock = env.Clock.AddHours(2);
        await env.Service.RefreshAsync();

        var after = (await env.Saved())!.PendingSends.Single();
        Assert.Equal(before.MissingObserved, after.MissingObserved);
        Assert.Equal(before.LastCheckAt, after.LastCheckAt);
        Assert.Equal(DdTxStatus.Pending, after.Status);
    }

    [Fact]
    public async Task SeenAgain_ResetsTheAbsenceClock()
    {
        var (env, txid) = await OneSendAsync();
        await env.Service.RefreshAsync();
        for (var i = 0; i < 4; i++) { env.Clock = env.Clock.AddMinutes(2); await env.Service.RefreshAsync(); }
        Assert.True((await env.Saved())!.PendingSends.Single().MissingObserved > TimeSpan.Zero);

        env.Api.Statuses[txid] = new DdTxStatusResult(DdTxState.Mempool, 0);
        env.Clock = env.Clock.AddMinutes(2);
        await env.Service.RefreshAsync();

        var pending = (await env.Saved())!.PendingSends.Single();
        Assert.Equal(TimeSpan.Zero, pending.MissingObserved);
        Assert.Null(pending.FirstMissingAt);
    }
}

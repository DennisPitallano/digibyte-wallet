using DigiByte.Crypto.Psbt;
using DigiByte.Crypto.Tests;
using DigiByte.Wallet.Services;
using NBitcoin;
using static DigiByte.Crypto.Tests.PsbtTestFixtures;

namespace DigiByte.Wallet.Tests;

public class PsbtSigningServiceTests
{
    private const long OneDgb = 100_000_000;

    private readonly Key _wallet = new();
    private readonly FakePsbtBlockchain _chain = new();
    private readonly FakePsbtKeyProvider _keys;
    private readonly TransactionTracker _tracker = new(new ChangeAddressTests.InMemoryStorage());
    private readonly PsbtSigningService _service;

    public PsbtSigningServiceTests()
    {
        _keys = new FakePsbtKeyProvider(Net, _wallet);
        _tracker.SetActiveWallet("psbt-test");
        _service = new PsbtSigningService(_chain, _keys, _tracker);
    }

    private FundedCoin FundWallet(long amount = OneDgb, int confirmations = 6)
    {
        var coin = Fund(SegWit(_wallet), amount);
        _chain.AddUnspent(coin, Net, confirmations);
        return coin;
    }

    private async Task<PsbtLoadOutcome> LoadAsync(PSBT psbt) => await _service.LoadAsync(psbt.ToBase64());

    // ─── Story 1 ───

    [Fact]
    public async Task Text_AndBytes_GiveTheSameReview()
    {
        var coin = FundWallet();
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();

        var fromText = await _service.LoadAsync("  " + psbt.ToBase64() + "\n");
        var fromBytes = await _service.LoadAsync(psbt.ToBytes());

        Assert.NotNull(fromText.Document);
        Assert.NotNull(fromBytes.Document);
        Assert.Equal(fromText.Review.NetAmount, fromBytes.Review.NetAmount);
        Assert.Equal(fromText.Review.Fee, fromBytes.Review.Fee);
        Assert.Equal(fromText.Review.InputsToSign, fromBytes.Review.InputsToSign);
        Assert.True(fromText.Review.CanSign);
        Assert.True(fromBytes.Review.CanSign);
    }

    [Fact]
    public async Task UnreadableInput_IsRefused_NotThrown()
    {
        var outcome = await _service.LoadAsync("not a psbt");

        Assert.Null(outcome.Document);
        Assert.Equal(PsbtRefusalReason.NotAPsbt, outcome.Review.Refusal!.Reason);
        Assert.False(outcome.Review.CanSign);
    }

    [Fact]
    public async Task Sign_SignsOnlyTheWalletsInputs()
    {
        var mine = FundWallet();
        var theirs = Fund(SegWit(new Key()), OneDgb);
        var psbt = Builder().AddInput(mine).AddInput(theirs).Pay(2 * OneDgb - 100_000).Build();
        var loaded = await LoadAsync(psbt);

        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.True(outcome.Succeeded, outcome.Error);
        var signed = PSBT.Parse(outcome.Result!.SignedPsbtBase64, Net);
        Assert.Single(signed.Inputs[0].PartialSigs);
        Assert.Empty(signed.Inputs[1].PartialSigs);
        Assert.Equal(1, outcome.Result.SignedCount);
        Assert.Equal(1, outcome.Result.RemainingCount);
        Assert.False(outcome.Result.IsComplete);
        Assert.Null(outcome.Result.FinalTransaction);
    }

    [Fact]
    public async Task Sign_CompletesAPsbtThatOnlyNeedsTheWallet()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());

        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: false);

        Assert.True(outcome.Succeeded, outcome.Error);
        Assert.True(outcome.Result!.IsComplete);
        Assert.Equal(0, outcome.Result.RemainingCount);
        Assert.NotNull(outcome.Result.FinalTransaction);
        Assert.False(PSBT.Parse(outcome.Result.SignedPsbtBase64, Net).Inputs[0].IsFinalized());
    }

    [Fact]
    public async Task Sign_WithoutARequiredAcknowledgment_IsRefused()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(80_000_000).Build());
        Assert.True(loaded.Review.NeedsAcknowledgment);

        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: false);

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Error);
        Assert.Equal(0, _keys.SignCalls);
    }

    [Fact]
    public async Task Sign_DoesntChangeTheLoadedPsbt_OrBroadcast()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        var before = loaded.Document!.Psbt.ToBase64();

        var outcome = await _service.SignAsync(loaded.Document, acknowledged: false);

        Assert.True(outcome.Succeeded, outcome.Error);
        Assert.Equal(before, loaded.Document.Psbt.ToBase64());
        // The fake chain throws if anything is broadcast, and the coin is still unspent: balance and history are unchanged.
        Assert.Single(await _chain.GetUtxosAsync(coin.ScriptPubKey.GetDestinationAddress(Net)!.ToString()));
    }

    [Fact]
    public void Service_HoldsNoPsbt()
    {
        var fields = typeof(PsbtSigningService).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        Assert.DoesNotContain(fields, f => f.FieldType == typeof(PSBT) || f.FieldType == typeof(PsbtDocument)
                                           || f.FieldType == typeof(ReviewSummary) || f.FieldType == typeof(string));
    }

    [Fact]
    public async Task OtherParticipantsInputs_AreNotLookedUp()
    {
        var mine = FundWallet();
        var theirs = Fund(SegWit(new Key()), OneDgb);

        var loaded = await LoadAsync(Builder().AddInput(mine).AddInput(theirs).Pay(2 * OneDgb - 100_000).Build());

        Assert.Equal(ChainStatus.Verified, loaded.Review.ChainStatus);
        Assert.Equal([SegWit(_wallet).GetDestinationAddress(Net)!.ToString()], _chain.UnspentLookupAddresses);
        Assert.Equal(0, _chain.TransactionLookups);
    }

    // ─── Story 2 ───

    [Fact]
    public async Task CoinSpentBetweenLoadAndSign_IsRefusedAtSigning()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        Assert.True(loaded.Review.CanSign);

        _chain.Spend(coin, Net);
        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.False(outcome.Succeeded);
        Assert.Equal(PsbtRefusalReason.InputSpentOrUnknown, outcome.Review.Refusal!.Reason);
        Assert.Equal(0, _keys.SignCalls);
    }

    [Fact]
    public async Task ChainThatCantAnswer_GivesAnUnavailableReview_AndNoSignature()
    {
        var coin = FundWallet();
        _chain.UnspentLookupsFail = true;

        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.Equal(ChainStatus.Unavailable, loaded.Review.ChainStatus);
        Assert.Null(loaded.Review.Refusal);
        Assert.False(loaded.Review.CanSign);
        Assert.False(outcome.Succeeded);
        Assert.Equal(0, _keys.SignCalls);
    }

    [Fact]
    public async Task ChainThatFailsOnlyAtSigning_IsUnavailable_NotSpent()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());

        _chain.UnspentLookupsFail = true;
        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ChainStatus.Unavailable, outcome.Review.ChainStatus);
        Assert.Null(outcome.Review.Refusal);
        Assert.Equal(0, _keys.SignCalls);
    }

    [Fact]
    public async Task ExplorerReturning502_IsUnavailable_NotSpentOrUnknown()
    {
        var explorer = new BlockchainApiService(new HttpClient(new StatusHandler(System.Net.HttpStatusCode.BadGateway)));
        var service = new PsbtSigningService(explorer, _keys, _tracker);
        var coin = Fund(SegWit(_wallet), OneDgb);

        var loaded = await service.LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build().ToBase64());

        Assert.Equal(ChainStatus.Unavailable, loaded.Review.ChainStatus);
        Assert.Null(loaded.Review.Refusal);
        Assert.False(loaded.Review.CanSign);
    }

    [Fact]
    public async Task CoinKnownOnlyOnAnotherNetwork_IsRefused()
    {
        var coin = Fund(SegWit(_wallet), OneDgb);
        _chain.AddUnspent(coin, DigiByte.Crypto.Networks.DigiByteNetwork.Testnet);

        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());

        Assert.Equal(PsbtRefusalReason.InputSpentOrUnknown, loaded.Review.Refusal!.Reason);
        Assert.False(loaded.Review.CanSign);
    }

    [Fact]
    public async Task WrongPin_ProducesNoSignature()
    {
        var coin = FundWallet();
        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        var flow = new DigiByte.Web.Services.PsbtConfirmationFlow(
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(true),
            () => Task.FromResult(false));

        var approval = await flow.RunAsync(loaded.Review, acknowledged: true);
        if (approval.Approved) await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.False(approval.Approved);
        Assert.Equal(0, _keys.SignCalls);
    }

    [Fact]
    public async Task WalletInputWithPsbtData_IsProvenByTheUnspentList_WithoutATransactionLookup()
    {
        var coin = Fund(SegWit(_wallet), OneDgb);
        _chain.AddUnspent(coin, Net, withTransaction: false);

        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());

        Assert.Null(loaded.Review.Refusal);
        Assert.True(loaded.Review.CanSign);
        Assert.Equal(0, _chain.TransactionLookups);
    }

    [Fact]
    public async Task FalseAmountOnAWalletInput_IsRefused()
    {
        var coin = FundWallet();
        var psbt = Builder().AddInput(coin).Pay(80_000_000).Build();
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(81_000_000), coin.ScriptPubKey);

        var loaded = await LoadAsync(psbt);

        Assert.Equal(PsbtRefusalReason.AmountOrScriptMismatch, loaded.Review.Refusal!.Reason);
    }

    // ─── Story 3: broadcast ───

    private async Task<(PsbtLoadOutcome Loaded, PsbtSignOutcome Signed)> SignAsync(PSBT psbt, bool acknowledged = true)
    {
        var loaded = await LoadAsync(psbt);
        var signed = await _service.SignAsync(loaded.Document!, acknowledged);
        Assert.True(signed.Succeeded, signed.Error);
        return (loaded, signed);
    }

    /// <summary>A wallet coin plus another participant's coin, the other already signed, so the wallet's signature completes it.</summary>
    private PSBT SharedPsbt(long walletIn, long otherIn, long walletOut, long pay)
    {
        var other = new Key();
        var mine = FundWallet(walletIn);
        var theirs = Fund(SegWit(other), otherIn);
        var builder = Builder().AddInput(mine).AddInput(theirs).Pay(pay);
        if (walletOut > 0) builder.AddOutput(SegWit(_wallet), walletOut);
        return PsbtSigner.SignInputs(builder.Build(), new Dictionary<int, Key> { [1] = other });
    }

    [Fact]
    public async Task CompletePsbt_Broadcasts_AndRecordsOneHistoryEntry()
    {
        var coin = FundWallet();
        var stranger = Stranger();
        var psbt = Builder().AddInput(coin).AddOutput(stranger, 60_000_000).AddOutput(SegWit(_wallet), 39_900_000).Build();
        var (_, signed) = await SignAsync(psbt);

        var result = await _service.BroadcastAsync(signed.Result!, signed.Review);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(signed.Result!.FinalTransaction!.GetHash().ToString(), result.TxId);
        Assert.Single(_chain.Broadcasts);
        var record = Assert.Single(await _tracker.GetAllAsync());
        Assert.Equal(result.TxId, record.TxId);
        Assert.Equal(stranger.GetDestinationAddress(Net)!.ToString(), record.CounterpartyAddress);
        Assert.Equal(60_000_000, record.AmountSatoshis);
        Assert.Equal(100_000, record.FeeSatoshis);
    }

    [Fact]
    public async Task SharedPsbt_RecordsTheAmountLeaving_WithNoFee()
    {
        var (_, signed) = await SignAsync(SharedPsbt(walletIn: 3 * OneDgb, otherIn: OneDgb, walletOut: OneDgb, pay: 3 * OneDgb - 100_000));
        Assert.True(signed.Result!.IsComplete);

        var result = await _service.BroadcastAsync(signed.Result, signed.Review);

        Assert.True(result.Succeeded, result.Error);
        var record = Assert.Single(await _tracker.GetAllAsync());
        Assert.Equal(2 * OneDgb, record.AmountSatoshis);
        Assert.Equal(0, record.FeeSatoshis);
    }

    [Fact]
    public async Task WhenTheWalletGainsOverall_NothingIsRecorded()
    {
        var (_, signed) = await SignAsync(SharedPsbt(walletIn: OneDgb, otherIn: 5 * OneDgb, walletOut: 3 * OneDgb, pay: 3 * OneDgb - 100_000));
        Assert.True(signed.Review.NetAmount < 0);

        var result = await _service.BroadcastAsync(signed.Result!, signed.Review);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(await _tracker.GetAllAsync());
    }

    [Fact]
    public async Task TamperedSignature_IsNotBroadcast()
    {
        var coin = FundWallet();
        var (_, signed) = await SignAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        var tampered = PSBT.Parse(signed.Result!.SignedPsbtBase64, Net);
        tampered.Inputs[0].PartialSigs[_wallet.PubKey] = new TransactionSignature(new Key().Sign(RandomUtils.GetUInt256()), SigHash.All);
        var claimed = signed.Result with { SignedPsbtBase64 = tampered.ToBase64() };

        var result = await _service.BroadcastAsync(claimed, signed.Review);

        Assert.False(result.Succeeded);
        Assert.Empty(_chain.Broadcasts);
        Assert.Empty(await _tracker.GetAllAsync());
    }

    [Fact]
    public async Task FullySignedPsbt_WithNoWalletInput_IsRefused()
    {
        var other = new Key();
        var theirs = Fund(SegWit(other), OneDgb);
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(theirs).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = other });
        var loaded = await LoadAsync(psbt);

        var prepared = await _service.PrepareSignedAsync(loaded.Document!, acknowledged: true);
        var complete = new SigningResult(psbt.ToBase64(), 0, 0, true, PsbtSigner.TryComplete(psbt).Transaction);
        var broadcast = await _service.BroadcastAsync(complete, loaded.Review);

        Assert.False(loaded.Review.CanBroadcastSigned);
        Assert.False(prepared.Succeeded);
        Assert.False(broadcast.Succeeded);
        Assert.Empty(_chain.Broadcasts);
    }

    [Fact]
    public async Task AlreadySignedPsbt_IsPrepared_WithoutANewSignature_AndBroadcasts()
    {
        var coin = FundWallet();
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = _wallet });
        var loaded = await LoadAsync(psbt);
        Assert.True(loaded.Review.CanBroadcastSigned);

        var prepared = await _service.PrepareSignedAsync(loaded.Document!, acknowledged: false);
        var result = await _service.BroadcastAsync(prepared.Result!, prepared.Review);

        Assert.True(prepared.Succeeded, prepared.Error);
        Assert.Equal(0, prepared.Result!.SignedCount);
        Assert.True(prepared.Result.IsComplete);
        Assert.Equal(0, _keys.SignCalls);
        Assert.True(result.Succeeded, result.Error);
        Assert.Single(_chain.Broadcasts);
    }

    [Fact]
    public async Task PrepareSigned_RepeatsTheChainChecks()
    {
        var coin = FundWallet();
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = _wallet });
        var loaded = await LoadAsync(psbt);

        _chain.Spend(coin, Net);
        var prepared = await _service.PrepareSignedAsync(loaded.Document!, acknowledged: true);

        Assert.False(prepared.Succeeded);
        Assert.Equal(PsbtRefusalReason.InputSpentOrUnknown, prepared.Review.Refusal!.Reason);
    }

    [Fact]
    public async Task PrepareSigned_WithoutARequiredAcknowledgment_IsRefused()
    {
        var coin = FundWallet();
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(80_000_000).Build(), new Dictionary<int, Key> { [0] = _wallet });
        var loaded = await LoadAsync(psbt);
        Assert.True(loaded.Review.NeedsAcknowledgment);

        Assert.False((await _service.PrepareSignedAsync(loaded.Document!, acknowledged: false)).Succeeded);
        Assert.True((await _service.PrepareSignedAsync(loaded.Document!, acknowledged: true)).Succeeded);
    }

    [Fact]
    public async Task FailedBroadcast_ReturnsTheReason_AndRecordsNothing()
    {
        var coin = FundWallet();
        var (_, signed) = await SignAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        _chain.BroadcastError = "txn-mempool-conflict";

        var result = await _service.BroadcastAsync(signed.Result!, signed.Review);

        Assert.False(result.Succeeded);
        Assert.Contains("txn-mempool-conflict", result.Error);
        Assert.Empty(await _tracker.GetAllAsync());
        Assert.NotEmpty(signed.Result!.SignedPsbtBase64);
    }

    [Fact]
    public async Task WatchOnlyWallet_CantPrepareOrBroadcast()
    {
        var coin = FundWallet();
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = _wallet });
        var loaded = await LoadAsync(psbt);
        var complete = new SigningResult(psbt.ToBase64(), 0, 0, true, PsbtSigner.TryComplete(psbt).Transaction);
        _keys.CanSign = false;

        Assert.False((await _service.PrepareSignedAsync(loaded.Document!, acknowledged: true)).Succeeded);
        Assert.False((await _service.BroadcastAsync(complete, loaded.Review)).Succeeded);
        Assert.Empty(_chain.Broadcasts);
    }

    // ─── Story 5: inputs without data ───

    [Fact]
    public async Task WalletSegWitInput_WithoutData_IsFilledFromTheChain_AndExported()
    {
        var coin = FundWallet();
        var (loaded, signed) = await SignAsync(Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build());

        var exported = PSBT.Parse(signed.Result!.SignedPsbtBase64, Net);
        Assert.Null(loaded.Document!.Psbt.Inputs[0].WitnessUtxo);
        Assert.Equal(coin.Amount, exported.Inputs[0].WitnessUtxo!.Value.Satoshi);
        Assert.Equal(coin.ScriptPubKey, exported.Inputs[0].WitnessUtxo!.ScriptPubKey);
        Assert.Single(exported.Inputs[0].PartialSigs);
        Assert.True(signed.Result.IsComplete);
        Assert.NotNull(signed.Result.FinalTransaction);
    }

    [Fact]
    public async Task FilledInput_CarriesAValidSignature()
    {
        var coin = FundWallet();
        var (_, signed) = await SignAsync(Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build());

        var tx = signed.Result!.FinalTransaction!;
        Assert.True(tx.Inputs.AsIndexedInputs().First().VerifyScript(coin.Coin.TxOut, out var error), error.ToString());
    }

    [Fact]
    public async Task LegacyWalletInput_WithoutItsPreviousTransaction_IsStillRefused()
    {
        var coin = Fund(Legacy(_wallet), OneDgb);
        _chain.AddUnspent(coin, Net);

        var loaded = await LoadAsync(Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build());
        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.Equal(PsbtRefusalReason.MissingPreviousTransaction, loaded.Review.Refusal!.Reason);
        Assert.False(string.IsNullOrEmpty(loaded.Review.Refusal.Message));
        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task OtherParticipantsInput_WithoutData_IsNotFilled()
    {
        var mine = FundWallet();
        var theirs = Fund(SegWit(new Key()), OneDgb);
        _chain.AddTransaction(theirs, Net);
        var (_, signed) = await SignAsync(Builder().AddInput(mine).AddInput(theirs, PsbtInputData.None).Pay(2 * OneDgb - 100_000).Build());

        var exported = PSBT.Parse(signed.Result!.SignedPsbtBase64, Net);
        Assert.Null(exported.Inputs[1].WitnessUtxo);
        Assert.Empty(exported.Inputs[1].PartialSigs);
    }

    [Fact]
    public async Task InputWithoutData_WhoseLookupFails_BlocksSigning_UntilARetrySucceeds()
    {
        var coin = FundWallet();
        _chain.TransactionLookupsFail = true;

        var loaded = await LoadAsync(Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build());
        var blocked = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.Equal(PsbtRefusalReason.InputNotIdentified, loaded.Review.Refusal!.Reason);
        Assert.False(blocked.Succeeded);
        Assert.Equal(0, _keys.SignCalls);

        _chain.TransactionLookupsFail = false;
        var retried = await _service.ReviewAsync(loaded.Document!);

        Assert.Null(retried.Refusal);
        Assert.True(retried.CanSign);
    }

    private sealed class StatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("<html>bad gateway</html>") });
    }

    [Fact]
    public async Task WatchOnlyWallet_CanReview_ButNotSign()
    {
        _keys.CanSign = false;
        var coin = FundWallet();

        var loaded = await LoadAsync(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build());
        var outcome = await _service.SignAsync(loaded.Document!, acknowledged: true);

        Assert.Equal(1, loaded.Review.InputsToSign);
        Assert.False(loaded.Review.CanSign);
        Assert.False(outcome.Succeeded);
        Assert.Equal(0, _keys.SignCalls);
    }
}

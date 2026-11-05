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
    private readonly PsbtSigningService _service;

    public PsbtSigningServiceTests()
    {
        _keys = new FakePsbtKeyProvider(Net, _wallet);
        _service = new PsbtSigningService(_chain, _keys);
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

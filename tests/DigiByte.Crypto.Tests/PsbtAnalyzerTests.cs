using DigiByte.Crypto.Psbt;
using DigiByte.Crypto.Transactions;
using NBitcoin;
using static DigiByte.Crypto.Tests.PsbtTestFixtures;

namespace DigiByte.Crypto.Tests;

public class PsbtAnalyzerTests
{
    private const long OneDgb = 100_000_000;
    private static readonly Key Wallet = new();
    private static readonly Key Other = new();

    private static IReadOnlyDictionary<Script, string> Owned(params Key[] keys)
    {
        var owned = new Dictionary<Script, string>();
        foreach (var key in keys)
            foreach (var type in new[] { ScriptPubKeyType.Segwit, ScriptPubKeyType.Legacy })
            {
                var address = key.PubKey.GetAddress(type, Net);
                owned[address.ScriptPubKey] = address.ToString();
            }
        return owned;
    }

    private static ChainInputFact Unspent(FundedCoin coin, int confirmations = 6) =>
        new(coin.Outpoint, ChainLookup.Found, coin.Amount, coin.ScriptPubKey, IsUnspent: true, Confirmations: confirmations);

    private static ReviewSummary Analyze(PSBT psbt, IEnumerable<ChainInputFact> facts, ChainStatus status = ChainStatus.Verified, bool canSign = true) =>
        PsbtAnalyzer.Analyze(psbt, Owned(Wallet), facts.ToDictionary(f => f.Outpoint), status, canSign, Net);

    private static ReviewSummary AnalyzeWalletSpend(FundedCoin coin, long payment) =>
        Analyze(Builder().AddInput(coin).Pay(payment).Build(), [Unspent(coin)]);

    // ─── Story 1: classification, outputs, totals, warnings ───

    [Fact]
    public void WalletInput_IsRecognizedByScript_AndCanBeSigned()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);

        var review = AnalyzeWalletSpend(coin, OneDgb - 100_000);

        var input = Assert.Single(review.Inputs);
        Assert.Equal(InputOwner.Wallet, input.Owner);
        Assert.Equal(InputKind.NativeSegWit, input.Kind);
        Assert.Equal(AmountSource.Chain, input.AmountSource);
        Assert.Equal(1, review.InputsToSign);
        Assert.True(review.CanSign);
        Assert.Null(review.Refusal);
        Assert.Equal(FeeStatus.Verified, review.FeeStatus);
        Assert.Equal(100_000, review.Fee);
    }

    [Fact]
    public void LegacyWalletInput_IsRecognized()
    {
        var coin = Fund(Legacy(Wallet), OneDgb);

        var review = AnalyzeWalletSpend(coin, OneDgb - 100_000);

        Assert.Equal(InputKind.Legacy, review.Inputs[0].Kind);
        Assert.True(review.CanSign);
    }

    [Fact]
    public void KeyPathsInThePsbt_DontMakeAnInputOrOutputTheWallets()
    {
        var mine = Fund(SegWit(Wallet), OneDgb);
        var theirs = Fund(SegWit(Other), OneDgb);
        var attacker = SegWit(Other);
        var psbt = Builder().AddInput(mine).AddInput(theirs).AddOutput(attacker, OneDgb).Pay(OneDgb - 200_000).Build();
        var fingerprint = new HDFingerprint(0x01020304);
        psbt.Inputs[1].HDKeyPaths[Other.PubKey] = new RootedKeyPath(fingerprint, new KeyPath("84'/20'/0'/0/0"));
        psbt.Outputs[0].HDKeyPaths[Other.PubKey] = new RootedKeyPath(fingerprint, new KeyPath("84'/20'/0'/1/0"));

        var review = Analyze(psbt, [Unspent(mine)]);

        Assert.Equal(InputOwner.Other, review.Inputs[1].Owner);
        Assert.False(review.Outputs[0].IsWallet);
        Assert.Equal(1, review.InputsToSign);
        Assert.Equal(1, review.OtherInputs);
    }

    [Fact]
    public void Outputs_AreLabeledAsAddress_Data_OrNonStandard()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin)
            .AddOutput(SegWit(Wallet), 10_000_000)
            .AddDataOutput("hello"u8.ToArray())
            .AddOutput(new Script(OpcodeType.OP_TRUE), 5_000_000)
            .Pay(80_000_000)
            .Build();

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.Equal(OutputLabel.Address, review.Outputs[0].Label);
        Assert.True(review.Outputs[0].IsWallet);
        Assert.Equal(Wallet.PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString(), review.Outputs[0].Address);
        Assert.Equal(OutputLabel.Data, review.Outputs[1].Label);
        Assert.Equal("hello", review.Outputs[1].DataText);
        Assert.Equal(OutputLabel.NonStandard, review.Outputs[2].Label);
        Assert.Contains(PsbtWarning.NonStandardOutput, review.Warnings);
        Assert.Equal(OutputLabel.Address, review.Outputs[3].Label);
        Assert.False(review.Outputs[3].IsWallet);
    }

    [Fact]
    public void NetAmount_IsWalletInputsLessWalletOutputs_ToTheSatoshi()
    {
        var coin = Fund(SegWit(Wallet), 123_456_789);
        var psbt = Builder().AddInput(coin).AddOutput(SegWit(Wallet), 23_456_789).Pay(99_990_000).Build();

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.Equal(100_000_000, review.NetAmount);
        Assert.Equal(10_000, review.Fee);
    }

    [Fact]
    public void NetAmount_IsNegative_WhenTheWalletGainsOverall()
    {
        var mine = Fund(SegWit(Wallet), OneDgb);
        var theirs = Fund(SegWit(Other), 5 * OneDgb);
        var psbt = Builder().AddInput(mine).AddInput(theirs).AddOutput(SegWit(Wallet), 3 * OneDgb).Pay(3 * OneDgb - 100_000).Build();

        var review = Analyze(psbt, [Unspent(mine)]);

        Assert.Equal(-2 * OneDgb, review.NetAmount);
    }

    [Fact]
    public void HighFee_OverTenPercentOfWhatLeaves_NeedsAcknowledgment()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);

        var review = AnalyzeWalletSpend(coin, 80_000_000);

        Assert.Contains(PsbtWarning.HighFee, review.Warnings);
        Assert.True(review.NeedsAcknowledgment);
    }

    [Fact]
    public void NormalFee_NeedsNoAcknowledgment()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);

        var review = AnalyzeWalletSpend(coin, OneDgb - 100_000);

        Assert.DoesNotContain(PsbtWarning.HighFee, review.Warnings);
        Assert.False(review.NeedsAcknowledgment);
    }

    [Fact]
    public void HighFee_WhenEveryOutputIsTheWallets_ComparesWithTotalInputs()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var consolidate = Builder().AddInput(coin).AddOutput(SegWit(Wallet), OneDgb - 100_000).Build();
        var overpay = Builder().AddInput(coin).AddOutput(SegWit(Wallet), 85_000_000).Build();

        Assert.DoesNotContain(PsbtWarning.HighFee, Analyze(consolidate, [Unspent(coin)]).Warnings);
        Assert.Contains(PsbtWarning.HighFee, Analyze(overpay, [Unspent(coin)]).Warnings);
    }

    [Fact]
    public void LockTime_AndUnconfirmedInputs_AreWarned()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).WithLockTime(20_000_000).Build();

        var review = Analyze(psbt, [Unspent(coin, confirmations: 0)]);

        Assert.Contains(PsbtWarning.LockTime, review.Warnings);
        Assert.Contains(PsbtWarning.UnconfirmedInput, review.Warnings);
        Assert.True(review.Inputs[0].IsUnconfirmed);
    }

    [Fact]
    public void CanSign_IsFalse_WhenTheWalletCantSign_OrChainDataIsUnavailable()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();

        Assert.False(Analyze(psbt, [Unspent(coin)], canSign: false).CanSign);
        var unavailable = Analyze(psbt, [], ChainStatus.Unavailable);
        Assert.False(unavailable.CanSign);
        Assert.Equal(ChainStatus.Unavailable, unavailable.ChainStatus);
        Assert.Null(unavailable.Refusal);
    }

    [Fact]
    public void SharedPsbt_HasAnUnverifiedFee_ANoteAndCounts_UntilStory4()
    {
        var mine = Fund(SegWit(Wallet), OneDgb);
        var theirs = Fund(SegWit(Other), OneDgb);
        var psbt = Builder().AddInput(mine).AddInput(theirs).Pay(2 * OneDgb - 100_000).Build();

        var review = Analyze(psbt, [Unspent(mine)]);

        Assert.Equal(FeeStatus.Unverified, review.FeeStatus);
        Assert.Contains(PsbtWarning.UnverifiedFee, review.Warnings);
        Assert.True(review.NeedsAcknowledgment);
        Assert.Contains(PsbtWarning.SharedOutputsNotChecked, review.Warnings);
        Assert.Contains(PsbtWarning.OutOfRangeNote, review.Warnings);
        Assert.Equal(1, review.InputsToSign);
        Assert.Equal(1, review.OtherInputs);
        Assert.Equal(AmountSource.PsbtOnly, review.Inputs[1].AmountSource);
        Assert.True(review.CanSign);
    }

    [Fact]
    public void AlreadySignedWalletInput_IsNotSignedAgain()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = Wallet });

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.Equal(SignatureState.SignedByWallet, review.Inputs[0].SignatureState);
        Assert.Equal(0, review.InputsToSign);
        Assert.Equal(PsbtRefusalReason.NothingToSign, review.Refusal!.Reason);
    }

    [Fact]
    public void CompletePsbt_WithAWalletInput_CanBeBroadcast()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = PsbtSigner.SignInputs(Builder().AddInput(coin).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = Wallet });

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.True(review.IsComplete);
        Assert.True(review.CanBroadcastSigned);
        Assert.False(review.CanSign);
    }

    [Fact]
    public void CompletePsbt_CantBeBroadcast_WithoutAWalletInput_OrWithoutSpendingKeys()
    {
        var theirs = Fund(SegWit(Other), OneDgb);
        var theirsSigned = PsbtSigner.SignInputs(Builder().AddInput(theirs).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = Other });
        var mine = Fund(SegWit(Wallet), OneDgb);
        var mineSigned = PsbtSigner.SignInputs(Builder().AddInput(mine).Pay(OneDgb - 100_000).Build(), new Dictionary<int, Key> { [0] = Wallet });

        Assert.False(Analyze(theirsSigned, []).CanBroadcastSigned);
        Assert.Equal(PsbtRefusalReason.NothingToSign, Analyze(theirsSigned, []).Refusal!.Reason);
        Assert.False(Analyze(mineSigned, [Unspent(mine)], canSign: false).CanBroadcastSigned);
    }

    [Fact]
    public void InputWithNoData_IsIdentifiedFromTheChain()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build();

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.Equal(InputOwner.Wallet, review.Inputs[0].Owner);
        Assert.Equal(OneDgb, review.Inputs[0].Amount);
        Assert.Equal(AmountSource.Chain, review.Inputs[0].AmountSource);
        Assert.Null(review.Refusal);
    }

    // ─── Story 2: refusals, in order ───

    private static PsbtRefusalReason? RefusalFor(PSBT psbt, params ChainInputFact[] facts) => Analyze(psbt, facts).Refusal?.Reason;

    [Fact]
    public void DigiDollarVersion_IsRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).WithVersion(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer)).Build();

        Assert.Equal(PsbtRefusalReason.UnsupportedDigiDollar, RefusalFor(psbt, Unspent(coin)));
    }

    [Fact]
    public void DigiDollarDataOutput_UnderAPlainVersion_IsRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000)
            .AddOutput(DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [500]), 0).Build();

        Assert.Equal(PsbtRefusalReason.UnsupportedDigiDollar, RefusalFor(psbt, Unspent(coin)));
    }

    [Theory]
    [InlineData(SigHash.None)]
    [InlineData(SigHash.Single)]
    [InlineData(SigHash.All | SigHash.AnyoneCanPay)]
    public void NonStandardSignatureType_OnAWalletInput_IsRefused(SigHash sigHash)
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();
        psbt.Inputs[0].SighashType = sigHash;

        Assert.Equal(PsbtRefusalReason.UnsupportedSignatureType, RefusalFor(psbt, Unspent(coin)));
    }

    [Fact]
    public void NonStandardSignatureType_OnAnotherInput_IsRefused()
    {
        var mine = Fund(SegWit(Wallet), OneDgb);
        var theirs = Fund(SegWit(Other), OneDgb);
        var psbt = Builder().AddInput(mine).AddInput(theirs).Pay(2 * OneDgb - 100_000).Build();
        psbt.Inputs[1].SighashType = SigHash.Single;

        var review = Analyze(psbt, [Unspent(mine)]);

        Assert.Equal(PsbtRefusalReason.UnsupportedSignatureType, review.Refusal!.Reason);
        Assert.Equal(1, review.Refusal.InputIndex);
        Assert.False(review.CanSign);
    }

    [Fact]
    public void ExplicitSighashAll_IsAccepted()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();
        psbt.Inputs[0].SighashType = SigHash.All;

        Assert.Null(RefusalFor(psbt, Unspent(coin)));
    }

    [Theory]
    [InlineData(ChainLookup.NotFound)]
    [InlineData(ChainLookup.Unavailable)]
    public void InputWithNoData_WhoseLookupFailed_IsRefusedAsNotIdentified(ChainLookup lookup)
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin, PsbtInputData.None).Pay(OneDgb - 100_000).Build();

        Assert.Equal(PsbtRefusalReason.InputNotIdentified, RefusalFor(psbt, new ChainInputFact(coin.Outpoint, lookup)));
    }

    [Fact]
    public void TaprootInput_IsRefused()
    {
        var coin = Fund(Taproot(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();

        Assert.Equal(PsbtRefusalReason.UnsupportedTaproot, RefusalFor(psbt));
    }

    [Fact]
    public void LegacyWalletInput_WithoutItsPreviousTransaction_IsRefused()
    {
        var coin = Fund(Legacy(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin, PsbtInputData.WitnessUtxo).Pay(OneDgb - 100_000).Build();

        Assert.Equal(PsbtRefusalReason.MissingPreviousTransaction, RefusalFor(psbt, Unspent(coin)));
    }

    [Fact]
    public void PreviousTransaction_ThatDoesntMatchTheInput_IsRefused()
    {
        var coin = Fund(Legacy(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();
        psbt.Inputs[0].NonWitnessUtxo = Fund(Legacy(Wallet), 5 * OneDgb).FundingTransaction;

        Assert.Equal(PsbtRefusalReason.PreviousTransactionMismatch, RefusalFor(psbt, Unspent(coin)));
    }

    [Fact]
    public void FalseAmount_OnAWalletInput_IsRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(80_000_000).Build();
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Satoshis(81_000_000), coin.ScriptPubKey);

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.Equal(PsbtRefusalReason.AmountOrScriptMismatch, review.Refusal!.Reason);
        Assert.Equal(0, review.Refusal.InputIndex);
    }

    [Fact]
    public void FalseScript_OnAWalletInput_IsRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();
        var fact = Unspent(coin) with { Script = Legacy(Wallet) };

        Assert.Equal(PsbtRefusalReason.AmountOrScriptMismatch, RefusalFor(psbt, fact));
    }

    [Fact]
    public void SpentWalletInput_IsRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();

        Assert.Equal(PsbtRefusalReason.InputSpentOrUnknown,
            RefusalFor(psbt, new ChainInputFact(coin.Outpoint, ChainLookup.NotFound, IsUnspent: false)));
    }

    [Fact]
    public void WalletInput_WithNoChainFact_IsRefusedAsUnknown()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();

        Assert.Equal(PsbtRefusalReason.InputSpentOrUnknown, RefusalFor(psbt));
    }

    [Fact]
    public void OutputsGreaterThanInputs_AreRefused()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);

        Assert.Equal(PsbtRefusalReason.OutputsExceedInputs, RefusalFor(Builder().AddInput(coin).Pay(OneDgb + 1).Build(), Unspent(coin)));
    }

    [Fact]
    public void NothingOfTheWallets_IsNothingToSign()
    {
        var theirs = Fund(SegWit(Other), OneDgb);
        var review = Analyze(Builder().AddInput(theirs).Pay(OneDgb - 100_000).Build(), []);

        Assert.Equal(PsbtRefusalReason.NothingToSign, review.Refusal!.Reason);
        Assert.False(review.CanSign);
    }

    [Fact]
    public void MultisigAndWrappedSegWit_OnTheWalletsKey_AreSomeoneElses()
    {
        var redeem = MultisigRedeem(Wallet, Other);
        var multisig = Fund(redeem.WitHash.ScriptPubKey, OneDgb);
        var wrapped = Fund(WrappedSegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(multisig, witnessScript: redeem).AddInput(wrapped).Pay(2 * OneDgb - 100_000).Build();

        var review = Analyze(psbt, []);

        Assert.All(review.Inputs, i => Assert.Equal(InputOwner.Other, i.Owner));
        Assert.Equal(PsbtRefusalReason.NothingToSign, review.Refusal!.Reason);
    }

    [Fact]
    public void FirstApplicableReason_Wins()
    {
        var coin = Fund(Taproot(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb + 1).WithVersion(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer)).Build();
        psbt.Inputs[0].SighashType = SigHash.None;

        Assert.Equal(PsbtRefusalReason.UnsupportedDigiDollar, RefusalFor(psbt));

        var plain = Builder().AddInput(coin).Pay(OneDgb + 1).Build();
        plain.Inputs[0].SighashType = SigHash.None;
        Assert.Equal(PsbtRefusalReason.UnsupportedSignatureType, RefusalFor(plain));
    }

    [Fact]
    public void Refusal_BlocksSigning_EvenWithEverythingElseInOrder()
    {
        var coin = Fund(SegWit(Wallet), OneDgb);
        var psbt = Builder().AddInput(coin).Pay(OneDgb - 100_000).Build();
        psbt.Inputs[0].SighashType = SigHash.None;

        var review = Analyze(psbt, [Unspent(coin)]);

        Assert.False(review.CanSign);
        Assert.False(review.CanBroadcastSigned);
    }
}

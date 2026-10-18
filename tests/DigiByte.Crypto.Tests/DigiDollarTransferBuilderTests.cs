using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// T029: building and signing DigiDollar transfers (research R1, R4, R5, R15; FR-011..FR-014):
/// nVersion 0x02000770, value-0 P2TR outputs, one DD OP_RETURN, key-path Schnorr for DD inputs, P2WPKH for the DGB fee.
/// A golden-vector comparison against a real testnet26 transaction is added once task T002 can run on a node.
/// </summary>
public class DigiDollarTransferBuilderTests
{
    private const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static readonly DigiDollarKeyDerivation Hd = new(new Mnemonic(Mnemonic, Wordlist.English), network: "testnet");
    private static readonly DigiDollarKeyDerivation Other = new(new Mnemonic(Mnemonic, Wordlist.English), passphrase: "other", network: "testnet");

    private static DigiDollarKey Recv(int i) => Hd.Derive(DigiDollarChain.Receive, i);
    private static DigiDollarKey Chg(int i) => Hd.Derive(DigiDollarChain.Change, i);

    private static string RecipientAddress => Other.GetAddress(Other.Derive(DigiDollarChain.Receive, 0));

    private static string Txid(int n) => n.ToString("x").PadLeft(64, '0');

    private static DdInput DdIn(int n, long cents, int keyIndex = 0) => new(Txid(n), 0, cents, Recv(keyIndex));

    private static (Key Key, DgbFeeInput Input) Fee(int n, long sats)
    {
        var key = new Key(Enumerable.Repeat((byte)(n + 1), 32).ToArray());
        return (key, new DgbFeeInput(Txid(100 + n), 0, sats, key));
    }

    private static Script DgbChange => new Key(Enumerable.Repeat((byte)77, 32).ToArray()).PubKey.WitHash.ScriptPubKey;

    private static DdTransferRequest Request(
        IReadOnlyList<DdInput>? dd = null, IReadOnlyList<DgbFeeInput>? fees = null, long amount = 1000,
        DigiDollarKey? change = null, string? recipient = null, long feeRate = 0, string network = "testnet") =>
        new(network, dd ?? [DdIn(1, 2500)], fees ?? [Fee(1, 50_000_000).Input], recipient ?? RecipientAddress, amount,
            change ?? Chg(0), DgbChange, feeRate);

    // ---------- layout ----------

    [Fact]
    public void Builds_TheCoreTransferLayout()
    {
        var result = DigiDollarTransferBuilder.Build(Request());
        var tx = result.Transaction;

        Assert.Equal(0x02000770u, tx.Version);
        Assert.True(DigiDollarScripts.IsDigiDollarVersion(tx.Version));

        // inputs: DD first, then the DGB fee input
        Assert.Equal(2, tx.Inputs.Count);
        Assert.Equal(Txid(1), tx.Inputs[0].PrevOut.Hash.ToString());
        Assert.Equal(Txid(101), tx.Inputs[1].PrevOut.Hash.ToString());

        // outputs: recipient DD, DD change, DGB change, OP_RETURN (last)
        Assert.Equal(4, tx.Outputs.Count);
        Assert.All(tx.Outputs.Take(2), o => Assert.Equal(Money.Zero, o.Value));
        Assert.True(DigiDollarScripts.TryGetOutputKey(tx.Outputs[0].ScriptPubKey, out var recipientKey));
        Assert.True(DigiDollarScripts.TryGetOutputKey(tx.Outputs[1].ScriptPubKey, out var changeKey));
        Assert.True(DigiDollarAddressValidator.TryDecode(RecipientAddress, "testnet", out _, out var expectedRecipient));
        Assert.Equal(expectedRecipient, recipientKey);       // the recipient address' key is used as-is
        Assert.Equal(Chg(0).OutputKey.ToBytes(), changeKey); // change goes to the supplied change key (its output key)
        Assert.True(tx.Outputs[2].Value > Money.Zero);        // DGB change
        Assert.True(DigiDollarScripts.TryParseOpReturn(tx.Outputs[3].ScriptPubKey, out var type, out var amounts));
        Assert.Equal(DigiDollarTxType.Transfer, type);
        Assert.Equal(new long[] { 1000, 1500 }, amounts); // recipient first, then change
    }

    [Fact]
    public void ConservesDigiDollar_InEqualsOut()
    {
        var result = DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, 700), DdIn(2, 1800, keyIndex: 1)], amount: 1000));
        var amounts = ParseAmounts(result.Transaction);

        Assert.Equal(2500, amounts.Sum());
        Assert.Equal(1500, result.DdChangeCents);
    }

    [Fact]
    public void SendAll_HasNoDdChangeOutput_AndASingleAmount()
    {
        var result = DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, 2500)], amount: 2500, change: null));
        var tx = result.Transaction;

        Assert.Equal(0, result.DdChangeCents);
        Assert.Equal(new long[] { 2500 }, ParseAmounts(tx));
        // recipient DD output, DGB change, OP_RETURN
        Assert.Equal(3, tx.Outputs.Count);
        Assert.Single(tx.Outputs.Where(o => o.Value == Money.Zero && DigiDollarScripts.TryGetOutputKey(o.ScriptPubKey, out _)));
    }

    [Fact]
    public void SpendsEveryDdInput_BeforeTheFeeInputs()
    {
        var result = DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, 700), DdIn(2, 900, 1), DdIn(3, 900, 2)], amount: 2500, change: null));
        var ins = result.Transaction.Inputs.Select(i => i.PrevOut.Hash.ToString()).ToList();

        Assert.Equal([Txid(1), Txid(2), Txid(3), Txid(101)], ins);
    }

    [Fact]
    public void OpReturn_IsTheOnlyDdOpReturn_AndMatchesTheDdOutputCount()
    {
        var tx = DigiDollarTransferBuilder.Build(Request()).Transaction;
        var ddOutputs = tx.Outputs.Count(o => o.Value == Money.Zero && DigiDollarScripts.TryGetOutputKey(o.ScriptPubKey, out _));
        var opReturns = tx.Outputs.Count(o => DigiDollarScripts.TryParseOpReturn(o.ScriptPubKey, out _, out _));

        Assert.Equal(1, opReturns);
        Assert.Equal(ddOutputs, ParseAmounts(tx).Count);
    }

    // ---------- fee (FR-014, R5) ----------

    [Fact]
    public void Fee_IsAtLeastTheProtocolMinimum_EvenAtZeroFeeRate()
    {
        var result = DigiDollarTransferBuilder.Build(Request(feeRate: 0));

        Assert.Equal(DigiDollarTransferBuilder.MinFeeSats, result.FeeSats);
        Assert.Equal(10_000_000, result.FeeSats); // 0.1 DGB
        Assert.Equal(50_000_000 - 10_000_000, result.DgbChangeSats);
    }

    [Fact]
    public void Fee_Grows_WithAHighFeeRate_ButNeverShrinksBelowTheMinimum()
    {
        var low = DigiDollarTransferBuilder.Build(Request(feeRate: 5));
        var high = DigiDollarTransferBuilder.Build(Request(feeRate: 200_000, fees: [Fee(1, 900_000_000).Input]));

        Assert.Equal(DigiDollarTransferBuilder.MinFeeSats, low.FeeSats);
        Assert.True(high.FeeSats > DigiDollarTransferBuilder.MinFeeSats);
    }

    [Fact]
    public void FeeInputs_AreSelectedLargestFirst_OnlyAsManyAsNeeded()
    {
        var small = Fee(1, 3_000_000).Input;
        var big = Fee(2, 50_000_000).Input;
        var mid = Fee(3, 20_000_000).Input;

        var result = DigiDollarTransferBuilder.Build(Request(fees: [small, big, mid]));

        var feeInputs = result.Transaction.Inputs.Skip(1).Select(i => i.PrevOut.Hash.ToString()).ToList();
        Assert.Equal([big.Txid], feeInputs);
    }

    [Fact]
    public void FeeInputs_Combine_WhenNoSingleOneCoversTheFee()
    {
        var a = Fee(1, 6_000_000).Input;
        var b = Fee(2, 6_000_000).Input;

        var result = DigiDollarTransferBuilder.Build(Request(fees: [a, b]));

        Assert.Equal(3, result.Transaction.Inputs.Count);
        Assert.Equal(12_000_000 - result.FeeSats, result.DgbChangeSats);
    }

    [Fact]
    public void DustDgbChange_IsAbsorbedIntoTheFee_NotOutput()
    {
        // 10,000,500 in, 10,000,000 fee: 500 sats of change is below the 1000-sat output threshold.
        var result = DigiDollarTransferBuilder.Build(Request(fees: [Fee(1, 10_000_500).Input]));

        Assert.Equal(0, result.DgbChangeSats);
        Assert.Equal(3, result.Transaction.Outputs.Count); // recipient, DD change, OP_RETURN
    }

    // ---------- signatures (R4) ----------

    [Fact]
    public void DdInputs_AreKeyPathSchnorrSigned_AndVerifyAgainstTheOutputKey()
    {
        var (feeKey, fee) = Fee(1, 50_000_000);
        var dd = new[] { DdIn(1, 700, 0), DdIn(2, 1800, 1) };
        var result = DigiDollarTransferBuilder.Build(Request(dd: dd, fees: [fee], amount: 1000));
        var tx = result.Transaction;

        var spent = SpentOutputs(dd, fee, feeKey);
        var pre = tx.PrecomputeTransactionData(spent);

        for (var i = 0; i < dd.Length; i++)
        {
            var witness = tx.Inputs[i].WitScript;
            Assert.Equal(1, witness.PushCount); // key path: just the signature
            Assert.True(TaprootSignature.TryParse(witness[0], out var sig));
            Assert.Equal(TaprootSigHash.Default, sig!.SigHash);
            var hash = tx.GetSignatureHashTaproot(pre, new TaprootExecutionData(i));
            Assert.True(dd[i].Key.OutputKey.VerifySignature(hash, sig.SchnorrSignature), $"DD input {i}");
        }
    }

    [Fact]
    public void FeeInput_IsP2wpkhSigned_AndVerifies()
    {
        var (feeKey, fee) = Fee(1, 50_000_000);
        var dd = new[] { DdIn(1, 2500) };
        var tx = DigiDollarTransferBuilder.Build(Request(dd: dd, fees: [fee])).Transaction;

        var spent = SpentOutputs(dd, fee, feeKey);
        var pre = tx.PrecomputeTransactionData(spent);
        var witness = tx.Inputs[1].WitScript;

        Assert.Equal(2, witness.PushCount); // <sig> <pubkey>
        Assert.Equal(feeKey.PubKey.ToBytes(), witness[1]);
        var hash = tx.GetSignatureHash(feeKey.PubKey.Hash.ScriptPubKey, 1, SigHash.All, spent[1], HashVersion.WitnessV0, pre);
        var sig = new TransactionSignature(witness[0]);
        Assert.Equal(SigHash.All, sig.SigHash);
        Assert.True(feeKey.PubKey.Verify(hash, sig.Signature));
    }

    [Fact]
    public void Result_Txid_MatchesTheTransaction_AndHexRoundTrips()
    {
        var result = DigiDollarTransferBuilder.Build(Request());

        Assert.Equal(result.Transaction.GetHash().ToString(), result.Txid);
        var parsed = Transaction.Parse(result.Hex, DigiByteNetwork.Testnet);
        Assert.Equal(result.Txid, parsed.GetHash().ToString());
        Assert.Equal(0x02000770u, parsed.Version);
    }

    // ---------- negative paths (Constitution III) ----------

    private static DdBuildError Error(DdTransferRequest r) =>
        Assert.Throws<DdBuildException>(() => DigiDollarTransferBuilder.Build(r)).Error;

    [Theory]
    [InlineData(99)]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-100)]
    public void AmountBelowTheMinimum_Throws(long amount) =>
        Assert.Equal(DdBuildError.BelowMinimum, Error(Request(amount: amount)));

    [Fact]
    public void AmountAboveTheMaximum_Throws() =>
        Assert.Equal(DdBuildError.AboveMaximum, Error(Request(dd: [DdIn(1, 20_000_000)], amount: 10_000_001)));

    [Fact]
    public void ExactMinimumAndMaximum_AreAccepted()
    {
        DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, 1100)], amount: 100));
        DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, 10_000_000)], amount: 10_000_000, change: null));
    }

    [Theory]
    [InlineData(2500, 2450)] // change 50
    [InlineData(2500, 2401)] // change 99
    [InlineData(101, 100)]   // change 1
    public void ChangeBetweenZeroAndTheMinimum_Throws(long balance, long amount) =>
        Assert.Equal(DdBuildError.ChangeBelowMinimum, Error(Request(dd: [DdIn(1, balance)], amount: amount)));

    [Theory]
    [InlineData(2500, 2400)] // change exactly 100: allowed
    public void ChangeExactlyTheMinimum_IsAllowed(long balance, long amount)
    {
        var r = DigiDollarTransferBuilder.Build(Request(dd: [DdIn(1, balance)], amount: amount));
        Assert.Equal(100, r.DdChangeCents);
    }

    [Fact]
    public void ChangeAboveTheMaximum_Throws() =>
        Assert.Equal(DdBuildError.ChangeAboveMaximum, Error(Request(dd: [DdIn(1, 25_000_000)], amount: 1000)));

    [Fact]
    public void InsufficientDd_Throws() =>
        Assert.Equal(DdBuildError.InsufficientDd, Error(Request(dd: [DdIn(1, 500)], amount: 1000)));

    [Fact]
    public void InsufficientDgbForTheFee_Throws()
    {
        Assert.Equal(DdBuildError.InsufficientDgbFee, Error(Request(fees: [Fee(1, 9_999_999).Input])));
        Assert.Equal(DdBuildError.InsufficientDgbFee, Error(Request(fees: [])));
    }

    [Fact]
    public void ChangeNeeded_ButNoChangeKey_Throws() =>
        Assert.Equal(DdBuildError.ChangeKeyRequired, Error(Request(dd: [DdIn(1, 2500)], amount: 1000, change: null) with { ChangeKey = null }));

    [Fact]
    public void InvalidRecipient_Throws()
    {
        Assert.Equal(DdBuildError.InvalidRecipient, Error(Request(recipient: "not-an-address")));
        Assert.Equal(DdBuildError.InvalidRecipient, Error(Request(recipient: "dgbt1qw508d6qejxtdg4y5r3zarvary0c5xw7kxxxxxx")));
        Assert.Equal(DdBuildError.InvalidRecipient, Error(Request(recipient: "")));
    }

    [Fact]
    public void RecipientOnTheOtherNetwork_Throws()
    {
        var mainnetRecipient = new DigiDollarKeyDerivation(new Mnemonic(Mnemonic, Wordlist.English), network: "mainnet")
            .GetAddress(new DigiDollarKeyDerivation(new Mnemonic(Mnemonic, Wordlist.English), network: "mainnet").Derive(DigiDollarChain.Receive, 0));

        Assert.Equal(DdBuildError.InvalidRecipient, Error(Request(recipient: mainnetRecipient)));
    }

    [Fact]
    public void BadInputs_Throw()
    {
        Assert.Equal(DdBuildError.InvalidInput, Error(Request(dd: [])));
        Assert.Equal(DdBuildError.InvalidInput, Error(Request(dd: [DdIn(1, 0)])));
        Assert.Equal(DdBuildError.InvalidInput, Error(Request(dd: [DdIn(1, -5)])));
        Assert.Equal(DdBuildError.InvalidInput, Error(Request(network: "regtest")));
    }

    [Fact]
    public void DuplicateDdInputs_Throw() =>
        Assert.Equal(DdBuildError.InvalidInput, Error(Request(dd: [DdIn(1, 2500), DdIn(1, 2500)])));

    // ---------- helpers ----------

    private static List<long> ParseAmounts(Transaction tx)
    {
        foreach (var o in tx.Outputs)
            if (DigiDollarScripts.TryParseOpReturn(o.ScriptPubKey, out _, out var a)) return a;
        throw new InvalidOperationException("no DD OP_RETURN");
    }

    private static TxOut[] SpentOutputs(IReadOnlyList<DdInput> dd, DgbFeeInput fee, Key feeKey) =>
        dd.Select(d => new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(d.Key.OutputKey.ToBytes())))
          .Append(new TxOut(Money.Satoshis(fee.ValueSats), feeKey.PubKey.WitHash.ScriptPubKey))
          .ToArray();
}

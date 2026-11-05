using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>Which data a test PSBT carries for an input.</summary>
internal enum PsbtInputData
{
    None,
    WitnessUtxo,
    PreviousTransaction,
}

/// <summary>A coin created by a made-up funding transaction.</summary>
internal sealed record FundedCoin(Coin Coin, Transaction FundingTransaction)
{
    public OutPoint Outpoint => Coin.Outpoint;
    public Script ScriptPubKey => Coin.ScriptPubKey;
    public long Amount => Coin.Amount.Satoshi;
}

/// <summary>
/// Builds unsigned PSBTs on DigiByte mainnet for tests. Shared by the crypto and wallet test projects (linked into the latter).
/// </summary>
internal static class PsbtTestFixtures
{
    public static readonly Network Net = DigiByteNetwork.Mainnet;

    public static Script SegWit(Key key) => key.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);
    public static Script Legacy(Key key) => key.PubKey.GetScriptPubKey(ScriptPubKeyType.Legacy);
    public static Script WrappedSegWit(Key key) => key.PubKey.GetScriptPubKey(ScriptPubKeyType.SegwitP2SH);
    public static Script Taproot(Key key) => key.GetScriptPubKey(ScriptPubKeyType.TaprootBIP86);

    public static Script MultisigRedeem(Key a, Key b) => PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, a.PubKey, b.PubKey);

    public static Script Stranger() => new Key().PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);

    public static FundedCoin Fund(Script scriptPubKey, long satoshis)
    {
        var funding = Net.CreateTransaction();
        funding.Inputs.Add(new OutPoint(RandomUtils.GetUInt256(), 0));
        funding.Outputs.Add(Money.Satoshis(satoshis), scriptPubKey);
        return new FundedCoin(new Coin(funding, 0u), funding);
    }

    public static PsbtBuilder Builder() => new();

    /// <summary>A PSBT whose global transaction lists the same input twice. NBitcoin refuses to build one, so the bytes are patched.</summary>
    public static byte[] DuplicateInputBytes()
    {
        var a = Fund(Stranger(), 100_000_000);
        var b = Fund(Stranger(), 100_000_000);
        var bytes = Builder().AddInput(a).AddInput(b).Pay(150_000_000).Build().ToBytes();
        var from = b.Outpoint.ToBytes();
        var to = a.Outpoint.ToBytes();
        var at = IndexOf(bytes, from);
        if (at < 0) throw new InvalidOperationException("Outpoint not found in the PSBT bytes.");
        Array.Copy(to, 0, bytes, at, to.Length);
        return bytes;
    }

    /// <summary>A PSBT whose global transaction has no inputs and one output. NBitcoin refuses to build one, so it is written by hand.</summary>
    public static byte[] NoInputsBytes() => Convert.FromHexString(
        "70736274ff" +                  // magic
        "01" + "00" + "14" +            // global unsigned transaction: key length, key type, value length (20)
        "02000000" + "00" + "01" +      // version 2, no inputs, one output
        "e803000000000000" + "01" + "51" + // 1000 satoshis to OP_TRUE
        "00000000" +                    // lock time
        "00" +                          // end of the global map
        "00");                          // the one output's empty map

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                match = haystack[i + j] == needle[j];
            if (match) return i;
        }
        return -1;
    }
}

internal sealed class PsbtBuilder
{
    private readonly List<(FundedCoin Coin, PsbtInputData Data, Script? WitnessScript, Script? RedeemScript)> _inputs = [];
    private readonly List<TxOut> _outputs = [];
    private uint _version = 2;
    private uint _lockTime;

    /// <summary>Adds an input. By default a legacy input carries its previous transaction and anything else its witness UTXO.</summary>
    public PsbtBuilder AddInput(FundedCoin coin, PsbtInputData? data = null, Script? witnessScript = null, Script? redeemScript = null)
    {
        var defaultData = coin.ScriptPubKey.IsScriptType(ScriptType.P2PKH) ? PsbtInputData.PreviousTransaction : PsbtInputData.WitnessUtxo;
        _inputs.Add((coin, data ?? defaultData, witnessScript, redeemScript));
        return this;
    }

    public PsbtBuilder AddOutput(Script scriptPubKey, long satoshis)
    {
        _outputs.Add(new TxOut(Money.Satoshis(satoshis), scriptPubKey));
        return this;
    }

    /// <summary>Pays a stranger.</summary>
    public PsbtBuilder Pay(long satoshis) => AddOutput(PsbtTestFixtures.Stranger(), satoshis);

    public PsbtBuilder AddDataOutput(byte[] data) => AddOutput(TxNullDataTemplate.Instance.GenerateScriptPubKey(data), 0);

    public PsbtBuilder WithVersion(uint version)
    {
        _version = version;
        return this;
    }

    public PsbtBuilder WithLockTime(uint lockTime)
    {
        _lockTime = lockTime;
        return this;
    }

    public PSBT Build()
    {
        var tx = PsbtTestFixtures.Net.CreateTransaction();
        tx.Version = _version;
        tx.LockTime = new LockTime(_lockTime);
        foreach (var input in _inputs)
            tx.Inputs.Add(new TxIn(input.Coin.Outpoint) { Sequence = Sequence.Final });
        foreach (var output in _outputs)
            tx.Outputs.Add(output);

        var psbt = PSBT.FromTransaction(tx, PsbtTestFixtures.Net);
        for (var i = 0; i < _inputs.Count; i++)
        {
            var (coin, data, witnessScript, redeemScript) = _inputs[i];
            if (data == PsbtInputData.WitnessUtxo)
                psbt.Inputs[i].WitnessUtxo = coin.Coin.TxOut;
            else if (data == PsbtInputData.PreviousTransaction)
                psbt.Inputs[i].NonWitnessUtxo = coin.FundingTransaction;
            if (witnessScript is not null) psbt.Inputs[i].WitnessScript = witnessScript;
            if (redeemScript is not null) psbt.Inputs[i].RedeemScript = redeemScript;
        }
        return psbt;
    }
}

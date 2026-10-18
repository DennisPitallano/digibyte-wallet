using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Transactions;
using DigiByte.NodeApi.DigiDollar;
using NBitcoin;

namespace DigiByte.NodeApi.Tests;

/// <summary>Builds synthetic DigiDollar transactions shaped exactly like Core's transfer builder (research R1).</summary>
internal static class DdTestTx
{
    public static byte[] Key(int seed) =>
        new Key(Enumerable.Range(seed, 32).Select(i => (byte)i).ToArray()).PubKey.TaprootInternalKey.ToBytes();

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public static Transaction NewTx(uint version, params (string Txid, int Vout)[] inputs)
    {
        var tx = DigiByteNetwork.Testnet.CreateTransaction();
        tx.Version = version;
        foreach (var (txid, vout) in inputs)
            tx.Inputs.Add(new OutPoint(uint256.Parse(txid), (uint)vout));
        return tx;
    }

    /// <summary>A DD transfer: DD outputs first (recipients then change), a DGB change output, then the DD OP_RETURN last.</summary>
    public static Transaction Transfer(
        (string Txid, int Vout)[] inputs, (byte[] Key, long Cents)[] ddOutputs, long dgbChangeSats = 5_000_000)
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), inputs);
        foreach (var (key, _) in ddOutputs)
            tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(key)));
        if (dgbChangeSats > 0)
            tx.Outputs.Add(new TxOut(Money.Satoshis(dgbChangeSats), new Key().PubKey.WitHash.ScriptPubKey));
        tx.Outputs.Add(new TxOut(Money.Zero,
            DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, ddOutputs.Select(o => o.Cents).ToArray())));
        return tx;
    }

    public static string Txid(int n) => n.ToString("x").PadLeft(64, '0');
}

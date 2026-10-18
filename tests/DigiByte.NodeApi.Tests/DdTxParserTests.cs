using DigiByte.Crypto.Transactions;
using DigiByte.NodeApi.DigiDollar;
using NBitcoin;
using static DigiByte.NodeApi.Tests.DdTestTx;

namespace DigiByte.NodeApi.Tests;

/// <summary>T014: the indexer decodes DigiDollar outputs the way Core validates them.</summary>
public class DdTxParserTests
{
    [Fact]
    public void ParsesATransfer_OutputsPairedWithAmountsInOrder_AndSpends()
    {
        var tx = Transfer([(Txid(1), 0), (Txid(2), 3)], [(Key(1), 500), (Key(2), 1500)]);

        var parsed = DdTxParser.Parse(tx);

        Assert.NotNull(parsed);
        Assert.Equal(DigiDollarTxType.Transfer, parsed!.Type);
        Assert.Equal(tx.GetHash().ToString(), parsed.Txid);
        Assert.Equal([0, 1], parsed.Outputs.Select(o => o.Vout));
        Assert.Equal([500L, 1500L], parsed.Outputs.Select(o => o.AmountCents));
        Assert.Equal(Key(1), parsed.Outputs[0].OutputKey);
        Assert.Equal(Key(2), parsed.Outputs[1].OutputKey);
        Assert.Equal("5120" + Hex(Key(1)), parsed.Outputs[0].ScriptPubKeyHex);
        Assert.Equal([new DdOutpointRef(Txid(1), 0), new DdOutpointRef(Txid(2), 3)], parsed.Spends);
    }

    [Fact]
    public void DgbChangeOutputBetweenDdOutputs_DoesNotShiftAmounts()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        tx.Outputs.Add(new TxOut(Money.Satoshis(1000), new Key().PubKey.WitHash.ScriptPubKey)); // DGB change in the middle
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(2))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [700, 300])));

        var parsed = DdTxParser.Parse(tx)!;

        Assert.Equal([0, 2], parsed.Outputs.Select(o => o.Vout));
        Assert.Equal([700L, 300L], parsed.Outputs.Select(o => o.AmountCents));
    }

    [Fact]
    public void OpReturnPosition_IsNotRequiredToBeLast()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [250])));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));

        var parsed = DdTxParser.Parse(tx);

        Assert.NotNull(parsed);
        Assert.Equal(250, parsed!.Outputs[0].AmountCents);
        Assert.Equal(1, parsed.Outputs[0].Vout);
    }

    [Fact]
    public void NonDdVersion_IsNull() =>
        Assert.Null(DdTxParser.Parse(NewTx(2, (Txid(1), 0))));

    [Fact]
    public void NoOpReturn_IsNull()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        Assert.Null(DdTxParser.Parse(tx));
    }

    [Fact]
    public void TwoDdOpReturns_IsNull()
    {
        var tx = Transfer([(Txid(1), 0)], [(Key(1), 100)]);
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [100])));
        Assert.Null(DdTxParser.Parse(tx));
    }

    [Fact]
    public void MoreAmountsThanDdOutputs_IsNull()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [100, 200])));
        Assert.Null(DdTxParser.Parse(tx));
    }

    [Fact]
    public void MoreDdOutputsThanAmounts_IsNull()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(2))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [100])));
        Assert.Null(DdTxParser.Parse(tx));
    }

    [Fact]
    public void OpReturnTypeNotMatchingVersionType_IsNull()
    {
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Mint, [100])));
        Assert.Null(DdTxParser.Parse(tx));
    }

    [Fact]
    public void ValuedP2trOutput_IsNotADdOutput()
    {
        // e.g. a mint collateral output: P2TR but with value, so it must not consume a DD amount.
        var tx = NewTx(DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer), (Txid(1), 0));
        tx.Outputs.Add(new TxOut(Money.Satoshis(50_000), DigiDollarScripts.CreateOutputScript(Key(9))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.CreateOutputScript(Key(1))));
        tx.Outputs.Add(new TxOut(Money.Zero, DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [100])));

        var parsed = DdTxParser.Parse(tx)!;

        Assert.Single(parsed.Outputs);
        Assert.Equal(1, parsed.Outputs[0].Vout);
    }

    [Fact]
    public void UnrelatedOpReturn_IsIgnored()
    {
        var tx = Transfer([(Txid(1), 0)], [(Key(1), 100)]);
        tx.Outputs.Add(new TxOut(Money.Zero, new Script(OpcodeType.OP_RETURN, Op.GetPushOp("hello"u8.ToArray()))));

        Assert.NotNull(DdTxParser.Parse(tx));
    }

    [Fact]
    public void CoinbaseStyleNullPrevout_IsNotASpend()
    {
        var tx = Transfer([(Txid(1), 0)], [(Key(1), 100)]);
        tx.Inputs.Add(new TxIn(new OutPoint())); // null prevout
        Assert.Single(DdTxParser.Parse(tx)!.Spends);
    }
}

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

    // Real mainnet mint (block 24,402,725): a collateral P2TR output, one value-0 DD output, a P2WPKH change output, and a
    // mint OP_RETURN whose first amount (50000 cents) is the DD value.
    private const string RealMintHex = "70070001000101d522d076ac2683d75bfb3aed766293950f4a2c2a4aaeda0ccfc007202b63de6e0000000000ffffffff04d4a931fd4928000022512009efac6fac6b48fe75e969d2bdb99c7e2a5d39c3714b4a25c29ef2fb3acb94ab0000000000000000225120d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f0000000000000000326a02444401010350c30004e06f94010104207a1ec77082468ddd221e6ba5e5bcd4591d33cbfb98552039b0f48eb1fc38bc68ec2658043c080000160014b8adadceed895da7ac32e073293aa5577a77d4d20247304402207ece91ee831791f74ec5f5decb1a2a5a283a465d3809f61300d2c7b043ef106402200c4b71367ae497c0857bef5e8c0ab54c1819cfbc4cb1bbc980060629773f40370121033a0b8654273468abe2b88316074cc130048670d0924d2ab867d0c05bf7c6c0bb00000000";

    [Fact]
    public void ParsesTheRealMainnetMint_OneDdOutputWithTheFirstAmount()
    {
        var tx = Transaction.Parse(RealMintHex, DigiByte.Crypto.Networks.DigiByteNetworkSet.Instance.Mainnet);

        var parsed = DdTxParser.Parse(tx);

        Assert.NotNull(parsed);
        Assert.Equal(DigiDollarTxType.Mint, parsed!.Type);
        Assert.Equal("c59a6716028c720666e7c07c0de63bcca4388f1051013e7e7eb7635e4a6cf011", parsed.Txid);
        var o = Assert.Single(parsed.Outputs);
        Assert.Equal(1, o.Vout);                                   // vout 0 is the DGB collateral output, not DigiDollar
        Assert.Equal(50000, o.AmountCents);
        Assert.Equal("d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f", Hex(o.OutputKey));
    }

    [Fact]
    public void Mint_WithTwoValueZeroDdOutputs_IsRejected()
    {
        var tx = Transaction.Parse(RealMintHex, DigiByte.Crypto.Networks.DigiByteNetworkSet.Instance.Mainnet);
        tx.Outputs.Add(Money.Zero, tx.Outputs[1].ScriptPubKey); // a second DD-shaped output

        Assert.Null(DdTxParser.Parse(tx));
    }
}

using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class MultiOutputTransactionTests
{
    private readonly Network _network = DigiByteNetwork.Mainnet;
    private readonly DigiByteTransactionBuilder _sut = new(DigiByteNetwork.Mainnet);

    private (List<Utxo> Utxos, BitcoinAddress Change) CreateFundedWallet(int utxoCount = 1, long satoshisEach = 10_000_000_00)
    {
        var key = new Key();
        var addr = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);
        var utxos = new List<Utxo>();
        for (int i = 0; i < utxoCount; i++)
        {
            utxos.Add(new Utxo
            {
                TransactionId = RandomUtils.GetUInt256(),
                OutputIndex = 0,
                Amount = Money.Satoshis(satoshisEach),
                ScriptPubKey = addr.ScriptPubKey,
                PrivateKey = key,
                Confirmations = 6,
            });
        }
        return (utxos, addr);
    }

    // ─── BuildMultiOutputTransaction ───

    [Fact]
    public void MultiOutput_TwoRecipients_ProducesCorrectOutputCount()
    {
        var (utxos, change) = CreateFundedWallet();
        var dest1 = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);
        var dest2 = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);

        var outputs = new List<(BitcoinAddress, Money)>
        {
            (dest1, Money.Coins(1m)),
            (dest2, Money.Coins(2m)),
        };

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)));

        // At least 2 recipient outputs + 1 change
        Assert.True(tx.Outputs.Count >= 3);
    }

    [Fact]
    public void MultiOutput_WithMemo_IncludesOpReturn()
    {
        var (utxos, change) = CreateFundedWallet();
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);

        var outputs = new List<(BitcoinAddress, Money)>
        {
            (dest, Money.Coins(1m)),
        };

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)), memo: "batch test");

        var opReturn = tx.Outputs.FirstOrDefault(o => o.ScriptPubKey.IsUnspendable);
        Assert.NotNull(opReturn);
        Assert.Equal(Money.Zero, opReturn.Value);
    }

    [Fact]
    public void MultiOutput_WithoutMemo_NoOpReturn()
    {
        var (utxos, change) = CreateFundedWallet();
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);

        var outputs = new List<(BitcoinAddress, Money)>
        {
            (dest, Money.Coins(1m)),
        };

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)));

        var opReturn = tx.Outputs.FirstOrDefault(o => o.ScriptPubKey.IsUnspendable);
        Assert.Null(opReturn);
    }

    [Fact]
    public void MultiOutput_MemoTruncatedTo80Bytes()
    {
        var (utxos, change) = CreateFundedWallet();
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);
        var longMemo = new string('A', 200);

        var outputs = new List<(BitcoinAddress, Money)>
        {
            (dest, Money.Coins(1m)),
        };

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)), memo: longMemo);

        var opReturn = tx.Outputs.FirstOrDefault(o => o.ScriptPubKey.IsUnspendable);
        Assert.NotNull(opReturn);

        // OP_RETURN + OP_PUSHDATA + data ≤ 80 bytes of payload
        var ops = opReturn.ScriptPubKey.ToOps().ToList();
        var pushData = ops.Last().PushData;
        Assert.NotNull(pushData);
        Assert.True(pushData.Length <= 80);
    }

    [Fact]
    public void MultiOutput_FiveRecipients_AllReceiveCorrectAmounts()
    {
        var (utxos, change) = CreateFundedWallet(utxoCount: 1, satoshisEach: 100_000_000_00);
        var amounts = new[] { 1m, 2m, 3m, 4m, 5m };
        var outputs = amounts.Select(a =>
        {
            var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);
            return (dest, Money.Coins(a));
        }).ToList();

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)));

        foreach (var (addr, money) in outputs)
        {
            var matchingOutput = tx.Outputs.FirstOrDefault(o =>
                o.ScriptPubKey == addr.ScriptPubKey && o.Value == money);
            Assert.NotNull(matchingOutput);
        }
    }

    [Fact]
    public void MultiOutput_TransactionIsValid()
    {
        var (utxos, change) = CreateFundedWallet();
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);

        var outputs = new List<(BitcoinAddress, Money)>
        {
            (dest, Money.Coins(1m)),
        };

        var tx = _sut.BuildMultiOutputTransaction(utxos, outputs, change, new FeeRate(Money.Satoshis(100_000)));
        var coins = utxos.Select(u => u.ToCoin()).ToArray();

        Assert.True(_sut.Verify(tx, coins));
    }
}

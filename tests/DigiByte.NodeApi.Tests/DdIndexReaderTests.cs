using DigiByte.Crypto.Networks;
using DigiByte.NodeApi.DigiDollar;
using static DigiByte.NodeApi.Tests.DdTestTx;

namespace DigiByte.NodeApi.Tests;

/// <summary>T014: the reader merges confirmed rows with the mempool overlay and answers status queries.</summary>
public class DdIndexReaderTests : IDisposable
{
    private readonly DdIndexStore _store = DdIndexStore.InMemory();
    private readonly DdMempoolOverlay _mempool = new(DigiByteNetwork.Testnet);
    private readonly DdIndexStatus _status = new();
    private readonly FakeChainSource _chain = new();
    private readonly DdIndex _index;

    public DdIndexReaderTests()
    {
        _index = new DdIndex(_store, _mempool, _status, _chain);
        _status.Update(tip: 110, networkHeight: 110, lastIndexed: 110);
    }

    public void Dispose() => _store.Dispose();

    private DdParsedTx ApplyConfirmed(int height, NBitcoin.Transaction tx)
    {
        var parsed = DdTxParser.Parse(tx)!;
        _store.ApplyBlock(height, "h" + height, 1_700_000_000 + height, [(parsed, 10_000_000L)]);
        return parsed;
    }

    private static NBitcoin.Transaction Fund(int n, byte[] key, long cents) =>
        Transfer([(Txid(900 + n), 0)], [(key, cents)]);

    // ---------- UTXOs ----------

    [Fact]
    public async Task Utxos_CarryConfirmations_FromTheTip()
    {
        var fund = ApplyConfirmed(100, Fund(1, Key(1), 2500));

        var utxos = await _index.GetUtxosAsync([Key(1)], minConf: 0, default);

        var u = Assert.Single(utxos);
        Assert.Equal(fund.Txid, u.Txid);
        Assert.Equal(11, u.Confirmations); // tip 110 - height 100 + 1
        Assert.Equal(2500, u.AmountCents);
        Assert.Equal(0, u.ValueSats);
        Assert.Equal("5120" + Hex(Key(1)), u.ScriptPubKeyHex);
        Assert.False(u.SpentByMempool);
    }

    [Fact]
    public async Task Utxos_MinConf_Filters()
    {
        ApplyConfirmed(105, Fund(1, Key(1), 100)); // 6 confirmations
        ApplyConfirmed(110, Fund(2, Key(1), 200)); // 1 confirmation

        Assert.Equal(2, (await _index.GetUtxosAsync([Key(1)], 1, default)).Count);
        Assert.Single(await _index.GetUtxosAsync([Key(1)], 6, default));
        Assert.Empty(await _index.GetUtxosAsync([Key(1)], 7, default));
    }

    [Fact]
    public async Task Utxos_SpentInTheMempool_AreFlagged_AndTheirMempoolChangeIsListedAtMinConf0()
    {
        var fund = ApplyConfirmed(100, Fund(1, Key(1), 2500));
        var send = Transfer([(fund.Txid, 0)], [(Key(2), 1000), (Key(1), 1500)]);
        _mempool.Set([DdTxParser.Parse(send)!]);

        var utxos = await _index.GetUtxosAsync([Key(1)], minConf: 0, default);

        Assert.Equal(2, utxos.Count);
        Assert.True(utxos.Single(u => u.Confirmations > 0).SpentByMempool);
        var change = utxos.Single(u => u.Confirmations == 0);
        Assert.Equal(1500, change.AmountCents);
        Assert.Equal(Hex(Key(1)), Hex(change.OutputKey));

        // At minConf 1 only the confirmed (but mempool-spent) output is returned.
        Assert.Single(await _index.GetUtxosAsync([Key(1)], minConf: 1, default));
    }

    [Fact]
    public async Task Utxos_MempoolOutputConsumedByAnotherMempoolTx_IsExcluded()
    {
        var fund = ApplyConfirmed(100, Fund(1, Key(1), 2500));
        var first = Transfer([(fund.Txid, 0)], [(Key(1), 2500)]);
        var firstId = first.GetHash().ToString();
        var second = Transfer([(firstId, 0)], [(Key(2), 2500)]);
        _mempool.Set([DdTxParser.Parse(first)!, DdTxParser.Parse(second)!]);

        var utxos = await _index.GetUtxosAsync([Key(1)], 0, default);

        Assert.DoesNotContain(utxos, u => u.Txid == firstId); // consumed by `second`
    }

    [Fact]
    public async Task Utxos_ForUnknownKey_AreEmpty() =>
        Assert.Empty(await _index.GetUtxosAsync([Key(77)], 0, default));

    // ---------- history ----------

    [Fact]
    public async Task History_ListsMempoolFirst_ThenConfirmedNewestFirst_WithInputsAndOutputs()
    {
        var fund = ApplyConfirmed(100, Fund(1, Key(1), 2500));
        var s1 = ApplyConfirmed(101, Transfer([(fund.Txid, 0)], [(Key(2), 1000), (Key(1), 1500)]));
        var pending = Transfer([(s1.Txid, 1)], [(Key(3), 1500)]);
        _mempool.Set([DdTxParser.Parse(pending)!]);

        var page = await _index.GetHistoryAsync([Key(1)], count: 10, before: null, default);

        Assert.Equal([pending.GetHash().ToString(), s1.Txid, fund.Txid], page.Items.Select(i => i.Txid));

        var mem = page.Items[0];
        Assert.Null(mem.BlockHeight);
        Assert.Equal(0, mem.Confirmations);
        Assert.Equal([(Hex(Key(1)), 1500L)], mem.DdInputs.Select(a => (Hex(a.OutputKey), a.AmountCents)));
        Assert.Equal([(Hex(Key(3)), 1500L)], mem.DdOutputs.Select(a => (Hex(a.OutputKey), a.AmountCents)));

        var conf = page.Items[1];
        Assert.Equal(101, conf.BlockHeight);
        Assert.Equal(10, conf.Confirmations);
        Assert.Equal(10_000_000, conf.FeeSats);
        Assert.Equal([2500L], conf.DdInputs.Select(a => a.AmountCents));
        Assert.Equal([1000L, 1500L], conf.DdOutputs.Select(a => a.AmountCents));
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task History_PagesWithATxidCursor()
    {
        var a = ApplyConfirmed(100, Fund(1, Key(1), 100));
        var b = ApplyConfirmed(101, Fund(2, Key(1), 200));
        var c = ApplyConfirmed(102, Fund(3, Key(1), 300));

        var p1 = await _index.GetHistoryAsync([Key(1)], 2, null, default);
        Assert.Equal([c.Txid, b.Txid], p1.Items.Select(i => i.Txid));
        Assert.Equal(b.Txid, p1.Next);

        var p2 = await _index.GetHistoryAsync([Key(1)], 2, p1.Next, default);
        Assert.Equal([a.Txid], p2.Items.Select(i => i.Txid));
        Assert.Null(p2.Next);
    }

    [Fact]
    public async Task History_ForUnknownKey_IsEmpty()
    {
        var page = await _index.GetHistoryAsync([Key(77)], 10, null, default);
        Assert.Empty(page.Items);
        Assert.Null(page.Next);
    }

    // ---------- tx status ----------

    [Fact]
    public async Task TxStatus_Mempool_Confirmed_AndFallbackToTheNode()
    {
        var confirmed = ApplyConfirmed(105, Fund(1, Key(1), 100));
        var pending = DdTxParser.Parse(Transfer([(Txid(1), 0)], [(Key(2), 100)]))!;
        _mempool.Set([pending]);

        Assert.Equal(new ChainTxStatus(ChainTxState.Mempool, 0), await _index.GetTxStatusAsync(pending.Txid, default));
        Assert.Equal(new ChainTxStatus(ChainTxState.Confirmed, 6), await _index.GetTxStatusAsync(confirmed.Txid, default));

        // Not indexed yet (index lagging): ask the node, so a valid tx never looks dropped.
        _chain.Statuses[Txid(42)] = new ChainTxStatus(ChainTxState.Confirmed, 3);
        Assert.Equal(new ChainTxStatus(ChainTxState.Confirmed, 3), await _index.GetTxStatusAsync(Txid(42), default));

        Assert.Equal(ChainTxState.Unknown, (await _index.GetTxStatusAsync(Txid(43), default)).State);
    }

    // ---------- status holder ----------

    [Fact]
    public void IndexStatus_ReportsLag()
    {
        var s = new DdIndexStatus();
        Assert.Null(s.LagBlocks);

        s.Update(tip: 200, networkHeight: 205, lastIndexed: 195);

        Assert.Equal(5, s.LagBlocks);
        Assert.Equal(205, s.NetworkHeight);
        Assert.Equal(195, s.LastIndexedHeight);
    }
}

using DigiByte.NodeApi.DigiDollar;
using static DigiByte.NodeApi.Tests.DdTestTx;

namespace DigiByte.NodeApi.Tests;

/// <summary>T014: the SQLite DD output index: outputs, spends, history ordering, and reorg rollback.</summary>
public class DdIndexStoreTests : IDisposable
{
    private readonly DdIndexStore _store = DdIndexStore.InMemory();

    public void Dispose() => _store.Dispose();

    private static string TxidOf(DdParsedTx t) => t.Txid;

    private static DdParsedTx Mint(int n, byte[] key, long cents)
    {
        // A synthetic "funding" DD tx: no DD inputs, one DD output.
        var tx = Transfer([(Txid(100 + n), 0)], [(key, cents)]);
        return DdTxParser.Parse(tx)!;
    }

    private void Apply(int height, params DdParsedTx[] txs) =>
        _store.ApplyBlock(height, "hash" + height, 1_700_000_000 + height, txs.Select(t => (t, (long?)10_000_000)).ToList());

    [Fact]
    public void ApplyBlock_IndexesOutputsByKey_AndAdvancesState()
    {
        var a = Mint(1, Key(1), 2500);

        Apply(100, a);

        var rows = _store.GetOutputsByKeys([Key(1)], unspentOnly: true);
        Assert.Single(rows);
        Assert.Equal(2500, rows[0].AmountCents);
        Assert.Equal(100, rows[0].BlockHeight);
        Assert.Equal("5120" + Hex(Key(1)), rows[0].ScriptPubKeyHex);
        Assert.Equal(new DdIndexerState(100, "hash100"), _store.GetState());
        Assert.Empty(_store.GetOutputsByKeys([Key(2)], unspentOnly: true));
    }

    [Fact]
    public void Spend_MarksTheOutputSpent_AndUnspentOnlyExcludesIt()
    {
        var fund = Mint(1, Key(1), 2500);
        Apply(100, fund);

        var send = DdTxParser.Parse(Transfer([(fund.Txid, 0)], [(Key(2), 1000), (Key(1), 1500)]))!;
        Apply(101, send);

        Assert.Empty(_store.GetOutputsByKeys([Key(1)], unspentOnly: true).Where(r => r.Txid == fund.Txid));
        var spent = _store.GetOutputsSpentBy(send.Txid);
        Assert.Single(spent);
        Assert.Equal(fund.Txid, spent[0].Txid);
        Assert.Equal(101, spent[0].SpentHeight);
        // change (Key(1), 1500) and recipient (Key(2), 1000) are unspent
        Assert.Equal(1500, _store.GetOutputsByKeys([Key(1)], unspentOnly: true).Single().AmountCents);
        Assert.Equal(1000, _store.GetOutputsByKeys([Key(2)], unspentOnly: true).Single().AmountCents);
    }

    [Fact]
    public void SpendInTheSameBlock_AsTheCreation_IsMarked()
    {
        var fund = Mint(1, Key(1), 900);
        var send = DdTxParser.Parse(Transfer([(fund.Txid, 0)], [(Key(2), 900)]))!;

        Apply(100, fund, send);

        Assert.Empty(_store.GetOutputsByKeys([Key(1)], unspentOnly: true));
        Assert.Single(_store.GetOutputsByKeys([Key(2)], unspentOnly: true));
    }

    [Fact]
    public void SpendOfAnUnknownOutpoint_IsIgnored()
    {
        var send = DdTxParser.Parse(Transfer([(Txid(999), 0)], [(Key(2), 100)]))!;

        Apply(100, send);

        Assert.Single(_store.GetOutputsByKeys([Key(2)], unspentOnly: true));
    }

    [Fact]
    public void RollbackTo_RemovesLaterBlocks_AndUnspendsLaterSpends()
    {
        var fund = Mint(1, Key(1), 2500);
        Apply(100, fund);
        var send = DdTxParser.Parse(Transfer([(fund.Txid, 0)], [(Key(2), 2500)]))!;
        Apply(101, send);

        _store.RollbackTo(100, "hash100");

        Assert.Equal(new DdIndexerState(100, "hash100"), _store.GetState());
        Assert.Single(_store.GetOutputsByKeys([Key(1)], unspentOnly: true)); // un-spent again
        Assert.Empty(_store.GetOutputsByKeys([Key(2)], unspentOnly: false)); // later output gone
        Assert.Null(_store.GetTx(send.Txid));
        Assert.Null(_store.GetBlockHash(101));
        Assert.Equal("hash100", _store.GetBlockHash(100));
    }

    [Fact]
    public void BlockHashes_AreRetainedOnlyForTheRetentionWindow()
    {
        for (var h = 1000; h < 1000 + DdIndexStore.BlockRetention + 5; h++) Apply(h);

        Assert.Null(_store.GetBlockHash(1000));
        Assert.NotNull(_store.GetBlockHash(1000 + DdIndexStore.BlockRetention + 4));
    }

    [Fact]
    public void History_IsNewestFirst_PagesByCursor_AndIncludesBothCreationsAndSpends()
    {
        var fund = Mint(1, Key(1), 2500);
        Apply(100, fund);
        var s1 = DdTxParser.Parse(Transfer([(fund.Txid, 0)], [(Key(2), 1000), (Key(1), 1500)]))!;
        Apply(101, s1);
        var s2 = DdTxParser.Parse(Transfer([(s1.Txid, 1)], [(Key(3), 1500)]))!;
        Apply(102, s2);

        var page1 = _store.GetTxsTouching([Key(1)], count: 2, before: null);
        Assert.Equal([s2.Txid, s1.Txid], page1.Select(t => t.Txid)); // s2 spends Key(1)'s change; s1 created and spent

        var page2 = _store.GetTxsTouching([Key(1)], count: 2, before: page1[^1]);
        Assert.Equal([fund.Txid], page2.Select(t => t.Txid));

        Assert.Empty(_store.GetTxsTouching([Key(9)], 10, null));
    }

    [Fact]
    public void History_OrdersTxsWithinOneBlock_ByPosition()
    {
        var a = Mint(1, Key(1), 100);
        var b = Mint(2, Key(1), 200);
        Apply(100, a, b);

        var rows = _store.GetTxsTouching([Key(1)], 10, null);

        Assert.Equal([b.Txid, a.Txid], rows.Select(t => t.Txid)); // later in the block comes first
    }

    [Fact]
    public void GetOutputsCreatedBy_And_GetOutput_RoundTrip()
    {
        var fund = Mint(1, Key(1), 2500);
        Apply(100, fund);

        Assert.Single(_store.GetOutputsCreatedBy(fund.Txid));
        Assert.Equal(2500, _store.GetOutput(fund.Txid, 0)!.AmountCents);
        Assert.Null(_store.GetOutput(fund.Txid, 7));
        Assert.Equal(100, _store.GetTx(fund.Txid)!.BlockHeight);
        Assert.Equal(10_000_000, _store.GetTx(fund.Txid)!.FeeSats);
    }
}

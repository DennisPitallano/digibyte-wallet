using DigiByte.Crypto.Networks;
using DigiByte.NodeApi.DigiDollar;
using NBitcoin;
using static DigiByte.NodeApi.Tests.DdTestTx;

namespace DigiByte.NodeApi.Tests;

/// <summary>An in-memory chain: blocks by height, a mempool, and call counters.</summary>
internal sealed class FakeChainSource : IDdChainSource
{
    public Dictionary<int, ChainBlock> Blocks { get; } = new();
    public Dictionary<string, string> Mempool { get; } = new(); // txid -> hex
    public Dictionary<string, ChainTxStatus> Statuses { get; } = new();
    public List<int> FetchedBlocks { get; } = [];
    public int RawFetches { get; private set; }
    public int? NetworkHeight { get; set; }

    public int Tip => Blocks.Count == 0 ? -1 : Blocks.Keys.Max();

    public Task<int> GetTipHeightAsync(CancellationToken ct) => Task.FromResult(Tip);
    public Task<int?> GetNetworkHeightAsync(CancellationToken ct) => Task.FromResult(NetworkHeight);
    public Task<string?> GetBlockHashAsync(int height, CancellationToken ct) =>
        Task.FromResult(Blocks.TryGetValue(height, out var b) ? b.Hash : null);
    public Task<ChainBlock> GetBlockAsync(int height, CancellationToken ct)
    {
        FetchedBlocks.Add(height);
        return Task.FromResult(Blocks[height]);
    }
    public Task<IReadOnlyList<string>> GetMempoolTxidsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Mempool.Keys.ToList());
    public Task<string?> GetRawTransactionHexAsync(string txid, CancellationToken ct)
    {
        RawFetches++;
        return Task.FromResult(Mempool.TryGetValue(txid, out var hex) ? hex : null);
    }
    public Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct) =>
        Task.FromResult(Statuses.TryGetValue(txid, out var s) ? s : new ChainTxStatus(ChainTxState.Unknown, 0));

    public static ChainTx ToChainTx(Transaction tx, long? fee = 10_000_000) =>
        new(tx.GetHash().ToString(), tx.Version, tx.ToHex(), fee);

    public void AddBlock(int height, string hash, params Transaction[] txs) =>
        Blocks[height] = new ChainBlock(height, hash, 1_700_000_000 + height, txs.Select(t => ToChainTx(t)).ToList());

    public void AddBlockRaw(int height, string hash, params ChainTx[] txs) =>
        Blocks[height] = new ChainBlock(height, hash, 1_700_000_000 + height, txs);
}

public class DdIndexSyncTests : IDisposable
{
    private readonly DdIndexStore _store = DdIndexStore.InMemory();
    private readonly FakeChainSource _chain = new();
    private readonly Network _network = DigiByteNetwork.Testnet;

    public void Dispose() => _store.Dispose();

    private DdIndexSync Sync(int start = 100) => new(_store, _chain, _network, start);

    private static Transaction Fund(int n, byte[] key, long cents) =>
        Transfer([(Txid(500 + n), 0)], [(key, cents)]);

    private static Transaction Plain() => NewTx(2, (Txid(7), 0));

    [Fact]
    public async Task SyncsFromTheStartHeight_IndexesDdTxs_SkipsOthers()
    {
        var fund = Fund(1, Key(1), 2500);
        _chain.AddBlock(100, "h100", Plain(), fund);
        _chain.AddBlock(101, "h101", Plain());

        var applied = await Sync().SyncAsync(1000, default);

        Assert.Equal(2, applied);
        Assert.Equal(new DdIndexerState(101, "h101"), _store.GetState());
        Assert.Single(_store.GetOutputsByKeys([Key(1)], unspentOnly: true));
        Assert.Null(_store.GetTx(Plain().GetHash().ToString()));
    }

    [Fact]
    public async Task DoesNotFetchBlocksBelowTheStartHeight()
    {
        for (var h = 95; h <= 102; h++) _chain.AddBlock(h, "h" + h);

        await Sync(start: 100).SyncAsync(1000, default);

        Assert.DoesNotContain(_chain.FetchedBlocks, h => h < 100);
        Assert.Equal(new[] { 100, 101, 102 }, _chain.FetchedBlocks);
    }

    [Fact]
    public async Task SecondPass_AppliesOnlyNewBlocks()
    {
        _chain.AddBlock(100, "h100");
        _chain.AddBlock(101, "h101");
        var sync = Sync();
        await sync.SyncAsync(1000, default);
        _chain.FetchedBlocks.Clear();

        _chain.AddBlock(102, "h102");
        var applied = await sync.SyncAsync(1000, default);

        Assert.Equal(1, applied);
        Assert.Equal([102], _chain.FetchedBlocks);
        Assert.Equal(102, _store.GetState().LastHeight);
    }

    [Fact]
    public async Task MaxBlocks_LimitsOnePass()
    {
        for (var h = 100; h <= 110; h++) _chain.AddBlock(h, "h" + h);

        var applied = await Sync().SyncAsync(4, default);

        Assert.Equal(4, applied);
        Assert.Equal(103, _store.GetState().LastHeight);
    }

    [Fact]
    public async Task Reorg_RollsBackToTheForkPoint_AndReapplies()
    {
        var fund = Fund(1, Key(1), 2500);
        var oldSend = Transfer([(fund.GetHash().ToString(), 0)], [(Key(2), 2500)]);
        _chain.AddBlock(100, "h100", fund);
        _chain.AddBlock(101, "h101");
        _chain.AddBlock(102, "old102", oldSend);
        var sync = Sync();
        await sync.SyncAsync(1000, default);
        Assert.Empty(_store.GetOutputsByKeys([Key(1)], unspentOnly: true)); // spent in block 102

        // Chain reorganises from 102: the spend is replaced by a different one.
        var newSend = Transfer([(fund.GetHash().ToString(), 0)], [(Key(3), 2500)]);
        _chain.AddBlock(102, "new102", newSend);
        _chain.AddBlock(103, "new103");
        await sync.SyncAsync(1000, default);

        Assert.Equal(new DdIndexerState(103, "new103"), _store.GetState());
        Assert.Empty(_store.GetOutputsByKeys([Key(2)], unspentOnly: false)); // old branch output is gone
        Assert.Single(_store.GetOutputsByKeys([Key(3)], unspentOnly: true));
        Assert.Equal(newSend.GetHash().ToString(), _store.GetOutputsSpentBy(newSend.GetHash().ToString()).Single().SpentByTxid);
    }

    [Fact]
    public async Task Reorg_BeyondTheRetentionWindow_RewindsToTheStart()
    {
        _chain.AddBlock(100, "a100", Fund(1, Key(1), 100));
        _chain.AddBlock(101, "a101");
        var sync = Sync();
        await sync.SyncAsync(1000, default);

        // Every block hash changes, including the start block.
        _chain.AddBlock(100, "b100", Fund(2, Key(2), 200));
        _chain.AddBlock(101, "b101");
        await sync.SyncAsync(1000, default);

        Assert.Equal(new DdIndexerState(101, "b101"), _store.GetState());
        Assert.Empty(_store.GetOutputsByKeys([Key(1)], unspentOnly: false));
        Assert.Single(_store.GetOutputsByKeys([Key(2)], unspentOnly: false));
    }

    [Fact]
    public async Task MalformedDdVersionTx_IsSkipped_NotFatal()
    {
        var bad = NewTx(0x02000770, (Txid(1), 0)); // DD version but no OP_RETURN / outputs
        var good = Fund(1, Key(1), 100);
        _chain.AddBlock(100, "h100", bad, good);

        await Sync().SyncAsync(1000, default);

        Assert.Single(_store.GetOutputsByKeys([Key(1)], unspentOnly: true));
        Assert.Null(_store.GetTx(bad.GetHash().ToString()));
    }

    [Fact]
    public async Task FeeFromTheBlock_IsStored()
    {
        var fund = Fund(1, Key(1), 100);
        _chain.AddBlockRaw(100, "h100", FakeChainSource.ToChainTx(fund, fee: 12_345));

        await Sync().SyncAsync(1000, default);

        Assert.Equal(12_345, _store.GetTx(fund.GetHash().ToString())!.FeeSats);
    }
}

public class DdMempoolOverlayTests
{
    private readonly FakeChainSource _chain = new();
    private readonly DdMempoolOverlay _overlay = new(DigiByteNetwork.Testnet);

    private void AddToMempool(NBitcoin.Transaction tx) => _chain.Mempool[tx.GetHash().ToString()] = tx.ToHex();

    [Fact]
    public async Task CollectsDdTxs_IgnoresOthers()
    {
        var dd = Transfer([(Txid(1), 0)], [(Key(1), 100)]);
        AddToMempool(dd);
        AddToMempool(NewTx(2, (Txid(9), 0)));

        await _overlay.RefreshAsync(_chain, default);

        Assert.Equal([dd.GetHash().ToString()], _overlay.Transactions.Select(t => t.Txid));
    }

    [Fact]
    public async Task DropsTxsThatLeftTheMempool()
    {
        var dd = Transfer([(Txid(1), 0)], [(Key(1), 100)]);
        AddToMempool(dd);
        await _overlay.RefreshAsync(_chain, default);

        _chain.Mempool.Clear();
        await _overlay.RefreshAsync(_chain, default);

        Assert.Empty(_overlay.Transactions);
    }

    [Fact]
    public async Task DoesNotRefetchKnownTxs_DdOrNot()
    {
        AddToMempool(Transfer([(Txid(1), 0)], [(Key(1), 100)]));
        AddToMempool(NewTx(2, (Txid(9), 0)));
        await _overlay.RefreshAsync(_chain, default);
        var firstPass = _chain.RawFetches;

        await _overlay.RefreshAsync(_chain, default);

        Assert.Equal(2, firstPass);
        Assert.Equal(firstPass, _chain.RawFetches);
    }

    [Fact]
    public async Task TracksSpentOutpoints()
    {
        var dd = Transfer([(Txid(1), 3)], [(Key(1), 100)]);
        AddToMempool(dd);

        await _overlay.RefreshAsync(_chain, default);

        var spent = _overlay.SpentOutpoints();
        Assert.Equal(dd.GetHash().ToString(), spent[new DdOutpointRef(Txid(1), 3)]);
    }
}

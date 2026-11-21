using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The gateway's one-pass wallet history (spec 023, US6, T046): the same answer as asking address by address, each raw transaction
/// parsed once, the same refusal when the record can't answer, and the Fallback handing it out only while the gateway answers history.
/// </summary>
public partial class P2pGatewayBlockchainServiceTests
{
    /// <summary>80 addresses and 200 transactions: payments from strangers, and every fifth one spending the payment before it.</summary>
    private static async Task<(Rig Rig, List<string> Addresses, List<SyncTransaction> Txs)> WalletRecordAsync(bool withUnknownInput = false)
    {
        var addresses = Enumerable.Range(0, 80).Select(_ => new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString()).ToList();
        var rig = await RigAsync(addresses);
        await rig.Service.GetBalanceAsync(addresses[0]); // the first read refreshes the record; the next ones within 10 s don't

        var stranger = new Key().GetAddress(ScriptPubKeyType.Segwit, Net);
        var txs = new List<SyncTransaction>();
        var remembered = new List<SyncOutpoint>();
        Transaction? previous = null;
        for (var i = 0; i < 200; i++)
        {
            var tx = Net.CreateTransaction();
            if (i % 5 == 4 && previous is not null)
            {
                tx.Inputs.Add(new OutPoint(previous.GetHash(), 0));
            }
            else
            {
                var foreign = new OutPoint(RandomUtils.GetUInt256(), 0);
                tx.Inputs.Add(foreign);
                if (!(withUnknownInput && i == 77))
                    remembered.Add(new SyncOutpoint(foreign.Hash.ToString(), 0, 20_000 + i, stranger.ScriptPubKey.ToHex()));
            }
            tx.Outputs.Add(Money.Satoshis(5_000 + i), BitcoinAddress.Create(addresses[i % 80], Net));
            tx.Outputs.Add(Money.Satoshis(100), stranger);
            var height = i % 10 == 0 ? 0 : Cp + 1 + i % 199;
            txs.Add(new SyncTransaction(tx.GetHash().ToString(), height, height == 0 ? "" : rig.Headers.HeaderAt(height)!.Hash.ToString(), tx.ToHex(), SyncOrigin.ExplorerSourced));
            previous = tx;
        }

        var state = await rig.States.GetAsync(WalletId);
        await rig.States.SaveAsync(state! with { Transactions = txs, UnspentOutpoints = [], SpentOutpoints = remembered });
        return (rig, addresses, txs);
    }

    private static string Fingerprint(TransactionInfo t) =>
        $"{t.TxId}|{t.Confirmations}|{t.FeeSatoshis}|{string.Join(",", t.Inputs.Select(i => $"{i.Address}:{i.AmountSatoshis}"))}|" +
        string.Join(",", t.Outputs.Select(o => $"{o.Address}:{o.AmountSatoshis}:{o.Index}"));

    [Fact]
    public async Task OnePass_EqualsTheUnionOfPerAddressAnswers()
    {
        var (rig, addresses, txs) = await WalletRecordAsync();

        var perAddress = new Dictionary<string, TransactionInfo>();
        foreach (var a in addresses)
            foreach (var t in await rig.Service.GetAddressTransactionsAsync(a, 0, int.MaxValue))
                perAddress.TryAdd(t.TxId, t);
        var onePass = await rig.Service.GetWalletTransactionsAsync(addresses);

        Assert.Equal(txs.Count, onePass.Count);
        Assert.Equal(onePass.Count, onePass.Select(t => t.TxId).Distinct().Count());
        Assert.Equal(perAddress.Values.Select(Fingerprint).Order(), onePass.Select(Fingerprint).Order());
        // Same order rule as per address: pending first, then newest block (fewest confirmations) first.
        var order = onePass.Select(t => t.Confirmations == 0 ? -1 : t.Confirmations).ToList();
        Assert.Equal(order.Order().ToList(), order);
        Assert.Equal(txs.Count(t => t.Height == 0), order.TakeWhile(c => c == -1).Count());
    }

    [Fact]
    public async Task OnePass_ParsesEachRawTransactionOnce()
    {
        var (rig, addresses, txs) = await WalletRecordAsync();

        var before = rig.Service.RawParseCount;
        await rig.Service.GetWalletTransactionsAsync(addresses);

        Assert.Equal(txs.Count, rig.Service.RawParseCount - before);
    }

    [Fact]
    public async Task OnePass_AnInputThatCantBeNamed_ThrowsAsPerAddressDoes()
    {
        var (rig, addresses, _) = await WalletRecordAsync(withUnknownInput: true);

        var perAddress = await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Service.GetAddressTransactionsAsync(addresses[0]));
        var onePass = await Assert.ThrowsAsync<GatewaySyncUnavailableException>(() => rig.Service.GetWalletTransactionsAsync(addresses));
        Assert.Equal(perAddress.Reason, onePass.Reason);
    }

    // ── The Fallback hands it out only while the gateway answers history ──

    private sealed class OnePassGateway : IBlockchainService, IWalletHistorySource
    {
        public int OnePassCalls;
        public List<TransactionInfo> History { get; } = [];
        public Task<List<TransactionInfo>> GetWalletTransactionsAsync(IReadOnlyList<string> addresses)
        {
            Interlocked.Increment(ref OnePassCalls);
            return Task.FromResult(History.ToList());
        }
        public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
        public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(4_000L);
        public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
        public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
        public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("tx");
        public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
        public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
        public Task<decimal> GetFeeRateAsync() => Task.FromResult(1m);
        public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
        public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
    }

    private static FallbackBlockchainService Fallback(IBlockchainService explorer, IBlockchainService gateway, FakeNodeState gatewayState, GatewayPosition position)
    {
        var node = new NodeApiBlockchainService(new HttpClient(new FailingHandler()), "http://fake-node:5260", true);
        return new FallbackBlockchainService(node, [explorer], new HttpClient(), isDevelopment: false,
            gatewaySource: gateway, gatewayState: gatewayState, gatewayPosition: position);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    [Theory]
    [InlineData(NodeSourceState.Active, GatewayPosition.First, true)]
    [InlineData(NodeSourceState.Active, GatewayPosition.Backup, false)]
    [InlineData(NodeSourceState.NotReady, GatewayPosition.First, false)]
    public void TryGetWalletHistorySource_OnlyWhileTheGatewayAnswersHistory(NodeSourceState state, GatewayPosition position, bool expected)
    {
        var sut = Fallback(new SummaryExplorer(), new OnePassGateway(), new FakeNodeState { State = state }, position);
        Assert.Equal(expected, sut.TryGetWalletHistorySource() is not null);
    }

    [Fact]
    public async Task TryGetWalletHistorySource_AsBackup_OnceEveryExplorerFailed()
    {
        var explorer = new SummaryExplorer { FailEverything = true };
        var sut = Fallback(explorer, new OnePassGateway(), new FakeNodeState { State = NodeSourceState.Active }, GatewayPosition.Backup);
        Assert.Null(sut.TryGetWalletHistorySource());

        await sut.GetBalanceAsync(["dgb1qa"]);   // the explorer fails and cools down; the gateway answers
        Assert.NotNull(sut.TryGetWalletHistorySource());
    }

    [Fact]
    public void TryGetWalletHistorySource_OnRegtest_IsNull()
    {
        var sut = Fallback(new SummaryExplorer(), new OnePassGateway(), new FakeNodeState { State = NodeSourceState.Active }, GatewayPosition.First);
        sut.SetNetworkMode("regtest");
        Assert.Null(sut.TryGetWalletHistorySource());
    }

    [Fact]
    public async Task GatewayFirst_WithChangeOnlyOn_NoSummaryOrHistoryRequestReachesTheExplorer()
    {
        var env = new HistorySyncEnv();
        var gateway = new OnePassGateway();
        var fallback = Fallback(env.Explorer, gateway, new FakeNodeState { State = NodeSourceState.Active }, GatewayPosition.First);
        var sync = new WalletHistorySync(fallback, env.Store, env.Clock);
        var service = new WalletService(new WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(), fallback,
            new TransactionTracker(env.Storage), historySync: sync, historyOptions: env.Options);
        service.SetNetworkMode("mainnet");
        await service.CreateWalletAsync("Gateway", env.Words, ChangeAddressTests.Pin);
        gateway.History.Add(new TransactionInfo
        {
            TxId = new string('9', 64), Confirmations = 3, Timestamp = DateTime.UtcNow, FeeSatoshis = 0,
            Inputs = [new TxInput { Address = "dgb1qstranger", AmountSatoshis = 4_100 }],
            Outputs = [new TxOutput { Address = env.Receive(0), AmountSatoshis = 4_000, Index = 0 }],
        });

        var balance = await service.GetBalanceAsync();
        var history = await service.GetTransactionHistoryAsync();

        Assert.Equal(4_000, balance.ConfirmedSatoshis);
        Assert.Contains(history, r => r.TxId == new string('9', 64));
        Assert.Equal(1, gateway.OnePassCalls);
        Assert.Equal(0, env.Explorer.SummaryRequests);
        Assert.Equal(0, env.Explorer.PageRequests);
        Assert.Equal(0, env.Explorer.AddressTxRequests);
        Assert.Equal(0, env.Explorer.BalanceRequests);
    }
}

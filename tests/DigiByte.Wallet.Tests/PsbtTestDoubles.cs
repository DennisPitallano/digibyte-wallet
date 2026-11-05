using DigiByte.Crypto.Psbt;
using DigiByte.Crypto.Tests;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>A chain for PSBT tests: transactions and unspent lists are set per test, and lookups can fail on demand.</summary>
internal sealed class FakePsbtBlockchain : IBlockchainService
{
    private readonly Dictionary<string, TransactionInfo> _transactions = [];
    private readonly Dictionary<string, List<UtxoInfo>> _unspent = [];

    /// <summary>Every unspent-output lookup throws, as when no source answers.</summary>
    public bool UnspentLookupsFail { get; set; }

    /// <summary>Every transaction lookup throws.</summary>
    public bool TransactionLookupsFail { get; set; }

    public int TransactionLookups { get; private set; }
    public int UnspentLookups { get; private set; }
    public List<string> UnspentLookupAddresses { get; } = [];

    /// <summary>Records a coin as existing on chain and unspent at its address.</summary>
    public void AddUnspent(FundedCoin coin, Network network, int confirmations = 6)
    {
        var address = coin.ScriptPubKey.GetDestinationAddress(network)!.ToString();
        if (!_unspent.TryGetValue(address, out var list)) _unspent[address] = list = [];
        list.Add(new UtxoInfo
        {
            TxId = coin.Outpoint.Hash.ToString(),
            OutputIndex = coin.Outpoint.N,
            AmountSatoshis = coin.Amount,
            ScriptPubKey = coin.ScriptPubKey.ToHex(),
            Confirmations = confirmations,
        });
        AddTransaction(coin, network);
    }

    /// <summary>Records the coin's funding transaction, so a lookup by transaction finds the coin.</summary>
    public void AddTransaction(FundedCoin coin, Network network)
    {
        var tx = coin.FundingTransaction;
        _transactions[tx.GetHash().ToString()] = new TransactionInfo
        {
            TxId = tx.GetHash().ToString(),
            Confirmations = 6,
            Timestamp = DateTime.UtcNow,
            FeeSatoshis = 0,
            Inputs = [],
            Outputs = tx.Outputs.Select((o, i) => new TxOutput
            {
                Address = o.ScriptPubKey.GetDestinationAddress(network)?.ToString() ?? "",
                AmountSatoshis = o.Value.Satoshi,
                Index = (uint)i,
                ScriptHex = o.ScriptPubKey.ToHex(),
            }).ToList(),
        };
    }

    /// <summary>Removes a coin from the unspent list, as if it was spent.</summary>
    public void Spend(FundedCoin coin, Network network)
    {
        var address = coin.ScriptPubKey.GetDestinationAddress(network)!.ToString();
        if (_unspent.TryGetValue(address, out var list))
            list.RemoveAll(u => u.TxId == coin.Outpoint.Hash.ToString() && u.OutputIndex == coin.Outpoint.N);
    }

    public Task<List<UtxoInfo>> GetVerifiedUtxosAsync(string address)
    {
        UnspentLookups++;
        UnspentLookupAddresses.Add(address);
        if (UnspentLookupsFail) throw new HttpRequestException("no source answered");
        return Task.FromResult(_unspent.TryGetValue(address, out var list) ? list.ToList() : []);
    }

    public Task<TransactionInfo?> GetTransactionAsync(string txId)
    {
        TransactionLookups++;
        if (TransactionLookupsFail) throw new HttpRequestException("no source answered");
        return Task.FromResult(_transactions.TryGetValue(txId, out var tx) ? tx : null);
    }

    public Task<List<UtxoInfo>> GetUtxosAsync(string address) =>
        Task.FromResult(_unspent.TryGetValue(address, out var list) ? list.ToList() : []);

    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) =>
        Task.FromResult(addresses.SelectMany(a => _unspent.TryGetValue(a, out var l) ? l : []).ToList());

    public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => throw new NotSupportedException();
    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
    public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
    public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
}

/// <summary>A key provider holding test keys. Signs through <see cref="PsbtSigner"/>, as the wallet does.</summary>
internal sealed class FakePsbtKeyProvider(Network network, params Key[] keys) : IPsbtKeyProvider
{
    public bool CanSign { get; set; } = true;
    public Network Network => network;
    public int SignCalls { get; private set; }

    public Task<IReadOnlyDictionary<Script, string>> GetOwnedScriptsAsync()
    {
        var owned = new Dictionary<Script, string>();
        foreach (var key in keys)
            foreach (var type in new[] { ScriptPubKeyType.Segwit, ScriptPubKeyType.Legacy })
            {
                var address = key.PubKey.GetAddress(type, network);
                owned[address.ScriptPubKey] = address.ToString();
            }
        return Task.FromResult<IReadOnlyDictionary<Script, string>>(owned);
    }

    public PSBT SignInputs(PSBT psbt, IReadOnlyList<int> inputIndexes)
    {
        if (!CanSign) throw new InvalidOperationException("This wallet can't sign.");
        SignCalls++;
        var byIndex = new Dictionary<int, Key>();
        foreach (var index in inputIndexes)
        {
            var script = psbt.Inputs[index].GetTxOut()?.ScriptPubKey;
            var key = keys.FirstOrDefault(k =>
                script == k.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit) || script == k.PubKey.GetScriptPubKey(ScriptPubKeyType.Legacy));
            if (key is not null) byIndex[index] = key;
        }
        return PsbtSigner.SignInputs(psbt, byIndex);
    }
}

using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

internal sealed class TestMemoryStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _data = new();
    public Task<string?> GetAsync(string key) => Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);
    public Task SetAsync(string key, string value) { _data[key] = value; return Task.CompletedTask; }
    public Task RemoveAsync(string key) { _data.Remove(key); return Task.CompletedTask; }
    public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_data.ContainsKey(key));
    public Task ClearAsync() { _data.Clear(); return Task.CompletedTask; }
    public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
        Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
}

/// <summary>Real AES-256-GCM keyed by SHA-256 of the secret; decrypt returns null on a wrong key or corrupt data.</summary>
internal sealed class TestAesCrypto : ICryptoService
{
    private static byte[] Key(string pin) => SHA256.HashData(Encoding.UTF8.GetBytes(pin));

    public Task<byte[]> EncryptAsync(byte[] plaintext, string pin)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(Key(pin), 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return Task.FromResult(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin)
    {
        try
        {
            if (ciphertext.Length < 28) return Task.FromResult<byte[]?>(null);
            var plain = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(Key(pin), 16);
            aes.Decrypt(ciphertext[..12], ciphertext[28..], ciphertext[12..28], plain);
            return Task.FromResult<byte[]?>(plain);
        }
        catch (CryptographicException)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}

internal sealed class FakeSessionProvider : IDdSessionProvider
{
    public DdWalletSession? Session { get; set; }
    public DdWalletSession? GetDigiDollarSession() => Session;

    public const string Mnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    public static DigiDollarKeyDerivation Derivation(string network = "testnet", string passphrase = "") =>
        new(new Mnemonic(Mnemonic, Wordlist.English), passphrase, network);

    public static FakeSessionProvider Seed(string walletId = "w1", string network = "testnet", string passphrase = "") =>
        new() { Session = new DdWalletSession(walletId, DdWalletKind.Seed, network, Derivation(network, passphrase)) };

    public static FakeSessionProvider Kind(DdWalletKind kind, string walletId = "w1", string network = "testnet") =>
        new() { Session = new DdWalletSession(walletId, kind, network, null) };
}

internal sealed class FakeDdFeeSource : IDdFeeSource
{
    public List<DdFeeUtxo> Utxos { get; } = [];
    public Script ChangeScript { get; } = new Key(Enumerable.Repeat((byte)91, 32).ToArray()).PubKey.WitHash.ScriptPubKey;

    /// <summary>Add a P2WPKH DGB output owned by a deterministic test key.</summary>
    public DdFeeUtxo Add(int n, long sats, int confirmations = 6)
    {
        var key = new Key(Enumerable.Repeat((byte)(n + 1), 32).ToArray());
        var u = new DdFeeUtxo(n.ToString("x").PadLeft(64, 'f'), 0, sats, confirmations, key);
        Utxos.Add(u);
        return u;
    }

    public Task<IReadOnlyList<DdFeeUtxo>> GetFeeUtxosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DdFeeUtxo>>(Utxos.ToList());

    public Task<Script> GetDgbChangeScriptAsync(CancellationToken ct) => Task.FromResult(ChangeScript);
}

/// <summary>An in-memory DigiDollar backend behind <see cref="IDigiDollarNodeApi"/>.</summary>
internal sealed class FakeDdNodeApi : IDigiDollarNodeApi
{
    public NetworkHealth Health { get; set; } = new()
    {
        Available = true, DeploymentActive = true, NodeHeight = 1000, NetworkHeight = 1000,
        OraclePriceMicroUsd = 6310, OracleHeight = 998,
    };

    public bool Down { get; set; }
    public int? FailAfterDataCalls { get; set; }
    public Dictionary<string, List<DigiDollarUtxo>> Utxos { get; } = new();
    public List<DdHistoryEntry> History { get; } = []; // newest first

    public List<int> UtxoBatchSizes { get; } = [];
    public List<int> HistoryBatchSizes { get; } = [];
    public int DataCalls => UtxoBatchSizes.Count + HistoryBatchSizes.Count;

    public List<string> Broadcasts { get; } = [];
    public Dictionary<string, DdTxStatusResult> Statuses { get; } = new();
    public string? BroadcastError { get; set; }
    public bool BroadcastDown { get; set; }
    public bool StatusDown { get; set; }

    public Task<NetworkHealth> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult(Down ? new NetworkHealth { Available = false } : Health);

    private void GuardData()
    {
        if (Down) throw new DdBackendUnavailableException("down");
        if (FailAfterDataCalls is { } n && DataCalls > n) throw new DdBackendUnavailableException("failed mid-way");
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>> GetUtxosAsync(
        IReadOnlyCollection<string> addresses, int minConf, CancellationToken ct)
    {
        UtxoBatchSizes.Add(addresses.Count);
        GuardData();
        var result = new Dictionary<string, IReadOnlyList<DigiDollarUtxo>>();
        foreach (var a in addresses)
            result[a] = Utxos.TryGetValue(a, out var l) ? l.Where(u => u.Confirmations >= minConf).ToList() : [];
        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>>(result);
    }

    public Task<DdHistoryResult> GetHistoryAsync(IReadOnlyCollection<string> addresses, int count, string? before, CancellationToken ct)
    {
        HistoryBatchSizes.Add(addresses.Count);
        GuardData();
        var set = addresses.ToHashSet();
        var matching = History
            .Where(h => h.DdInputs.Any(i => set.Contains(i.Address)) || h.DdOutputs.Any(o => set.Contains(o.Address)))
            .ToList();
        var start = before is null ? 0 : matching.FindIndex(h => h.Txid == before) + 1;
        var page = matching.Skip(start).Take(count).ToList();
        var next = start + page.Count < matching.Count && page.Count > 0 ? page[^1].Txid : null;
        return Task.FromResult(new DdHistoryResult(page, next));
    }

    public Task<DdTxStatusResult> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        if (StatusDown || Down) throw new DdBackendUnavailableException("down");
        return Task.FromResult(Statuses.TryGetValue(txid, out var s) ? s : new DdTxStatusResult(DdTxState.Unknown, 0));
    }

    public Task<string> BroadcastAsync(string rawTxHex, CancellationToken ct)
    {
        if (Down || BroadcastDown) throw new DdBackendUnavailableException("down");
        if (BroadcastError is not null) throw new DdApiException(BroadcastError, 400);
        Broadcasts.Add(rawTxHex);
        return Task.FromResult("broadcast-" + Broadcasts.Count);
    }
}

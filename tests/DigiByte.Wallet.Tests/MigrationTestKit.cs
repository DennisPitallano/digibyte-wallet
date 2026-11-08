using System.Security.Cryptography;
using System.Text;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Crypto double that behaves like the real service for formats: legacy values are Base64 AES-GCM keyed by the
/// credential, and the typed methods produce real <c>dgb2$</c> envelopes (header as authenticated data).
/// Argon2 itself is replaced by SHA-256 so the tests run under xUnit.
/// </summary>
internal sealed class EnvelopeTestCrypto : ICryptoService
{
    public static byte[] Key(string credential) => SHA256.HashData(Encoding.UTF8.GetBytes(credential));

    /// <summary>When set, the typed encrypt fails with this reason.</summary>
    public CryptoFailure? FailEncryptWith { get; set; }

    /// <summary>When true, the next typed encrypt returns an envelope that opens to different plaintext.</summary>
    public bool CorruptNextEncrypt { get; set; }

    /// <summary>Throws from the typed decrypt of a dgb2 envelope (the read-back check) after this many calls (null = never).</summary>
    public int? ThrowOnTypedDecryptAfter { get; set; }

    public int TypedDecryptCalls { get; private set; }
    public int TypedEncryptCalls { get; private set; }

    public Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(LegacySeal(plaintext, pin));

    public Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult(LegacyOpen(ciphertext, pin));

    public static byte[] LegacySeal(byte[] plaintext, string pin)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(Key(pin), 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }

    public static byte[]? LegacyOpen(byte[] ciphertext, string pin)
    {
        try
        {
            if (ciphertext.Length < 28) return null;
            var plain = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(Key(pin), 16);
            aes.Decrypt(ciphertext[..12], ciphertext[28..], ciphertext[12..28], plain);
            return plain;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static string LegacyString(string plaintext, string pin) =>
        Convert.ToBase64String(LegacySeal(Encoding.UTF8.GetBytes(plaintext), pin));

    public Task<CryptoResult<string>> EncryptAsync(byte[] plaintext, string credential, CredentialKind kind)
    {
        TypedEncryptCalls++;
        if (FailEncryptWith is { } failure) return Task.FromResult(CryptoResult<string>.Fail(failure));

        if (CorruptNextEncrypt)
        {
            CorruptNextEncrypt = false;
            plaintext = plaintext.Concat(new byte[] { 0x21 }).ToArray();
        }
        return Task.FromResult(CryptoResult<string>.Ok(SealEnvelope(plaintext, credential, kind)));
    }

    public static string SealEnvelope(byte[] plaintext, string credential, CredentialKind kind)
    {
        var header = EnvelopeCodec.BuildHeader(KdfProfile.Standard, kind);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(Key(credential), 16);
        aes.Encrypt(nonce, plaintext, cipher, tag, header);
        return EnvelopeCodec.Format(KdfProfile.Standard, kind, salt, nonce, cipher.Concat(tag).ToArray());
    }

    public Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential)
    {
        TypedDecryptCalls++;
        if (ThrowOnTypedDecryptAfter is { } n && stored.StartsWith("dgb2$", StringComparison.Ordinal) && TypedDecryptCalls > n)
            throw new InvalidOperationException("injected decrypt failure");

        var parsed = EnvelopeCodec.Parse(stored);
        switch (parsed.Status)
        {
            case EnvelopeStatus.UnsupportedVersion:
                return Task.FromResult(CryptoResult<byte[]>.Fail(CryptoFailure.UnsupportedVersion));
            case EnvelopeStatus.Unreadable:
                return Task.FromResult(CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable));
            case EnvelopeStatus.Legacy:
                {
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(stored); }
                    catch (FormatException) { return Task.FromResult(CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable)); }
                    var plain = LegacyOpen(bytes, credential);
                    return Task.FromResult(plain is null
                        ? CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential)
                        : CryptoResult<byte[]>.Ok(plain));
                }
        }

        var e = parsed.Envelope!;
        try
        {
            var tag = e.CiphertextAndTag[^16..];
            var cipher = e.CiphertextAndTag[..^16];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(Key(credential), 16);
            aes.Decrypt(e.Nonce, cipher, tag, plain, e.Header);
            return Task.FromResult(CryptoResult<byte[]>.Ok(plain));
        }
        catch (CryptographicException)
        {
            return Task.FromResult(CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential));
        }
    }
}

/// <summary>In-memory storage with fault injection and a call log.</summary>
internal sealed class FaultyStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _data = new();

    /// <summary>Throws on a write (Set) to any key containing this text, e.g. a storage-quota error.</summary>
    public string? FailWritesToKeyContaining { get; set; }

    public Exception Failure { get; set; } = new IOException("QuotaExceededError");
    public List<string> Writes { get; } = new();

    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_data);

    public Task<string?> GetAsync(string key) => Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        if (FailWritesToKeyContaining is { } part && key.Contains(part, StringComparison.Ordinal))
            throw Failure;
        Writes.Add(key);
        _data[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key) { _data.Remove(key); return Task.CompletedTask; }
    public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_data.ContainsKey(key));
    public Task ClearAsync() { _data.Clear(); return Task.CompletedTask; }
    public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
        Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
}

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Serialises holders per name, like Web Locks without ifAvailable.</summary>
internal sealed class SemaphoreMigrationLock : IMigrationLock
{
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();
    public int Acquisitions { get; private set; }
    public bool Unavailable { get; set; }
    public bool ThrowOnAcquire { get; set; }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string name)
    {
        if (ThrowOnAcquire) throw new InvalidOperationException("injected lock failure");
        if (Unavailable) return null;
        SemaphoreSlim s;
        lock (_locks) { s = _locks.TryGetValue(name, out var x) ? x : _locks[name] = new SemaphoreSlim(1, 1); }
        await s.WaitAsync();
        Acquisitions++;
        return new Releaser(s);
    }

    private sealed class Releaser(SemaphoreSlim s) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { s.Release(); return ValueTask.CompletedTask; }
    }
}

internal sealed class MigrationStubBlockchain : IBlockchainService
{
    public Task<long> GetBalanceAsync(string address) => Task.FromResult(0L);
    public Task<long> GetBalanceAsync(IEnumerable<string> addresses) => Task.FromResult(0L);
    public Task<List<UtxoInfo>> GetUtxosAsync(string address) => Task.FromResult(new List<UtxoInfo>());
    public Task<List<UtxoInfo>> GetUtxosAsync(IEnumerable<string> addresses) => Task.FromResult(new List<UtxoInfo>());
    public Task<string> BroadcastTransactionAsync(byte[] rawTransaction) => Task.FromResult("faketxid");
    public Task<TransactionInfo?> GetTransactionAsync(string txId) => Task.FromResult<TransactionInfo?>(null);
    public Task<List<TransactionInfo>> GetAddressTransactionsAsync(string address, int skip = 0, int take = 50) => Task.FromResult(new List<TransactionInfo>());
    public Task<decimal> GetFeeRateAsync() => Task.FromResult(100m);
    public Task<decimal> GetDgbPriceAsync(string fiatCurrency = "USD") => Task.FromResult(0.01m);
    public Task<int> GetBlockHeightAsync() => Task.FromResult(1000);
}

/// <summary>Builds a WalletService over fault-injectable storage with a legacy-format wallet already stored.</summary>
internal sealed class MigrationHarness
{
    public const string Pin = "123456";
    public const string Mnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    public const string WalletId = "w1";

    public FaultyStorage Storage { get; } = new();
    public EnvelopeTestCrypto Crypto { get; } = new();
    public ManualClock Clock { get; } = new();
    public SemaphoreMigrationLock Lock { get; } = new();
    public WalletService Service { get; }
    public WalletKeyStore Store { get; }

    public static string LegacyKey(string id = WalletId) => "wallet_seed_" + id;
    public static string V2Key(string id = WalletId) => "wallet_seed_v2_" + id;
    public static string RecordKey(string id = WalletId) => "wallet_migration_" + id;

    public MigrationHarness()
    {
        Store = new WalletKeyStore(Storage);
        Service = new WalletService(Store, Crypto, new MigrationStubBlockchain(), new TransactionTracker(Storage),
            sweepStore: null, time: Clock, migrationLock: Lock);
        Service.SetNetworkMode("mainnet");
    }

    /// <summary>Stores a wallet in the pre-feature format: legacy Base64 under wallet_seed_{id} and plain info.</summary>
    public async Task AddLegacyWalletAsync(string id = WalletId, string walletType = "hd", string? secret = null, string pin = Pin)
    {
        secret ??= Mnemonic;
        await Storage.SetAsync(LegacyKey(id), EnvelopeTestCrypto.LegacyString(secret, pin));
        var info = new WalletInfo { Id = id, Name = "Test " + id, CreatedAt = DateTime.UtcNow, WalletType = walletType };
        await Storage.SetAsync("wallet_info_" + id, System.Text.Json.JsonSerializer.Serialize(info));
        await Storage.SetAsync("active_wallet_id", id);
    }

    public async Task<string?> Raw(string key) => await Storage.GetAsync(key);
}

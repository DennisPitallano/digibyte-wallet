using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T012: the DigiDollar store persists the account, addresses, cache and pending sends as ONE encrypted
/// blob (FR-008, research R11). The only plaintext item is the boolean <c>dd.used</c> marker.
/// </summary>
public class DigiDollarStoreTests
{
    private readonly InMemoryStorage _storage = new();
    private readonly DigiDollarStore _sut;

    private const string Address = "DDsecretaddressxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    private const string Txid = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public DigiDollarStoreTests() => _sut = new DigiDollarStore(_storage, new AesGcmTestCrypto());

    private static DigiDollarState SampleState(string walletId = "w1") => new()
    {
        Account = DigiDollarAccount.Create(walletId, DdNetwork.Testnet, DateTimeOffset.UnixEpoch) with
        {
            NextReceiveIndex = 3,
            NextChangeIndex = 1,
            ScanComplete = true,
            LastScanAt = DateTimeOffset.UnixEpoch.AddDays(1),
        },
        Addresses =
        [
            new DigiDollarAddress
            {
                WalletId = walletId, Address = Address, Chain = DigiDollarChain.Receive, Index = 0,
                Label = "rent", CreatedAt = DateTimeOffset.UnixEpoch,
            },
        ],
        Cache = new DigiDollarCache(
            new DigiDollarBalance(2500, -100, new Dictionary<string, long> { [Address] = 2500 }, 1234),
            [new DigiDollarTransaction(Txid, DdDirection.Received, null, 2500, null, 3, DateTimeOffset.UnixEpoch, DdTxStatus.Confirmed)],
            DateTimeOffset.UnixEpoch),
        PendingSends =
        [
            new PendingDdSend
            {
                Txid = Txid, RawTxHex = "02000770deadbeef", AmountCents = 500, FeeSats = 10_000_000, Recipient = Address,
                SpentOutpoints = [new DdOutpoint(Txid, 1)], SubmittedAt = DateTimeOffset.UnixEpoch,
                FirstMissingAt = DateTimeOffset.UnixEpoch.AddMinutes(5), Rebroadcasted = true,
            },
        ],
    };

    [Fact]
    public async Task SaveThenLoad_RoundTrips_TheState()
    {
        var state = SampleState();
        await _sut.SaveAsync(state, "secret");

        var loaded = await _sut.LoadAsync("w1", "secret");

        Assert.NotNull(loaded);
        Assert.Equal(state.Account, loaded!.Account);
        Assert.Equal(state.Addresses, loaded.Addresses);
        Assert.Equal(state.PendingSends[0].Txid, loaded.PendingSends[0].Txid);
        Assert.Equal(state.PendingSends[0].SpentOutpoints, loaded.PendingSends[0].SpentOutpoints);
        Assert.Equal(state.PendingSends[0].FirstMissingAt, loaded.PendingSends[0].FirstMissingAt);
        Assert.True(loaded.PendingSends[0].Rebroadcasted);
        Assert.Equal(2500, loaded.Cache!.Balance!.ConfirmedCents);
        Assert.Equal(-100, loaded.Cache.Balance.PendingCents);
        Assert.Equal(2500, loaded.Cache.Balance.PerAddress[Address]);
        Assert.Equal(DdDirection.Received, loaded.Cache.RecentHistory[0].Direction);
    }

    [Fact]
    public async Task Load_WithWrongSecret_ReturnsNull()
    {
        await _sut.SaveAsync(SampleState(), "right");
        Assert.Null(await _sut.LoadAsync("w1", "wrong"));
    }

    [Fact]
    public async Task Load_WhenNothingStored_ReturnsNull() =>
        Assert.Null(await _sut.LoadAsync("never-saved", "secret"));

    [Fact]
    public async Task StoredBlob_ContainsNoPlaintext_AddressTxidOrAmountsOrLabel()
    {
        await _sut.SaveAsync(SampleState(), "secret");

        var raw = await _storage.GetAsync("dd.state.w1");
        Assert.NotNull(raw);
        var bytes = Convert.FromBase64String(raw!);
        var asText = Encoding.UTF8.GetString(bytes) + raw;

        foreach (var leak in new[] { Address, Txid, "rent", "02000770deadbeef", "Testnet", "ScanComplete" })
            Assert.DoesNotContain(leak, asText);
    }

    [Fact]
    public async Task Wallets_AreIsolated()
    {
        await _sut.SaveAsync(SampleState("w1"), "s1");
        await _sut.SaveAsync(SampleState("w2") with { Addresses = [] }, "s2");

        Assert.Single((await _sut.LoadAsync("w1", "s1"))!.Addresses);
        Assert.Empty((await _sut.LoadAsync("w2", "s2"))!.Addresses);
        Assert.Null(await _sut.LoadAsync("w1", "s2"));
    }

    [Fact]
    public async Task Load_CorruptBlob_ReturnsNull_NotThrows()
    {
        await _storage.SetAsync("dd.state.w1", "this is not base64!!");
        Assert.Null(await _sut.LoadAsync("w1", "secret"));

        await _storage.SetAsync("dd.state.w1", Convert.ToBase64String("garbage"u8.ToArray()));
        Assert.Null(await _sut.LoadAsync("w1", "secret"));
    }

    [Fact]
    public async Task Delete_RemovesStateAndMarker()
    {
        await _sut.SaveAsync(SampleState(), "secret");
        await _sut.MarkUsedAsync("w1");

        await _sut.DeleteAsync("w1");

        Assert.Null(await _sut.LoadAsync("w1", "secret"));
        Assert.False(await _sut.HasUsedAsync("w1"));
    }

    [Fact]
    public async Task UsedMarker_IsAPlainBoolean_ReadableWithoutASecret_AndHoldsNoDDData()
    {
        Assert.False(await _sut.HasUsedAsync("w1"));

        await _sut.MarkUsedAsync("w1");

        Assert.True(await _sut.HasUsedAsync("w1"));
        var raw = await _storage.GetAsync("dd.used.w1");
        Assert.Equal("1", raw);
        Assert.False(await _sut.HasUsedAsync("w2"));
    }

    [Fact]
    public async Task Save_RejectsEmptySecret()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SaveAsync(SampleState(), ""));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.LoadAsync("w1", ""));
    }

    [Fact]
    public async Task Save_TruncatesHistoryToTheCacheLimit()
    {
        var many = Enumerable.Range(0, 80)
            .Select(i => new DigiDollarTransaction($"t{i}", DdDirection.Sent, null, i, 1, 1, DateTimeOffset.UnixEpoch.AddSeconds(i), DdTxStatus.Confirmed))
            .ToList();
        var state = SampleState() with { Cache = new DigiDollarCache(null, many, DateTimeOffset.UnixEpoch) };

        await _sut.SaveAsync(state, "secret");

        var loaded = await _sut.LoadAsync("w1", "secret");
        Assert.Equal(DigiDollarCache.MaxHistory, loaded!.Cache!.RecentHistory.Count);
        Assert.Equal("t0", loaded.Cache.RecentHistory[0].Txid); // first 50 entries are kept
    }

    // ---------- storage key derivation ----------

    [Fact]
    public void DeriveStoreSecret_IsDeterministic_AndSeedAndWalletSpecific()
    {
        var seedA = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();
        var seedB = Enumerable.Range(2, 64).Select(i => (byte)i).ToArray();

        Assert.Equal(DigiDollarStore.DeriveStoreSecret(seedA), DigiDollarStore.DeriveStoreSecret(seedA));
        Assert.NotEqual(DigiDollarStore.DeriveStoreSecret(seedA), DigiDollarStore.DeriveStoreSecret(seedB));
        Assert.False(string.IsNullOrWhiteSpace(DigiDollarStore.DeriveStoreSecret(seedA)));
    }

    [Fact]
    public void DeriveStoreSecret_DoesNotExposeTheSeed()
    {
        var seed = Enumerable.Range(1, 64).Select(i => (byte)i).ToArray();
        var secret = DigiDollarStore.DeriveStoreSecret(seed);

        Assert.DoesNotContain(Convert.ToBase64String(seed), secret);
        Assert.DoesNotContain(Convert.ToHexString(seed), secret, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeriveStoreSecret_RejectsEmptySeed() =>
        Assert.Throws<ArgumentException>(() => DigiDollarStore.DeriveStoreSecret([]));

    // ---------- test doubles ----------

    private sealed class InMemoryStorage : ISecureStorage
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
    private sealed class AesGcmTestCrypto : ICryptoService
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
                var nonce = ciphertext[..12];
                var tag = ciphertext[12..28];
                var cipher = ciphertext[28..];
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(Key(pin), 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Task.FromResult<byte[]?>(plain);
            }
            catch (CryptographicException)
            {
                return Task.FromResult<byte[]?>(null);
            }
        }
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigiByte.Crypto.Hashing;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Storage;

/// <summary>
/// Persists everything DigiDollar-related for one wallet (account, addresses, cached history, pending sends)
/// as ONE encrypted blob (FR-008, research R11). Addresses, txids and amounts never touch storage in plaintext.
///
/// The wallet does not keep the PIN in memory after unlock, so the blob is encrypted under a "store secret"
/// derived from the unlocked seed (<see cref="DeriveStoreSecret"/>): readable only while the wallet is unlocked
/// with its PIN, and re-encryptable at any time during the session without asking for the PIN again.
///
/// The one exception is a plain boolean marker <c>dd.used.{walletId}</c> (set the first time DigiDollar is
/// opened) that lets the home screen show its neutral "DigiDollar is temporarily unavailable" line while the
/// view flag is off, without decrypting anything (FR-002). It reveals only that DigiDollar was opened once.
/// </summary>
public sealed class DigiDollarStore
{
    private const string StatePrefix = "dd.state.";
    private const string UsedPrefix = "dd.used.";
    private static readonly byte[] StoreKeyInfo = Encoding.UTF8.GetBytes("digibyte-wallet/digidollar/store/v1");

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly ISecureStorage _storage;
    private readonly ICryptoService _crypto;

    public DigiDollarStore(ISecureStorage storage, ICryptoService crypto)
    {
        _storage = storage;
        _crypto = crypto;
    }

    /// <summary>
    /// Derive the store secret from the unlocked seed with HKDF-SHA256. The seed itself is never stored or exposed.
    /// </summary>
    public static string DeriveStoreSecret(byte[] seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (seed.Length == 0) throw new ArgumentException("Seed must not be empty.", nameof(seed));
        return Convert.ToBase64String(Hkdf.DeriveSha256(seed, [], StoreKeyInfo, 32));
    }

    public async Task SaveAsync(DigiDollarState state, string storeSecret)
    {
        ArgumentNullException.ThrowIfNull(state);
        RequireSecret(storeSecret);

        var toSave = state;
        if (state.Cache is { RecentHistory.Count: > DigiDollarCache.MaxHistory } cache)
            toSave = state with { Cache = cache with { RecentHistory = cache.RecentHistory.Take(DigiDollarCache.MaxHistory).ToList() } };

        var plain = JsonSerializer.SerializeToUtf8Bytes(toSave, Json);
        var cipher = await _crypto.EncryptAsync(plain, storeSecret);
        await _storage.SetAsync(StatePrefix + state.Account.WalletId, Convert.ToBase64String(cipher));
    }

    /// <summary>Returns null when nothing is stored, the secret is wrong, or the blob is corrupt.</summary>
    public async Task<DigiDollarState?> LoadAsync(string walletId, string storeSecret)
    {
        RequireSecret(storeSecret);

        var raw = await _storage.GetAsync(StatePrefix + walletId);
        if (string.IsNullOrEmpty(raw)) return null;

        byte[] cipher;
        try { cipher = Convert.FromBase64String(raw); }
        catch (FormatException) { return null; }

        byte[]? plain;
        try { plain = await _crypto.DecryptAsync(cipher, storeSecret); }
        catch { return null; }
        if (plain is null) return null;

        try { return JsonSerializer.Deserialize<DigiDollarState>(plain, Json); }
        catch (JsonException) { return null; }
    }

    public async Task DeleteAsync(string walletId)
    {
        await _storage.RemoveAsync(StatePrefix + walletId);
        await _storage.RemoveAsync(UsedPrefix + walletId);
    }

    public Task MarkUsedAsync(string walletId) => _storage.SetAsync(UsedPrefix + walletId, "1");

    public async Task<bool> HasUsedAsync(string walletId) =>
        await _storage.GetAsync(UsedPrefix + walletId) == "1";

    private static void RequireSecret(string storeSecret)
    {
        if (string.IsNullOrEmpty(storeSecret))
            throw new ArgumentException("A store secret is required.", nameof(storeSecret));
    }
}

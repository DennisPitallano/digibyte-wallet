namespace DigiByte.Wallet.Storage;

/// <summary>
/// Abstraction for encrypted key/value storage.
/// Implemented via IndexedDB + AES-256-GCM in the browser.
/// </summary>
public interface ISecureStorage
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
    Task<bool> ContainsKeyAsync(string key);
    Task ClearAsync();
    Task<List<string>> GetKeysWithPrefixAsync(string prefix);
}

/// <summary>
/// Abstraction for wallet seed/key storage with encryption.
/// </summary>
public interface IKeyStore
{
    Task StoreSeedAsync(string walletId, byte[] encryptedSeed);
    Task<byte[]?> GetSeedAsync(string walletId);
    Task DeleteSeedAsync(string walletId);
    Task<bool> HasSeedAsync(string walletId);
}

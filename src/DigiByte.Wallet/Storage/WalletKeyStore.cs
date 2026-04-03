namespace DigiByte.Wallet.Storage;

/// <summary>
/// Stores and retrieves encrypted wallet seeds via ISecureStorage.
/// </summary>
public class WalletKeyStore : IKeyStore
{
    private readonly ISecureStorage _storage;
    private const string SeedKeyPrefix = "wallet_seed_";
    private const string WalletListKey = "wallet_list";

    public WalletKeyStore(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task StoreSeedAsync(string walletId, byte[] encryptedSeed)
    {
        var base64 = Convert.ToBase64String(encryptedSeed);
        await _storage.SetAsync(SeedKeyPrefix + walletId, base64);
    }

    public async Task<byte[]?> GetSeedAsync(string walletId)
    {
        var base64 = await _storage.GetAsync(SeedKeyPrefix + walletId);
        return base64 != null ? Convert.FromBase64String(base64) : null;
    }

    public async Task DeleteSeedAsync(string walletId)
    {
        await _storage.RemoveAsync(SeedKeyPrefix + walletId);
    }

    public async Task<bool> HasSeedAsync(string walletId)
    {
        return await _storage.ContainsKeyAsync(SeedKeyPrefix + walletId);
    }

    public async Task SaveWalletInfoAsync(string walletId, string serializedInfo)
    {
        await _storage.SetAsync("wallet_info_" + walletId, serializedInfo);
    }

    public async Task<string?> GetWalletInfoAsync(string walletId)
    {
        return await _storage.GetAsync("wallet_info_" + walletId);
    }

    public async Task<string?> GetActiveWalletIdAsync()
    {
        return await _storage.GetAsync("active_wallet_id");
    }

    public async Task SetActiveWalletIdAsync(string walletId)
    {
        await _storage.SetAsync("active_wallet_id", walletId);
    }

    /// <summary>
    /// Delete a wallet completely — seed, info, and active ID.
    /// </summary>
    public async Task DeleteWalletAsync(string walletId)
    {
        await _storage.RemoveAsync(SeedKeyPrefix + walletId);
        await _storage.RemoveAsync("wallet_info_" + walletId);
        await _storage.RemoveAsync("active_wallet_id");
    }

    /// <summary>
    /// Wipe all wallet data from storage.
    /// </summary>
    public async Task ClearAllAsync()
    {
        await _storage.ClearAsync();
    }
}

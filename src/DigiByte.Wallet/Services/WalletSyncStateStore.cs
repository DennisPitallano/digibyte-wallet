using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>The record would break a rule of the data model (for example a gateway-proven entry without its raw transaction).</summary>
public sealed class InvalidSyncStateException(string message) : Exception(message);

/// <summary>
/// Keeps each wallet <see cref="WalletSyncState"/> on the device in the wallet key/value storage, under <c>gwsync:{walletId}</c>, apart
/// from the seed store. A save replaces the whole record in one write, so a refresh that fails part way (and never reaches the save)
/// leaves the height and hash exactly as they were. A record that cannot be read is treated as absent: the wallet then rebuilds it
/// from an explorer-served refresh. Nothing here is ever sent to the gateway, exported or backed up.
/// </summary>
public sealed class WalletSyncStateStore(ISecureStorage storage)
{
    public const string KeyPrefix = "gwsync:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string KeyFor(string walletId) => KeyPrefix + walletId;

    public async Task<WalletSyncState?> GetAsync(string walletId)
    {
        RequireId(walletId);
        var text = await storage.GetAsync(KeyFor(walletId));
        if (text is null)
            return null;
        try
        {
            var state = JsonSerializer.Deserialize<WalletSyncState>(text, Json);
            if (state is null || state.WalletId != walletId)
                return await DiscardAsync(walletId);
            Validate(state);
            return state;
        }
        catch (Exception e) when (e is JsonException or InvalidSyncStateException or NotSupportedException)
        {
            return await DiscardAsync(walletId);
        }
    }

    public async Task SaveAsync(WalletSyncState state)
    {
        RequireId(state.WalletId);
        Validate(state);
        await storage.SetAsync(KeyFor(state.WalletId), JsonSerializer.Serialize(state, Json));
    }

    public Task DeleteAsync(string walletId)
    {
        RequireId(walletId);
        return storage.RemoveAsync(KeyFor(walletId));
    }

    /// <summary>Removes the state of every wallet (the delete-all and clear-data paths).</summary>
    public async Task DeleteAllAsync()
    {
        foreach (var key in await storage.GetKeysWithPrefixAsync(KeyPrefix))
            await storage.RemoveAsync(key);
    }

    private async Task<WalletSyncState?> DiscardAsync(string walletId)
    {
        await storage.RemoveAsync(KeyFor(walletId));
        return null;
    }

    private static void RequireId(string walletId)
    {
        if (string.IsNullOrWhiteSpace(walletId))
            throw new ArgumentException("A wallet id is needed.", nameof(walletId));
    }

    /// <summary>The data-model rules: heights are not negative, hashes are 64 hex characters, gateway-proven entries keep their raw transaction.</summary>
    public static void Validate(WalletSyncState state)
    {
        if (state.LastSyncedHeight < 0)
            throw new InvalidSyncStateException("The last synced height cannot be negative.");
        if (!IsHash(state.LastSyncedHash))
            throw new InvalidSyncStateException("The last synced block hash is not a hash.");
        foreach (var tx in state.Transactions)
        {
            if (!IsHash(tx.Txid))
                throw new InvalidSyncStateException("A transaction id is not a hash.");
            if (tx.Height < 0)
                throw new InvalidSyncStateException("A transaction height cannot be negative.");
            // A proven transaction names its block. An explorer-sourced one may not, when its block is older than the wallet headers reach.
            if (tx.Height > 0 && tx.Origin == SyncOrigin.GatewayProven && !IsHash(tx.BlockHash))
                throw new InvalidSyncStateException("A proven confirmed transaction needs its block hash.");
            if (tx.Origin == SyncOrigin.GatewayProven && string.IsNullOrEmpty(tx.Raw))
                throw new InvalidSyncStateException("A gateway-proven transaction must keep its raw bytes.");
        }
        foreach (var output in state.UnspentOutpoints)
            if (!IsHash(output.Txid) || output.Vout < 0 || output.Amount < 0)
                throw new InvalidSyncStateException("An unspent outpoint is not valid.");
    }

    private static bool IsHash(string? text) => text is { Length: 64 } && text.All(Uri.IsHexDigit);
}

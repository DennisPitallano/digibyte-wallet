using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Keeps a restore scan progress on the device under <c>gwscan:{walletId}</c> (spec FR-008, T094). <see cref="AdvanceAsync"/> is the only way
/// to move the resume point, and it moves it only forward, so a chunk that was not finished is read again after an interruption. Nothing
/// here is ever sent to the gateway.
/// </summary>
public sealed class ScanProgressStore(ISecureStorage storage)
{
    public const string KeyPrefix = "gwscan:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string KeyFor(string walletId) => KeyPrefix + walletId;

    public async Task<ScanProgress?> GetAsync(string walletId)
    {
        var text = await storage.GetAsync(KeyFor(walletId));
        if (text is null)
            return null;
        try
        {
            var progress = JsonSerializer.Deserialize<ScanProgress>(text, Json);
            if (progress is null || progress.WalletId != walletId || progress.StartHeight < 0 || progress.NextHeight < progress.StartHeight)
                return await DiscardAsync(walletId);
            return progress;
        }
        catch (JsonException)
        {
            return await DiscardAsync(walletId);
        }
    }

    /// <summary>Starts a scan, or returns the one in progress so it can be resumed.</summary>
    public async Task<ScanProgress> StartOrResumeAsync(string walletId, int startHeight, ScanMode mode)
    {
        if (await GetAsync(walletId) is { } existing && existing.Mode == mode)
            return existing;
        var fresh = new ScanProgress(walletId, startHeight, startHeight, -1, -1, mode, ScanSource.Gateway, []);
        await SaveAsync(fresh);
        return fresh;
    }

    /// <summary>
    /// Records that every block below <paramref name="nextHeight"/> has been processed, with what was found. Refused when it would not move
    /// the resume point forward.
    /// </summary>
    public async Task<ScanProgress> AdvanceAsync(string walletId, int nextHeight, int receiveHighestUsed, int changeHighestUsed, IReadOnlyList<SyncTransaction> transactions)
    {
        var current = await GetAsync(walletId) ?? throw new InvalidOperationException("No scan is in progress for this wallet.");
        if (nextHeight <= current.NextHeight)
            throw new InvalidOperationException("The resume point only moves forward.");
        var next = current with
        {
            NextHeight = nextHeight,
            ReceiveHighestUsed = Math.Max(current.ReceiveHighestUsed, receiveHighestUsed),
            ChangeHighestUsed = Math.Max(current.ChangeHighestUsed, changeHighestUsed),
            Transactions = transactions,
        };
        await SaveAsync(next);
        return next;
    }

    /// <summary>Records a change of source (the scan is carrying on through the explorer path).</summary>
    public async Task SetSourceAsync(string walletId, ScanSource source)
    {
        if (await GetAsync(walletId) is { } current)
            await SaveAsync(current with { Source = source });
    }

    public Task DeleteAsync(string walletId) => storage.RemoveAsync(KeyFor(walletId));

    private Task SaveAsync(ScanProgress progress) => storage.SetAsync(KeyFor(progress.WalletId), JsonSerializer.Serialize(progress, Json));

    private async Task<ScanProgress?> DiscardAsync(string walletId)
    {
        await storage.RemoveAsync(KeyFor(walletId));
        return null;
    }
}

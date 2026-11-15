using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Keeps each wallet birth height (spec FR-008a, T093): recorded from the wallet own verified chain when a wallet is created, or estimated
/// from a date the user gives on restore, or unknown. The estimate reads the timestamps of the headers the wallet verified itself and
/// leans early (a day before the date), because a restore that starts a little too soon only reads some extra blocks, while one that starts
/// too late would miss funds. Stored on the device under <c>gwbirth:{walletId}</c> and removed with the wallet.
/// </summary>
public sealed class BirthHeightService(ISecureStorage storage, GatewayHeaderStore headers)
{
    public const string KeyPrefix = "gwbirth:";

    /// <summary>DigiByte makes a block about every 15 seconds.</summary>
    public const int SecondsPerBlock = 15;

    /// <summary>The estimate starts this many blocks (about a day) before the date, to cover a date that is only approximate.</summary>
    public const int EarlyMarginBlocks = 5760;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string KeyFor(string walletId) => KeyPrefix + walletId;

    /// <summary>Records the current verified tip as the birth of a new wallet. Gives the unknown record when the headers are not ready.</summary>
    public async Task<WalletBirthHeight> RecordAtCreationAsync(string walletId)
    {
        await headers.EnsureInitializedAsync();
        // Only a tip beyond the built-in checkpoint is the wallet own verified view; before that it would just be the checkpoint.
        var birth = headers.Count > 0 ? new WalletBirthHeight(headers.TipHeight, BirthOrigin.Recorded) : new WalletBirthHeight(null, BirthOrigin.Unknown);
        await SaveAsync(walletId, birth);
        return birth;
    }

    /// <summary>Stores a birth height worked out from the date of first use the user gave.</summary>
    public async Task<WalletBirthHeight> SetFromDateAsync(string walletId, DateTimeOffset firstUse)
    {
        await headers.EnsureInitializedAsync();
        var birth = new WalletBirthHeight(EstimateHeight(firstUse), BirthOrigin.EstimatedFromDate);
        await SaveAsync(walletId, birth);
        return birth;
    }

    public async Task<WalletBirthHeight> GetAsync(string walletId)
    {
        var text = await storage.GetAsync(KeyFor(walletId));
        if (text is null)
            return new WalletBirthHeight(null, BirthOrigin.Unknown);
        try
        {
            return JsonSerializer.Deserialize<WalletBirthHeight>(text, Json) ?? new WalletBirthHeight(null, BirthOrigin.Unknown);
        }
        catch (JsonException)
        {
            return new WalletBirthHeight(null, BirthOrigin.Unknown);
        }
    }

    public Task DeleteAsync(string walletId) => storage.RemoveAsync(KeyFor(walletId));

    private Task SaveAsync(string walletId, WalletBirthHeight birth) => storage.SetAsync(KeyFor(walletId), JsonSerializer.Serialize(birth, Json));

    /// <summary>
    /// The height at which to start looking for a wallet first used at <paramref name="firstUse"/>: a day before the first verified block at
    /// or after that moment. A date earlier than the headers reach is extrapolated at 15 seconds a block; a date after the tip gives the tip.
    /// </summary>
    public int EstimateHeight(DateTimeOffset firstUse)
    {
        var target = firstUse.ToUnixTimeSeconds();
        var first = headers.FirstHeight;
        var tip = headers.TipHeight;
        var firstTime = headers.HeaderAt(first)!.Time;
        var tipTime = headers.HeaderAt(tip)!.Time;

        int height;
        if (target >= tipTime)
        {
            height = tip;
        }
        else if (target < firstTime)
        {
            height = first - (int)Math.Ceiling((firstTime - target) / (double)SecondsPerBlock);
        }
        else
        {
            // First verified height whose block time is not before the date. Block times are not strictly increasing, so this is an estimate.
            var low = first;
            var high = tip;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (headers.HeaderAt(mid)!.Time >= target)
                    high = mid;
                else
                    low = mid + 1;
            }
            height = low;
        }
        return Math.Max(1, height - EarlyMarginBlocks);
    }
}

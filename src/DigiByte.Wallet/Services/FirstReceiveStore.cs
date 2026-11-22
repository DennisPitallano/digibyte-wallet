using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Keeps each wallet's <see cref="FirstReceiveState"/> and the per-device "first session ended" marker (spec 025,
/// data-model.md). Nothing here is secret. A storage problem never throws to the caller: it reads as "not decided" /
/// "not ended", which only ever delays a celebration or keeps optional items hidden.
/// </summary>
public sealed class FirstReceiveStore(ISecureStorage storage)
{
    public const string FirstSessionEndedKey = "firstSession:ended";

    private const string EmptyValue = "empty";
    private const string HistoryValue = "history";
    private const string CelebratedValue = "celebrated";

    public static string StateKey(string walletId) => $"firstReceive:{walletId}";

    /// <summary>The wallet's stored state. No value, an unknown value or unreadable storage is <see cref="FirstReceiveState.Undecided"/>.</summary>
    public async Task<FirstReceiveState> GetAsync(string walletId)
    {
        try
        {
            return await storage.GetAsync(StateKey(walletId)) switch
            {
                EmptyValue => FirstReceiveState.Empty,
                HistoryValue => FirstReceiveState.History,
                CelebratedValue => FirstReceiveState.Celebrated,
                _ => FirstReceiveState.Undecided,
            };
        }
        catch
        {
            return FirstReceiveState.Undecided;
        }
    }

    /// <summary>
    /// Stores a new state, only along the allowed moves: undecided → empty or history, empty → history or celebrated.
    /// Anything else is ignored, so a wallet never leaves <see cref="FirstReceiveState.History"/> or
    /// <see cref="FirstReceiveState.Celebrated"/> and only a wallet that started empty can be celebrated.
    /// </summary>
    public async Task SetAsync(string walletId, FirstReceiveState state)
    {
        if (state == FirstReceiveState.Undecided) return;

        var current = await GetAsync(walletId);
        var allowed = current switch
        {
            FirstReceiveState.Undecided => state is FirstReceiveState.Empty or FirstReceiveState.History,
            FirstReceiveState.Empty => state is FirstReceiveState.History or FirstReceiveState.Celebrated,
            _ => false,
        };
        if (!allowed) return;

        var value = state switch
        {
            FirstReceiveState.Empty => EmptyValue,
            FirstReceiveState.History => HistoryValue,
            _ => CelebratedValue,
        };
        try { await storage.SetAsync(StateKey(walletId), value); } catch { }
    }

    /// <summary>Removes the wallet's record (when the wallet is deleted). The first-session marker is not touched.</summary>
    public async Task DeleteAsync(string walletId)
    {
        try { await storage.RemoveAsync(StateKey(walletId)); } catch { }
    }

    /// <summary>True once any wallet on this device has had a transaction. Never goes back to false.</summary>
    public async Task<bool> IsFirstSessionEndedAsync()
    {
        try { return await storage.GetAsync(FirstSessionEndedKey) == "1"; }
        catch { return false; }
    }

    public async Task EndFirstSessionAsync()
    {
        try { await storage.SetAsync(FirstSessionEndedKey, "1"); } catch { }
    }
}

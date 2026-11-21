using System.Globalization;
using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Web.Services;

/// <summary>What a wallet page last showed: the balance, history, DGB price and when they were fetched. Any part may be missing.</summary>
public sealed record WalletSnapshot(
    WalletBalance? Balance,
    List<TransactionRecord>? Transactions,
    decimal? Price,
    DateTime? LastFetchUtc)
{
    public bool HasData => Balance is not null || Transactions is { Count: > 0 };
}

/// <summary>
/// The saved figures wallet pages show at once (spec 023, FR-017), under the keys Home has always used:
/// <c>cache:balance:{walletId}</c>, <c>cache:transactions:{walletId}</c>, <c>cache:lastFetch:{walletId}</c> and the shared
/// <c>cache:dgbPrice</c>. Kept per wallet, so a page never shows another wallet's figures. Invalidated after a send (by wallet id)
/// and removed with the wallet. Storage errors are swallowed: a missing snapshot only means the page waits for the network.
/// </summary>
public sealed class WalletSnapshotCache(ISecureStorage storage, MemoryCacheService memory)
{
    public const string PriceKey = "cache:dgbPrice";
    public static string BalanceKey(string walletId) => $"cache:balance:{walletId}";
    public static string TransactionsKey(string walletId) => $"cache:transactions:{walletId}";
    public static string LastFetchKey(string walletId) => $"cache:lastFetch:{walletId}";

    /// <summary>The in-memory copy of the last fetch time that Home's staleness check reads.</summary>
    public static string MemoryLastFetchKey(string walletId) => $"lastFetchTime:{walletId}";

    public async Task<WalletSnapshot> LoadAsync(string walletId)
    {
        if (string.IsNullOrEmpty(walletId)) return new WalletSnapshot(null, null, null, null);

        WalletBalance? balance = null;
        List<TransactionRecord>? transactions = null;
        decimal? price = null;
        DateTime? lastFetch = null;
        try
        {
            if (await storage.GetAsync(BalanceKey(walletId)) is { } balJson)
                balance = JsonSerializer.Deserialize<WalletBalance>(balJson);

            if (await storage.GetAsync(PriceKey) is { } priceText
                && decimal.TryParse(priceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) && p > 0)
                price = p;

            if (await storage.GetAsync(TransactionsKey(walletId)) is { } txJson)
                transactions = JsonSerializer.Deserialize<List<TransactionRecord>>(txJson);

            if (await storage.GetAsync(LastFetchKey(walletId)) is { } ticksText && long.TryParse(ticksText, out var ticks))
            {
                lastFetch = new DateTime(ticks, DateTimeKind.Utc);
                memory.Set(MemoryLastFetchKey(walletId), lastFetch.Value, TimeSpan.FromMinutes(10));
            }
        }
        catch
        {
            // A snapshot that can't be read is the same as none.
        }
        return new WalletSnapshot(balance, transactions, price, lastFetch);
    }

    /// <summary>Saves a complete fetch (Home): balance, history, price when known, and the fetch time.</summary>
    public async Task SaveAsync(string walletId, WalletBalance balance, List<TransactionRecord> transactions, decimal price)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        try
        {
            var now = DateTime.UtcNow;
            await storage.SetAsync(BalanceKey(walletId), JsonSerializer.Serialize(balance));
            await storage.SetAsync(TransactionsKey(walletId), JsonSerializer.Serialize(transactions));
            if (price > 0)
                await storage.SetAsync(PriceKey, price.ToString(CultureInfo.InvariantCulture));
            await storage.SetAsync(LastFetchKey(walletId), now.Ticks.ToString());
            memory.Set(MemoryLastFetchKey(walletId), now, TimeSpan.FromMinutes(10));
        }
        catch
        {
        }
    }

    /// <summary>
    /// Saves only the history (a page that fetched history alone). The fetch time is left alone, so Home still refreshes its
    /// balance when it next opens.
    /// </summary>
    public async Task SaveTransactionsAsync(string walletId, List<TransactionRecord> transactions)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        try { await storage.SetAsync(TransactionsKey(walletId), JsonSerializer.Serialize(transactions)); }
        catch { }
    }

    /// <summary>Saves only the balance. The fetch time is left alone (see <see cref="SaveTransactionsAsync"/>).</summary>
    public async Task SaveBalanceAsync(string walletId, WalletBalance balance)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        try { await storage.SetAsync(BalanceKey(walletId), JsonSerializer.Serialize(balance)); }
        catch { }
    }

    /// <summary>
    /// After a send: the wallet's saved balance, history and fetch time go (the shared price stays), and so do the in-memory copies,
    /// so the next read reflects the send (FR-023).
    /// </summary>
    public async Task InvalidateAsync(string? walletId)
    {
        memory.RemoveByPrefix("balance");
        memory.RemoveByPrefix("transactions");
        memory.RemoveByPrefix("lastFetchTime");
        if (string.IsNullOrEmpty(walletId)) return;
        try
        {
            foreach (var key in WalletService.WalletSnapshotKeys(walletId))
                await storage.RemoveAsync(key);
        }
        catch
        {
            // A stale snapshot only delays the refresh.
        }
    }

    /// <summary>Forgets every wallet's fetch time (after the display currency changes), so each page refetches.</summary>
    public async Task ForgetFetchTimesAsync()
    {
        memory.RemoveByPrefix("lastFetchTime");
        try
        {
            foreach (var key in await storage.GetKeysWithPrefixAsync("cache:lastFetch:"))
                await storage.RemoveAsync(key);
        }
        catch { }
    }

    /// <summary>
    /// "Clear caches" in Settings: every wallet's page snapshot, the price, and the saved chain data change-only sync keeps
    /// (summaries and history). The next refresh reads in full.
    /// </summary>
    public async Task ClearAllAsync()
    {
        memory.Clear();
        try
        {
            foreach (var prefix in new[] { "cache:", "addr_summary_", "chain_history_" })
                foreach (var key in await storage.GetKeysWithPrefixAsync(prefix))
                    await storage.RemoveAsync(key);
        }
        catch { }
    }
}

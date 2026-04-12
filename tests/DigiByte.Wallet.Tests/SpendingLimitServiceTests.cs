using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class SpendingLimitServiceTests
{
    private const string WalletId = "test-wallet-001";

    private static (SpendingLimitService svc, InMemorySecureStorage storage, TransactionTracker tracker) CreateService()
    {
        var storage = new InMemorySecureStorage();
        var tracker = new TransactionTracker(storage);
        tracker.SetActiveWallet(WalletId);
        var svc = new SpendingLimitService(storage, tracker);
        return (svc, storage, tracker);
    }

    #region Settings Persistence

    [Fact]
    public async Task GetSettingsAsync_ReturnsDefaults_WhenNoneStored()
    {
        var (svc, _, _) = CreateService();
        var settings = await svc.GetSettingsAsync(WalletId);

        Assert.False(settings.Enabled);
        Assert.Equal(0m, settings.DailyLimitDgb);
        Assert.Equal(0m, settings.WeeklyLimitDgb);
        Assert.Equal(0m, settings.MonthlyLimitDgb);
        Assert.Equal(80, settings.AlertThresholdPercent);
        Assert.False(settings.HardBlock);
    }

    [Fact]
    public async Task SaveAndGetSettings_Roundtrips()
    {
        var (svc, _, _) = CreateService();
        var saved = new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
            WeeklyLimitDgb = 500,
            MonthlyLimitDgb = 2000,
            AlertThresholdPercent = 70,
            HardBlock = true,
        };

        await svc.SaveSettingsAsync(WalletId, saved);
        var loaded = await svc.GetSettingsAsync(WalletId);

        Assert.True(loaded.Enabled);
        Assert.Equal(100m, loaded.DailyLimitDgb);
        Assert.Equal(500m, loaded.WeeklyLimitDgb);
        Assert.Equal(2000m, loaded.MonthlyLimitDgb);
        Assert.Equal(70, loaded.AlertThresholdPercent);
        Assert.True(loaded.HardBlock);
    }

    [Fact]
    public async Task DeleteSettingsAsync_RemovesSettings()
    {
        var (svc, storage, _) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings { Enabled = true });
        Assert.True(await storage.ContainsKeyAsync("spending_limit_" + WalletId));

        await svc.DeleteSettingsAsync(WalletId);
        Assert.False(await storage.ContainsKeyAsync("spending_limit_" + WalletId));

        var settings = await svc.GetSettingsAsync(WalletId);
        Assert.False(settings.Enabled);
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsDefaults_WhenStoredJsonIsCorrupt()
    {
        var (svc, storage, _) = CreateService();
        await storage.SetAsync("spending_limit_" + WalletId, "NOT VALID JSON!!!");

        var settings = await svc.GetSettingsAsync(WalletId);
        Assert.False(settings.Enabled);
    }

    #endregion

    #region GetSpentInPeriodAsync

    [Fact]
    public async Task GetSpentInPeriodAsync_ReturnsZero_WhenNoTransactions()
    {
        var (svc, _, _) = CreateService();
        var spent = await svc.GetSpentInPeriodAsync(TimeSpan.FromHours(24));
        Assert.Equal(0m, spent);
    }

    [Fact]
    public async Task GetSpentInPeriodAsync_SumsOnlySentTransactions()
    {
        var (svc, _, tracker) = CreateService();

        await tracker.RecordSendAsync("tx1", "addr1", 500_000_000, 10_000); // 5 DGB
        await tracker.RecordSendAsync("tx2", "addr2", 300_000_000, 10_000); // 3 DGB
        await tracker.RecordReceiveAsync("tx3", "addr3", 1_000_000_000);     // 10 DGB received — should be excluded

        var spent = await svc.GetSpentInPeriodAsync(TimeSpan.FromHours(24));
        Assert.Equal(8m, spent); // 5 + 3
    }

    [Fact]
    public async Task GetSpentInPeriodAsync_ExcludesOldTransactions()
    {
        var (svc, storage, _) = CreateService();

        // Manually insert a transaction with an old timestamp
        var oldTx = new TransactionRecord
        {
            TxId = "old-tx",
            Direction = TransactionDirection.Sent,
            AmountSatoshis = 1_000_000_000, // 10 DGB
            FeeSatoshis = 10_000,
            Timestamp = DateTime.UtcNow.AddHours(-25), // older than 24h
            Confirmations = 6,
        };
        var recentTx = new TransactionRecord
        {
            TxId = "recent-tx",
            Direction = TransactionDirection.Sent,
            AmountSatoshis = 200_000_000, // 2 DGB
            FeeSatoshis = 10_000,
            Timestamp = DateTime.UtcNow.AddMinutes(-30),
            Confirmations = 1,
        };
        var json = JsonSerializer.Serialize(new List<TransactionRecord> { recentTx, oldTx });
        await storage.SetAsync("tx_history_" + WalletId, json);

        // Re-create tracker so it reads from fresh storage (no cached data)
        var tracker2 = new TransactionTracker(storage);
        tracker2.SetActiveWallet(WalletId);
        var svc2 = new SpendingLimitService(storage, tracker2);

        var spent = await svc2.GetSpentInPeriodAsync(TimeSpan.FromHours(24));
        Assert.Equal(2m, spent); // only the recent tx
    }

    #endregion

    #region CheckLimitAsync — Disabled Limits

    [Fact]
    public async Task CheckLimitAsync_ReturnsAllowed_WhenLimitsDisabled()
    {
        var (svc, _, tracker) = CreateService();
        await tracker.RecordSendAsync("tx1", "addr1", 99_999_999_999, 10_000); // huge send

        var result = await svc.CheckLimitAsync(WalletId, 1_000_000m);
        Assert.True(result.Allowed);
        Assert.False(result.Warning);
        Assert.Null(result.Message);
    }

    #endregion

    #region CheckLimitAsync — Under Limit

    [Fact]
    public async Task CheckLimitAsync_ReturnsAllowed_WhenUnderLimit()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 1_000_000_000, 10_000); // 10 DGB sent

        var result = await svc.CheckLimitAsync(WalletId, 5); // sending 5 more => 15 total < 100
        Assert.True(result.Allowed);
        Assert.False(result.Warning);
    }

    #endregion

    #region CheckLimitAsync — Threshold Warning

    [Fact]
    public async Task CheckLimitAsync_ReturnsWarning_WhenAtThreshold()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
            AlertThresholdPercent = 80, // warn at 80 DGB
        });

        await tracker.RecordSendAsync("tx1", "addr1", 7_500_000_000, 10_000); // 75 DGB sent

        var result = await svc.CheckLimitAsync(WalletId, 10); // 75 + 10 = 85 >= 80 threshold
        Assert.True(result.Allowed);
        Assert.True(result.Warning);
        Assert.NotNull(result.Message);
        Assert.Contains("daily", result.Message);
    }

    #endregion

    #region CheckLimitAsync — Over Limit (Warn Only)

    [Fact]
    public async Task CheckLimitAsync_ReturnsWarningNotBlocked_WhenOverLimitAndHardBlockOff()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
            HardBlock = false,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 9_500_000_000, 10_000); // 95 DGB

        var result = await svc.CheckLimitAsync(WalletId, 10); // 95 + 10 = 105 > 100
        Assert.True(result.Allowed); // still allowed — warn only
        Assert.True(result.Warning);
        Assert.Contains("exceeds", result.Message!);
    }

    #endregion

    #region CheckLimitAsync — Over Limit (Hard Block)

    [Fact]
    public async Task CheckLimitAsync_Blocks_WhenOverLimitAndHardBlockOn()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
            HardBlock = true,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 9_500_000_000, 10_000); // 95 DGB

        var result = await svc.CheckLimitAsync(WalletId, 10); // 105 > 100
        Assert.False(result.Allowed); // blocked
        Assert.True(result.Warning);
        Assert.Contains("Blocked", result.Message!);
    }

    #endregion

    #region CheckLimitAsync — Multiple Periods (Worst Wins)

    [Fact]
    public async Task CheckLimitAsync_ReturnsWorstResult_AcrossPeriods()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 1000, // large daily — won't trigger
            WeeklyLimitDgb = 50,  // small weekly — will exceed
            HardBlock = true,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 4_500_000_000, 10_000); // 45 DGB

        var result = await svc.CheckLimitAsync(WalletId, 10); // 55 > 50 weekly
        Assert.False(result.Allowed);
        Assert.Contains("weekly", result.Message!);
    }

    [Fact]
    public async Task CheckLimitAsync_BlockedOverridesWarning()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,   // daily: 85% threshold warning at 80 DGB
            WeeklyLimitDgb = 50,   // weekly: will exceed → blocked
            AlertThresholdPercent = 80,
            HardBlock = true,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 4_500_000_000, 10_000); // 45 DGB

        var result = await svc.CheckLimitAsync(WalletId, 10); // daily fine, weekly 55 > 50 blocked
        Assert.False(result.Allowed); // block wins over warning
    }

    #endregion

    #region CheckLimitAsync — Zero Limits Skipped

    [Fact]
    public async Task CheckLimitAsync_SkipsZeroLimits()
    {
        var (svc, _, tracker) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 0,   // no daily - skipped
            WeeklyLimitDgb = 0,  // no weekly - skipped
            MonthlyLimitDgb = 1000,
        });

        await tracker.RecordSendAsync("tx1", "addr1", 500_000_000, 10_000); // 5 DGB

        var result = await svc.CheckLimitAsync(WalletId, 5); // only monthly checked, 10 < 1000
        Assert.True(result.Allowed);
        Assert.False(result.Warning);
    }

    #endregion

    #region CheckLimitAsync — Batch Send (large amount)

    [Fact]
    public async Task CheckLimitAsync_HandlesLargeAdditionalAmount()
    {
        var (svc, _, _) = CreateService();
        await svc.SaveSettingsAsync(WalletId, new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
            HardBlock = true,
        });

        // No prior sends, but batch send of 150 DGB
        var result = await svc.CheckLimitAsync(WalletId, 150);
        Assert.False(result.Allowed);
        Assert.Contains("Blocked", result.Message!);
    }

    #endregion

    #region Per-Wallet Isolation

    [Fact]
    public async Task Settings_AreIsolatedPerWallet()
    {
        var (svc, _, _) = CreateService();

        await svc.SaveSettingsAsync("wallet-A", new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 100,
        });
        await svc.SaveSettingsAsync("wallet-B", new SpendingLimitSettings
        {
            Enabled = true,
            DailyLimitDgb = 500,
        });

        var a = await svc.GetSettingsAsync("wallet-A");
        var b = await svc.GetSettingsAsync("wallet-B");

        Assert.Equal(100m, a.DailyLimitDgb);
        Assert.Equal(500m, b.DailyLimitDgb);
    }

    #endregion

    #region Fakes

    private class InMemorySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = new();

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_store.TryGetValue(key, out var val) ? val : null);

        public Task SetAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task ClearAsync() { _store.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
            Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    #endregion
}

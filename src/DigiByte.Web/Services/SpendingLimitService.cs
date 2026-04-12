using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Web.Services;

/// <summary>
/// Per-wallet spending limits stored in IndexedDB.
/// Calculates spent amounts from TransactionTracker history.
/// </summary>
public class SpendingLimitService
{
    private readonly ISecureStorage _storage;
    private readonly TransactionTracker _txTracker;
    private const string SettingsPrefix = "spending_limit_";

    public SpendingLimitService(ISecureStorage storage, TransactionTracker txTracker)
    {
        _storage = storage;
        _txTracker = txTracker;
    }

    public async Task<SpendingLimitSettings> GetSettingsAsync(string walletId)
    {
        var json = await _storage.GetAsync(SettingsPrefix + walletId);
        if (json == null) return new SpendingLimitSettings();
        try { return JsonSerializer.Deserialize<SpendingLimitSettings>(json) ?? new SpendingLimitSettings(); }
        catch { return new SpendingLimitSettings(); }
    }

    public async Task SaveSettingsAsync(string walletId, SpendingLimitSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        await _storage.SetAsync(SettingsPrefix + walletId, json);
    }

    public async Task DeleteSettingsAsync(string walletId)
    {
        await _storage.RemoveAsync(SettingsPrefix + walletId);
    }

    /// <summary>
    /// Calculate total DGB sent in a rolling time window.
    /// </summary>
    public async Task<decimal> GetSpentInPeriodAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        var txs = await _txTracker.GetAllAsync();
        return txs
            .Where(t => t.Direction == TransactionDirection.Sent && t.Timestamp >= cutoff)
            .Sum(t => t.AmountDgb);
    }

    /// <summary>
    /// Check all enabled limits against current spending + a proposed additional amount.
    /// Returns the most restrictive (worst) result.
    /// </summary>
    public async Task<SpendingCheckResult> CheckLimitAsync(string walletId, decimal additionalAmountDgb)
    {
        var settings = await GetSettingsAsync(walletId);
        if (!settings.Enabled)
            return new SpendingCheckResult();

        SpendingCheckResult? worst = null;

        if (settings.DailyLimitDgb > 0)
        {
            var result = await CheckPeriod(settings, TimeSpan.FromHours(24), settings.DailyLimitDgb, "daily", additionalAmountDgb);
            worst = PickWorst(worst, result);
        }

        if (settings.WeeklyLimitDgb > 0)
        {
            var result = await CheckPeriod(settings, TimeSpan.FromDays(7), settings.WeeklyLimitDgb, "weekly", additionalAmountDgb);
            worst = PickWorst(worst, result);
        }

        if (settings.MonthlyLimitDgb > 0)
        {
            var result = await CheckPeriod(settings, TimeSpan.FromDays(30), settings.MonthlyLimitDgb, "monthly", additionalAmountDgb);
            worst = PickWorst(worst, result);
        }

        return worst ?? new SpendingCheckResult();
    }

    private async Task<SpendingCheckResult> CheckPeriod(
        SpendingLimitSettings settings, TimeSpan period, decimal limit, string label, decimal additionalAmount)
    {
        var spent = await GetSpentInPeriodAsync(period);
        var totalAfterSend = spent + additionalAmount;
        var thresholdAmount = limit * settings.AlertThresholdPercent / 100m;

        if (totalAfterSend > limit)
        {
            return new SpendingCheckResult
            {
                Allowed = !settings.HardBlock,
                Warning = true,
                Message = settings.HardBlock
                    ? $"Blocked: this send would exceed your {label} limit ({spent:N4} + {additionalAmount:N4} = {totalAfterSend:N4} / {limit:N4} DGB)."
                    : $"Warning: this send exceeds your {label} limit ({totalAfterSend:N4} / {limit:N4} DGB).",
                SpentInPeriod = spent,
                LimitForPeriod = limit,
                PeriodLabel = label,
            };
        }

        if (totalAfterSend >= thresholdAmount)
        {
            return new SpendingCheckResult
            {
                Allowed = true,
                Warning = true,
                Message = $"You'll reach {totalAfterSend / limit * 100:N0}% of your {label} limit ({totalAfterSend:N4} / {limit:N4} DGB).",
                SpentInPeriod = spent,
                LimitForPeriod = limit,
                PeriodLabel = label,
            };
        }

        return new SpendingCheckResult
        {
            SpentInPeriod = spent,
            LimitForPeriod = limit,
            PeriodLabel = label,
        };
    }

    private static SpendingCheckResult PickWorst(SpendingCheckResult? current, SpendingCheckResult candidate)
    {
        if (current == null) return candidate;
        // Blocked > Warning > Ok
        if (!candidate.Allowed) return candidate;
        if (!current.Allowed) return current;
        if (candidate.Warning && !current.Warning) return candidate;
        return current;
    }
}

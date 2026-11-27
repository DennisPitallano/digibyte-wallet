using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Brings an older SQLite dev database (created with <c>EnsureCreated</c> before AddDdPayouts) up to the payout schema.
/// The new tables' DDL comes from the current model, so it can't drift from the migration. No-op on a fresh database.
/// </summary>
public static class DdPayoutsSqliteShim
{
    private static readonly string[] NewTables =
        ["DdAddresses", "DdLedgerEntries", "DdPayouts", "DdPayoutItems", "PayoutAddressChanges", "StepUpGrants", "OperatorSettings", "MerchantDdSettings"];

    public static async Task ApplyAsync(DigiPayDbContext db)
    {
        var script = db.Database.GenerateCreateScript();
        foreach (var raw in script.Split(';'))
        {
            var statement = raw.Trim();
            if (statement.Length == 0) continue;
            var isTable = NewTables.Any(t => statement.StartsWith($"CREATE TABLE \"{t}\"", StringComparison.Ordinal));
            var isIndex = NewTables.Any(t => statement.Contains($" ON \"{t}\"", StringComparison.Ordinal)) && statement.StartsWith("CREATE ", StringComparison.Ordinal);
            if (!isTable && !isIndex) continue;
            var guarded = statement
                .Replace("CREATE TABLE \"", "CREATE TABLE IF NOT EXISTS \"", StringComparison.Ordinal)
                .Replace("CREATE UNIQUE INDEX \"", "CREATE UNIQUE INDEX IF NOT EXISTS \"", StringComparison.Ordinal)
                .Replace("CREATE INDEX \"", "CREATE INDEX IF NOT EXISTS \"", StringComparison.Ordinal);
            try { await db.Database.ExecuteSqlRawAsync(guarded); } catch { /* already there */ }
        }

        var exemptAdded = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutAddressRequiredExempt\" INTEGER NOT NULL DEFAULT 0");
            exemptAdded = true;
        }
        catch { /* already there */ }
        foreach (var col in new[]
        {
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutAddress\" VARCHAR(128) NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutAddressSetAt\" TEXT NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutSchedule\" VARCHAR(16) NOT NULL DEFAULT 'confirmed'",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutDailyTime\" TEXT NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutTimeZone\" VARCHAR(64) NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutThresholdCents\" INTEGER NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutsAutoPausedAt\" TEXT NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutConsecutiveFailures\" INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutsPausedByOperatorAt\" TEXT NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutRequestedAt\" TEXT NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutRequestReason\" VARCHAR(16) NULL",
            "ALTER TABLE \"Stores\" ADD COLUMN \"DdPayoutLastDailyRunAt\" TEXT NULL",
            "ALTER TABLE \"WebhookDeliveries\" ADD COLUMN \"PayloadJson\" TEXT NULL",
            "ALTER TABLE \"WebhookDeliveries\" ADD COLUMN \"PayoutId\" VARCHAR(32) NULL",
        })
        {
            try { await db.Database.ExecuteSqlRawAsync(col); } catch { /* already there */ }
        }
        // Stores that already had DD on keep it on without a payout address (FR-031), as the Postgres migration does.
        if (exemptAdded)
            await db.Database.ExecuteSqlRawAsync("UPDATE \"Stores\" SET \"DdPayoutAddressRequiredExempt\" = 1 WHERE \"DigiDollarEnabled\" = 1");
    }
}

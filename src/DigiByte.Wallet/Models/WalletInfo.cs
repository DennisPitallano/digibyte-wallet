namespace DigiByte.Wallet.Models;

public class WalletInfo
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedAt { get; init; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";
    public int NextReceivingIndex { get; set; }
    public int NextChangeIndex { get; set; }

    /// <summary>
    /// "hd" for BIP39/BIP84 mnemonic wallets, "privatekey" for single WIF key import, "xpub" for watch-only.
    /// </summary>
    public string WalletType { get; set; } = "hd";

    /// <summary>
    /// For privatekey wallets: the network detected from the WIF prefix ("mainnet", "testnet", "regtest").
    /// </summary>
    public string? WifNetwork { get; set; }

    /// <summary>
    /// Display color for the wallet icon/badge. Hex color code (e.g. "#0066FF").
    /// </summary>
    public string Color { get; set; } = "#0066FF";

    /// <summary>
    /// Predefined wallet color palette.
    /// </summary>
    public static readonly string[] ColorPalette =
    [
        "#0066FF", // DigiByte blue
        "#F59E0B", // Amber
        "#10B981", // Emerald
        "#8B5CF6", // Violet
        "#EF4444", // Red
        "#EC4899", // Pink
        "#06B6D4", // Cyan
        "#F97316", // Orange
        "#6366F1", // Indigo
        "#14B8A6", // Teal
    ];
}

public class WalletBalance
{
    public long ConfirmedSatoshis { get; set; }
    public long UnconfirmedSatoshis { get; set; }
    public decimal FiatValue { get; set; }
    public string FiatCurrency { get; set; } = "USD";

    public decimal ConfirmedDgb => ConfirmedSatoshis / 100_000_000m;
    public decimal UnconfirmedDgb => UnconfirmedSatoshis / 100_000_000m;
    public decimal TotalDgb => ConfirmedDgb + UnconfirmedDgb;
}

public class Contact
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>True if this is an auto-generated contact from another wallet in this app.</summary>
    public bool IsWallet { get; init; }

    /// <summary>Wallet color for display (only set when IsWallet = true).</summary>
    public string? WalletColor { get; init; }
}

public class TransactionRecord
{
    public required string TxId { get; init; }
    public required TransactionDirection Direction { get; init; }
    public required long AmountSatoshis { get; init; }
    public required long FeeSatoshis { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int Confirmations { get; set; }
    public string? CounterpartyAddress { get; set; }
    public string? CounterpartyName { get; set; }
    public string? Memo { get; set; }

    public decimal AmountDgb => AmountSatoshis / 100_000_000m;
}

public enum TransactionDirection
{
    Sent,
    Received
}

/// <summary>
/// Per-wallet spending limit configuration.
/// </summary>
public class SpendingLimitSettings
{
    public bool Enabled { get; set; }
    public decimal DailyLimitDgb { get; set; }
    public decimal WeeklyLimitDgb { get; set; }
    public decimal MonthlyLimitDgb { get; set; }

    /// <summary>Alert threshold as a percentage (0–100). Default 80%.</summary>
    public int AlertThresholdPercent { get; set; } = 80;

    /// <summary>If true, sends exceeding the limit are blocked. If false, only a warning is shown.</summary>
    public bool HardBlock { get; set; }
}

/// <summary>
/// Result of checking a spending limit before sending.
/// </summary>
public class SpendingCheckResult
{
    public bool Allowed { get; init; } = true;
    public bool Warning { get; init; }
    public string? Message { get; init; }
    public decimal SpentInPeriod { get; init; }
    public decimal LimitForPeriod { get; init; }
    public string? PeriodLabel { get; init; }
}

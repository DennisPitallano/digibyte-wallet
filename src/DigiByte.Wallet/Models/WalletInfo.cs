namespace DigiByte.Wallet.Models;

public class WalletInfo
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedAt { get; init; }
    public bool IsSimpleMode { get; set; } = true;
    public string FiatCurrency { get; set; } = "USD";

    /// <summary>The receive index of the address the wallet currently shows. It moves only when the user asks for a new address.</summary>
    public int NextReceivingIndex { get; set; }

    public int NextChangeIndex { get; set; }

    /// <summary>One past the highest receive index the chain has shown as used. These stay scanned even if the wallet shows an earlier address.</summary>
    public int UsedReceivingCount { get; set; }

    /// <summary>
    /// "hd" for BIP39/BIP84 mnemonic wallets, "privatekey" for single WIF key import, "xpub" for watch-only.
    /// </summary>
    public string WalletType { get; set; } = "hd";

    /// <summary>"pin" (default for existing wallets) or "passphrase". Chooses which input to show; the envelope header is the authority.</summary>
    public string CredentialType { get; set; } = "pin";

    /// <summary>The credential type as an enum, for choosing the input to show.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DigiByte.Wallet.Storage.CredentialKind Kind =>
        CredentialType == "passphrase" ? DigiByte.Wallet.Storage.CredentialKind.Passphrase : DigiByte.Wallet.Storage.CredentialKind.Pin;

    /// <summary>"standard" (default) or "legacy". "legacy" only when created on a browser that cannot run the memory-hard protection, after the user agreed.</summary>
    public string ProtectionLevel { get; set; } = "standard";

    /// <summary>True once the user dismissed the passphrase upgrade card (SC-010).</summary>
    public bool PassphraseNudgeDismissed { get; set; }


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

    /// <summary>
    /// Surface finish of the wallet card: one of <see cref="TexturePalette"/>. Purely cosmetic. Wallets saved before this existed read as "smooth".
    /// </summary>
    public string Texture { get; set; } = DefaultTexture;

    public const string DefaultTexture = "smooth";

    /// <summary>
    /// The wallet card finishes a user can choose from. The first is the default (no texture).
    /// </summary>
    public static readonly string[] TexturePalette = [DefaultTexture, "leather", "cotton", "plastic", "metal"];

    /// <summary>Returns <paramref name="texture"/> if it is a known finish, otherwise the default. Safe to put in a CSS class name.</summary>
    public static string NormalizeTexture(string? texture) =>
        texture is not null && Array.IndexOf(TexturePalette, texture) >= 0 ? texture : DefaultTexture;
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
    public required long AmountSatoshis { get; set; }
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

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
    /// "hd" for BIP39/BIP44 mnemonic wallets, "privatekey" for single WIF key import.
    /// </summary>
    public string WalletType { get; set; } = "hd";
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

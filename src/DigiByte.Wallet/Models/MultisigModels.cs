using NBitcoin;

namespace DigiByte.Wallet.Models;

/// <summary>
/// Configuration for a multisig wallet. Stored alongside WalletInfo.
/// </summary>
public class MultisigWalletConfig
{
    public required string WalletId { get; init; }

    /// <summary>
    /// Number of required signatures (M in M-of-N).
    /// </summary>
    public required int RequiredSignatures { get; init; }

    /// <summary>
    /// Total number of co-signers (N in M-of-N).
    /// </summary>
    public required int TotalSigners { get; init; }

    /// <summary>
    /// The co-signers (including this wallet's own key).
    /// </summary>
    public required List<CoSigner> CoSigners { get; init; }

    /// <summary>
    /// The hex-encoded redeem script for this multisig wallet.
    /// </summary>
    public required string RedeemScriptHex { get; init; }

    /// <summary>
    /// The multisig address (P2SH-P2WSH or P2WSH).
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// "p2sh-p2wsh" or "p2wsh"
    /// </summary>
    public string AddressType { get; init; } = "p2sh-p2wsh";

    /// <summary>
    /// Index of this wallet's own key in the CoSigners list.
    /// -1 if this device is watch-only (no private key).
    /// </summary>
    public int OwnKeyIndex { get; init; } = -1;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public string Label => $"{RequiredSignatures}-of-{TotalSigners} Multisig";
}

/// <summary>
/// A co-signer in a multisig wallet.
/// </summary>
public class CoSigner
{
    /// <summary>
    /// Human-friendly label (e.g. "My Phone", "Hardware Wallet", "Alice").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Hex-encoded compressed public key.
    /// </summary>
    public required string PublicKeyHex { get; init; }

    /// <summary>
    /// Whether this co-signer's private key is held on this device.
    /// </summary>
    public bool IsLocal { get; init; }
}

/// <summary>
/// A pending multisig transaction awaiting co-signer signatures.
/// </summary>
public class PendingMultisigTransaction
{
    public required string Id { get; init; }
    public required string WalletId { get; init; }

    /// <summary>
    /// Base64-encoded PSBT with current signatures.
    /// </summary>
    public required string PsbtBase64 { get; set; }

    /// <summary>
    /// Human-readable description (e.g. "Send 100 DGB to dgb1...").
    /// </summary>
    public string? Description { get; set; }

    public required string DestinationAddress { get; init; }
    public required long AmountSatoshis { get; init; }
    public required long FeeSatoshis { get; init; }

    /// <summary>
    /// Public key hex values of co-signers who have signed.
    /// </summary>
    public List<string> SignedBy { get; set; } = [];

    /// <summary>
    /// Required number of signatures to finalize.
    /// </summary>
    public required int RequiredSignatures { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public PendingTxStatus Status { get; set; } = PendingTxStatus.AwaitingSignatures;

    /// <summary>
    /// TxId after broadcast, null until finalized and sent.
    /// </summary>
    public string? BroadcastTxId { get; set; }

    public decimal AmountDgb => AmountSatoshis / 100_000_000m;
    public int SignatureCount => SignedBy.Count;
    public bool HasEnoughSignatures => SignatureCount >= RequiredSignatures;
}

public enum PendingTxStatus
{
    AwaitingSignatures,
    ReadyToFinalize,
    Broadcast,
    Failed,
    Cancelled
}

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
    public int OwnKeyIndex { get; set; } = -1;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Wallets on this device that own this multisig (creator/importer, or holders of a co-signer key).
    /// Empty means a legacy multisig no wallet has claimed yet. The multisig list entry is the source of truth.
    /// </summary>
    public List<string> OwnerWalletIds { get; set; } = [];

    /// <summary>User-chosen display name (local only, never part of the script). Null falls back to <see cref="Label"/>.</summary>
    public string? Name { get; set; }

    /// <summary>When the redeem script was last downloaded or copied as a backup. Null until the user backs it up.</summary>
    public DateTime? BackupAt { get; set; }

    public string Label => $"{RequiredSignatures}-of-{TotalSigners} Multisig";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Label : Name!;
}

/// <summary>
/// A multisig wallet setup saved part-way, for when the co-signers' public keys aren't available yet.
/// Holds only public information; nothing here can spend funds.
/// </summary>
public class MultisigDraft
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int RequiredSignatures { get; set; } = 2;
    public int TotalSigners { get; set; } = 3;

    /// <summary>Public key of the wallet that started the draft. Used to warn if it's resumed from a different wallet.</summary>
    public string OwnPublicKeyHex { get; set; } = "";
    public string OwnWalletName { get; set; } = "";

    /// <summary>The app wallet that started this draft. Empty for drafts saved before wallet scoping existed.</summary>
    public string OwnerWalletId { get; set; } = "";

    /// <summary>The other co-signers entered so far. Name and key may be blank or unchecked.</summary>
    public List<DraftCoSigner> CoSigners { get; set; } = [];

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string Label => $"{RequiredSignatures}-of-{TotalSigners}";
}

public class DraftCoSigner
{
    public string Name { get; set; } = "";
    public string PublicKeyHex { get; set; } = "";
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
    public bool IsLocal { get; set; }
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
    /// Id of the unsigned transaction inside the PSBT. Used to check that an imported signature copy belongs to this
    /// proposal. Empty for proposals saved before this field existed; it is filled in when they are loaded.
    /// </summary>
    public string UnsignedTxId { get; set; } = "";

    /// <summary>Optional note the proposer attached.</summary>
    public string? Memo { get; set; }

    /// <summary>Human-readable reason of the last failed broadcast.</summary>
    public string? FailureReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

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

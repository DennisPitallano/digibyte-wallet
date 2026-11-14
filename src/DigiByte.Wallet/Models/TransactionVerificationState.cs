namespace DigiByte.Wallet.Models;

/// <summary>
/// What the wallet can say about a transaction it shows (specs/011-p2p-spv-gateway, data-model: VerifiedTransactionView). Derived from
/// proofs and headers, never stored as a claim by a source. "Checked" and "Verified" differ only in whether the wallet has itself
/// confirmed the proof of work of the headers the proof was checked against.
/// </summary>
public enum TransactionVerificationState
{
    /// <summary>Came from an explorer and no gateway proof exists yet. Counts as today for explorer data.</summary>
    Unchecked,

    /// <summary>Sent by this wallet and not yet in a block.</summary>
    PendingUnconfirmed,

    /// <summary>In a block that is fewer than 6 blocks deep.</summary>
    Pending,

    /// <summary>At least 6 blocks deep, proof checked against headers whose linkage and difficulty the wallet checked.</summary>
    Checked,

    /// <summary>As checked, and the wallet also checked the proof of work of those headers.</summary>
    Verified,

    /// <summary>The proof failed against the wallet own headers. Never shown as fact.</summary>
    Rejected,

    /// <summary>The gateway proof or chain contradicts what the explorer reported. Not spendable until resolved.</summary>
    Disputed,
}

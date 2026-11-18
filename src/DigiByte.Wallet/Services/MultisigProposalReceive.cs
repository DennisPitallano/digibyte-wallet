using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>What happened when a co-signer's copy of a proposal was received (specs/022-multisig-proposal-scan-import).</summary>
public enum ProposalReceiveOutcome
{
    /// <summary>The proposal was not on this device and has been added.</summary>
    Added,

    /// <summary>The proposal was already here; new signatures were merged into it.</summary>
    Merged,

    /// <summary>The proposal was already here and the copy had nothing new.</summary>
    AlreadyUpToDate,

    /// <summary>The proposal was cancelled on this device. Nothing changed; the user may choose to restore it.</summary>
    Cancelled,

    /// <summary>The proposal was already sent to the network. Nothing changed.</summary>
    Closed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Pending">The new, updated or existing local proposal.</param>
/// <param name="NewSignatures">Signatures gained (0 unless Added or Merged).</param>
/// <param name="RejectedSigners">Public keys of co-signers whose signatures failed verification and were discarded.</param>
public sealed record ProposalReceiveResult(
    ProposalReceiveOutcome Outcome,
    PendingMultisigTransaction Pending,
    int NewSignatures,
    IReadOnlyList<string> RejectedSigners);

/// <summary>Another multisig of the same owner that a proposal spends from (for the "open that wallet" offer).</summary>
public sealed record ProposalWalletMatch(string WalletId, string Name);

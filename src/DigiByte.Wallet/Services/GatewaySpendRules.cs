using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>What the wallet knows about one transaction when it judges it.</summary>
/// <param name="Confirmations">Blocks deep in the wallet verified chain, 0 when not in a block.</param>
/// <param name="IsOwnSend">The wallet signed and sent this transaction.</param>
/// <param name="ProofChecked">A gateway proof for it was checked against the wallet own headers.</param>
/// <param name="ExplorerSourced">It came from an explorer and has no checked proof.</param>
/// <param name="Rejected">A proof for it failed against the wallet own headers.</param>
/// <param name="Disputed">The gateway contradicts what the explorer reported about it.</param>
public sealed record TransactionFacts(int Confirmations, bool IsOwnSend, bool ProofChecked, bool ExplorerSourced, bool Rejected = false, bool Disputed = false);

/// <summary>One output of such a transaction.</summary>
/// <param name="Transaction">The transaction it belongs to.</param>
/// <param name="IsChangeToSelf">The output pays change back to the wallet from its own send.</param>
public sealed record OutputFacts(TransactionFacts Transaction, bool IsChangeToSelf);

/// <summary>
/// The pending and spendable rules of the spec (clarifications 2 and 3, FR-013): 6 blocks deep before a payment counts as spendable,
/// except change from a transaction this wallet signed and sent, which is spendable at once while still shown as pending. "Verified"
/// is reachable only when the wallet checks proof of work itself (<see cref="GatewayCapabilities.WalletPowVerified"/>).
/// </summary>
public static class GatewaySpendRules
{
    public const int SpendableConfirmations = 6;

    public static TransactionVerificationState StateOf(TransactionFacts facts, bool walletPowVerified)
    {
        if (facts.Rejected)
            return TransactionVerificationState.Rejected;
        if (facts.Disputed)
            return TransactionVerificationState.Disputed;
        if (facts.ExplorerSourced && !facts.ProofChecked)
            return TransactionVerificationState.Unchecked;
        if (facts.Confirmations <= 0)
            return TransactionVerificationState.PendingUnconfirmed;
        if (facts.Confirmations < SpendableConfirmations)
            return TransactionVerificationState.Pending;
        return walletPowVerified ? TransactionVerificationState.Verified : TransactionVerificationState.Checked;
    }

    /// <summary>The "n of 6" shown for a pending transaction, or null when it is not pending.</summary>
    public static int? PendingConfirmations(TransactionFacts facts, bool walletPowVerified) =>
        StateOf(facts, walletPowVerified) is TransactionVerificationState.Pending ? facts.Confirmations : null;

    /// <summary>Whether an output counts toward the spendable balance and may be chosen as an input.</summary>
    public static bool IsSpendable(OutputFacts output, bool walletPowVerified)
    {
        switch (StateOf(output.Transaction, walletPowVerified))
        {
            case TransactionVerificationState.Rejected:
            case TransactionVerificationState.Disputed:
                return false;
            case TransactionVerificationState.Unchecked:
                // Explorer data counts once the explorer says it is confirmed, as it does today.
                return output.Transaction.Confirmations >= SpendableConfirmations;
            case TransactionVerificationState.Checked:
            case TransactionVerificationState.Verified:
                return true;
            default:
                // Pending: only change from the wallet own send is spendable early.
                return output.IsChangeToSelf && output.Transaction.IsOwnSend;
        }
    }
}

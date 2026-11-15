using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Puts the verification state on history rows from the wallet sync record (spec FR-013, T076). A row the record does not know keeps no
/// state, so a wallet that does not use the gateway sees exactly what it sees today. "Checked" is shown only for a transaction whose
/// proof the wallet checked against its own headers, and it is never described as protection against a dishonest source.
/// </summary>
public static class GatewayVerificationLabeler
{
    public static void Apply(IEnumerable<TransactionRecord> records, WalletSyncState state, bool walletPowVerified)
    {
        var known = state.Transactions.ToDictionary(t => t.Txid, t => t, StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (!known.TryGetValue(record.TxId, out var tx))
                continue;
            record.Verification = GatewaySpendRules.StateOf(
                new TransactionFacts(record.Confirmations, tx.IsOwnSend, ProofChecked: tx.Origin == SyncOrigin.GatewayProven,
                    ExplorerSourced: tx.Origin == SyncOrigin.ExplorerSourced, Rejected: false, Disputed: tx.Disputed),
                walletPowVerified);
        }
    }
}

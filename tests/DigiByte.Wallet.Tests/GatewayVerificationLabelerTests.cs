using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Verification state on history rows comes only from the wallet record, and rows it does not know keep nothing (T076).</summary>
public class GatewayVerificationLabelerTests
{
    private static string Id(char c) => new(c, 64);

    private static TransactionRecord Row(char id, int confirmations) => new()
    {
        TxId = Id(id), Direction = TransactionDirection.Received, AmountSatoshis = 1, FeeSatoshis = 0, Timestamp = DateTime.UtcNow, Confirmations = confirmations,
    };

    private static WalletSyncState State(params SyncTransaction[] transactions) =>
        new("w1", 100, Id('a'), transactions, [], DateTimeOffset.UtcNow);

    [Fact]
    public void A_proven_transaction_six_blocks_deep_is_checked_and_never_verified_while_the_wallet_does_not_check_proof_of_work()
    {
        var rows = new[] { Row('1', 10) };

        GatewayVerificationLabeler.Apply(rows, State(new SyncTransaction(Id('1'), 90, Id('b'), "0100", SyncOrigin.GatewayProven)), GatewayCapabilities.WalletPowVerified);

        Assert.Equal(TransactionVerificationState.Checked, rows[0].Verification);
    }

    [Fact]
    public void A_young_transaction_is_pending_and_an_own_send_not_in_a_block_is_pending_unconfirmed()
    {
        var rows = new[] { Row('1', 2), Row('2', 0) };
        var state = State(
            new SyncTransaction(Id('1'), 99, Id('b'), "0100", SyncOrigin.GatewayProven),
            new SyncTransaction(Id('2'), 0, string.Empty, "0100", SyncOrigin.GatewayProven, IsOwnSend: true));

        GatewayVerificationLabeler.Apply(rows, state, false);

        Assert.Equal(TransactionVerificationState.Pending, rows[0].Verification);
        Assert.Equal(TransactionVerificationState.PendingUnconfirmed, rows[1].Verification);
    }

    [Fact]
    public void An_explorer_sourced_transaction_is_unchecked_and_a_disputed_one_is_disputed()
    {
        var rows = new[] { Row('1', 20), Row('2', 20) };
        var state = State(
            new SyncTransaction(Id('1'), 80, Id('b'), null, SyncOrigin.ExplorerSourced),
            new SyncTransaction(Id('2'), 80, Id('c'), null, SyncOrigin.ExplorerSourced, Disputed: true));

        GatewayVerificationLabeler.Apply(rows, state, false);

        Assert.Equal(TransactionVerificationState.Unchecked, rows[0].Verification);
        Assert.Equal(TransactionVerificationState.Disputed, rows[1].Verification);
    }

    [Fact]
    public void A_row_the_record_does_not_know_keeps_no_state_so_nothing_extra_is_shown()
    {
        var rows = new[] { Row('9', 20) };

        GatewayVerificationLabeler.Apply(rows, State(new SyncTransaction(Id('1'), 80, Id('b'), "0100", SyncOrigin.GatewayProven)), false);

        Assert.Null(rows[0].Verification);
    }

    [Fact]
    public void Matching_ignores_the_case_of_the_transaction_id()
    {
        var upper = Row('a', 20);
        var rows = new[] { new TransactionRecord { TxId = Id('A'), Direction = TransactionDirection.Sent, AmountSatoshis = 1, FeeSatoshis = 0, Timestamp = DateTime.UtcNow, Confirmations = 20 } };

        GatewayVerificationLabeler.Apply(rows, State(new SyncTransaction(Id('a'), 80, Id('b'), "0100", SyncOrigin.GatewayProven)), false);

        Assert.Equal(TransactionVerificationState.Checked, rows[0].Verification);
        Assert.Null(upper.Verification);
    }

    [Fact]
    public void Verified_appears_only_when_the_wallet_does_check_proof_of_work()
    {
        var rows = new[] { Row('1', 10) };

        GatewayVerificationLabeler.Apply(rows, State(new SyncTransaction(Id('1'), 90, Id('b'), "0100", SyncOrigin.GatewayProven)), walletPowVerified: true);

        Assert.Equal(TransactionVerificationState.Verified, rows[0].Verification);
    }
}

using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Pending and spendable rules (T050, spec clarifications 2 and 3).</summary>
public class GatewaySpendRulesTests
{
    private static TransactionFacts Tx(int confirmations, bool own = false, bool proof = true, bool explorer = false, bool rejected = false, bool disputed = false) =>
        new(confirmations, own, proof, explorer, rejected, disputed);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void A_transaction_fewer_than_six_blocks_deep_is_pending_with_its_count(int confirmations)
    {
        var facts = Tx(confirmations);

        Assert.Equal(TransactionVerificationState.Pending, GatewaySpendRules.StateOf(facts, walletPowVerified: false));
        Assert.Equal(confirmations, GatewaySpendRules.PendingConfirmations(facts, false));
    }

    [Fact]
    public void At_six_blocks_it_is_checked_and_spendable_and_no_longer_pending()
    {
        var facts = Tx(6);

        Assert.Equal(TransactionVerificationState.Checked, GatewaySpendRules.StateOf(facts, false));
        Assert.Null(GatewaySpendRules.PendingConfirmations(facts, false));
        Assert.True(GatewaySpendRules.IsSpendable(new OutputFacts(facts, false), false));
    }

    [Fact]
    public void Verified_is_reachable_only_when_the_wallet_checks_proof_of_work_itself()
    {
        Assert.Equal(TransactionVerificationState.Checked, GatewaySpendRules.StateOf(Tx(10), walletPowVerified: false));
        Assert.Equal(TransactionVerificationState.Verified, GatewaySpendRules.StateOf(Tx(10), walletPowVerified: true));
    }

    [Fact]
    public void The_wallet_does_not_check_proof_of_work_yet_so_callers_pass_false()
    {
        Assert.False(GatewayCapabilities.WalletPowVerified);
    }

    [Fact]
    public void Incoming_payments_from_others_do_not_count_until_six_blocks()
    {
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(3), IsChangeToSelf: false), false));
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(5), IsChangeToSelf: false), false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void Change_from_a_transaction_this_wallet_sent_is_spendable_at_once_while_still_pending(int confirmations)
    {
        var facts = Tx(confirmations, own: true);

        Assert.True(GatewaySpendRules.IsSpendable(new OutputFacts(facts, IsChangeToSelf: true), false));
        Assert.True(GatewaySpendRules.StateOf(facts, false) is TransactionVerificationState.Pending or TransactionVerificationState.PendingUnconfirmed);
    }

    [Fact]
    public void The_exception_is_only_for_change_not_for_the_payment_output_of_an_own_send()
    {
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(0, own: true), IsChangeToSelf: false), false));
    }

    [Fact]
    public void A_change_flag_on_a_transaction_the_wallet_did_not_send_does_not_open_the_exception()
    {
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(2, own: false), IsChangeToSelf: true), false));
    }

    [Fact]
    public void An_own_send_not_yet_in_a_block_is_pending_unconfirmed()
    {
        Assert.Equal(TransactionVerificationState.PendingUnconfirmed, GatewaySpendRules.StateOf(Tx(0, own: true), false));
    }

    [Fact]
    public void A_reorganization_that_takes_a_transaction_back_to_fewer_blocks_makes_it_pending_again()
    {
        Assert.Equal(TransactionVerificationState.Checked, GatewaySpendRules.StateOf(Tx(8), false));
        Assert.Equal(TransactionVerificationState.Pending, GatewaySpendRules.StateOf(Tx(2), false));
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(2), false), false));
        Assert.Equal(TransactionVerificationState.PendingUnconfirmed, GatewaySpendRules.StateOf(Tx(0, own: true), false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rejected_and_disputed_transactions_are_never_spendable_even_deep_in_the_chain(bool rejected, bool disputed)
    {
        var facts = Tx(50, rejected: rejected, disputed: disputed);

        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(facts, true), false));
        Assert.Equal(rejected ? TransactionVerificationState.Rejected : TransactionVerificationState.Disputed, GatewaySpendRules.StateOf(facts, false));
    }

    [Fact]
    public void An_explorer_sourced_transaction_is_unchecked_and_counts_as_it_does_today_once_confirmed()
    {
        var facts = Tx(20, proof: false, explorer: true);

        Assert.Equal(TransactionVerificationState.Unchecked, GatewaySpendRules.StateOf(facts, false));
        Assert.True(GatewaySpendRules.IsSpendable(new OutputFacts(facts, false), false));
        Assert.False(GatewaySpendRules.IsSpendable(new OutputFacts(Tx(2, proof: false, explorer: true), false), false));
    }

    [Fact]
    public void Once_a_proof_is_checked_an_explorer_sourced_transaction_is_no_longer_unchecked()
    {
        var facts = Tx(20, proof: true, explorer: true);

        Assert.Equal(TransactionVerificationState.Checked, GatewaySpendRules.StateOf(facts, false));
    }
}

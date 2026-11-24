using System.Text.Json;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Receiving a co-signer's copy of a proposal by scan, paste or file (specs/022-multisig-proposal-scan-import):
/// adding a proposal this device does not hold, merging into one it does, restoring a cancelled one, finding the wallet a
/// proposal belongs to, and spotting proposals whose coins are already spent. Written before the code (Principle III).
/// </summary>
public class MultisigProposalReceiveTests
{
    private const string Owner = "wallet-a";
    private static readonly Network Net = DigiByteNetwork.Mainnet;

    private sealed class Device
    {
        public required MultisigWalletService Sut { get; init; }
        public required MultisigFakeChain Chain { get; init; }
        public required MultisigWalletConfig Config { get; init; }
    }

    private static List<UtxoInfo> Utxos() =>
    [
        new UtxoInfo { TxId = RandomUtils.GetUInt256().ToString(), OutputIndex = 0, AmountSatoshis = 1_000_000_000, ScriptPubKey = "", Confirmations = 10 },
    ];

    private static Key[] Keys() => [new Key(), new Key(), new Key()];
    private static string Destination() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString();

    private static List<CoSigner> CoSigners(Key[] keys, int localIndex) =>
        keys.Select((k, i) => new CoSigner { Name = $"Label{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == localIndex }).ToList();

    /// <summary>A device holding the 2-of-3 multisig of <paramref name="keys"/>, with <paramref name="localIndex"/> as its key (-1 = watch-only).</summary>
    private static async Task<Device> DeviceAsync(Key[] keys, int localIndex, string owner = Owner, List<UtxoInfo>? utxos = null, string name = "Room")
    {
        var chain = new MultisigFakeChain(utxos: utxos ?? Utxos());
        var sut = new MultisigWalletService(new MultisigFakeStorage(), chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync(name, 2, CoSigners(keys, localIndex), ownerWalletId: owner);
        return new Device { Sut = sut, Chain = chain, Config = config };
    }

    private static string SignedCopy(string psbtBase64, Key key)
    {
        var psbt = PSBT.Parse(psbtBase64, Net);
        psbt.SignWithKeys(key);
        return psbt.ToBase64();
    }

    /// <summary>Device A creates a proposal; returns A, a separate device B for the same multisig, and A's copy.</summary>
    private static async Task<(Device A, Device B, Key[] Keys, PendingMultisigTransaction Proposal)> TwoDevicesAsync()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10, memo: "lunch");
        return (a, b, keys, proposal);
    }

    // ─── Model (T004) ───

    [Fact]
    public void A_pending_record_saved_before_the_field_existed_reads_as_not_received()
    {
        const string json = """
            {"Id":"p1","WalletId":"w1","PsbtBase64":"x","DestinationAddress":"d","AmountSatoshis":1,"FeeSatoshis":1,"RequiredSignatures":2}
            """;

        var pending = JsonSerializer.Deserialize<PendingMultisigTransaction>(json)!;

        Assert.False(pending.ReceivedFromCoSigner);
    }

    [Fact]
    public async Task ReceivedFromCoSigner_round_trips_through_storage_and_is_false_for_own_proposals()
    {
        var (a, b, _, proposal) = await TwoDevicesAsync();

        await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.True(Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId)).ReceivedFromCoSigner);
        Assert.False(Assert.Single(await a.Sut.GetPendingTransactionsAsync(a.Config.WalletId)).ReceivedFromCoSigner);
    }

    // ─── US1: add a proposal this device does not hold (T005, T006) ───

    [Fact]
    public async Task Receive_adds_a_proposal_whose_facts_come_from_the_psbt()
    {
        var (_, b, _, proposal) = await TwoDevicesAsync();

        var result = await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.Equal(ProposalReceiveOutcome.Added, result.Outcome);
        var saved = Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
        Assert.Equal(result.Pending.Id, saved.Id);
        Assert.True(saved.ReceivedFromCoSigner);
        Assert.Equal(b.Config.WalletId, saved.WalletId);
        Assert.Equal(proposal.UnsignedTxId, saved.UnsignedTxId);
        Assert.Equal(proposal.DestinationAddress, saved.DestinationAddress);
        Assert.Equal(proposal.AmountSatoshis, saved.AmountSatoshis);
        Assert.Equal(proposal.FeeSatoshis, saved.FeeSatoshis);
        Assert.Equal("lunch", saved.Memo);
        Assert.Equal(PendingTxStatus.AwaitingSignatures, saved.Status);
    }

    [Fact]
    public async Task Receive_keeps_only_co_signer_signatures_that_verify()
    {
        var (_, b, keys, proposal) = await TwoDevicesAsync();

        var result = await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[0]));

        Assert.Equal(ProposalReceiveOutcome.Added, result.Outcome);
        Assert.Equal(1, result.NewSignatures);
        Assert.Equal([keys[0].PubKey.ToHex()], result.Pending.SignedBy);
        Assert.Empty(result.RejectedSigners);
    }

    [Fact]
    public async Task Receive_discards_a_co_signer_signature_that_does_not_verify_and_keeps_the_proposal()
    {
        var (_, b, keys, proposal) = await TwoDevicesAsync();
        var forged = PSBT.Parse(proposal.PsbtBase64, Net);
        forged.Inputs[0].PartialSigs.Add(keys[1].PubKey, new TransactionSignature(keys[1].Sign(uint256.One), SigHash.All));

        var result = await b.Sut.ReceiveProposalAsync(b.Config, forged.ToBase64());

        Assert.Equal(ProposalReceiveOutcome.Added, result.Outcome);
        Assert.Contains(keys[1].PubKey.ToHex(), result.RejectedSigners);
        Assert.Empty(result.Pending.SignedBy);
    }

    [Fact]
    public async Task A_watch_only_copy_can_receive_a_proposal()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var watcher = await DeviceAsync(keys, -1, owner: "wallet-w", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);

        var result = await watcher.Sut.ReceiveProposalAsync(watcher.Config, proposal.PsbtBase64);

        Assert.Equal(ProposalReceiveOutcome.Added, result.Outcome);
        Assert.Equal(-1, watcher.Config.OwnKeyIndex);
    }

    [Fact]
    public async Task Receive_refuses_a_payment_of_another_multisig_and_adds_nothing()
    {
        var (_, b, _, _) = await TwoDevicesAsync();
        var other = await DeviceAsync(Keys(), 0);
        var foreign = await other.Sut.CreateSpendingPSBTAsync(other.Config, Destination(), 1m, 10);

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => b.Sut.ReceiveProposalAsync(b.Config, foreign.PsbtBase64));

        Assert.Contains("not this multisig", ex.Message);
        Assert.Empty(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a psbt")]
    [InlineData("cHNidP8BAAAA-truncated")]
    public async Task Receive_refuses_unreadable_input_and_adds_nothing(string text)
    {
        var (_, b, _, _) = await TwoDevicesAsync();

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => b.Sut.ReceiveProposalAsync(b.Config, text));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.Empty(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Fact]
    public async Task Receive_refuses_a_copy_signed_by_a_key_that_is_not_a_co_signer_in_full()
    {
        var (_, b, _, proposal) = await TwoDevicesAsync();
        var outsider = new Key();
        var copy = PSBT.Parse(proposal.PsbtBase64, Net);
        // NBitcoin will not sign for a key outside the script, so attach the outsider's signature by hand.
        copy.Inputs[0].PartialSigs.Add(outsider.PubKey, new TransactionSignature(outsider.Sign(uint256.One), SigHash.All));

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => b.Sut.ReceiveProposalAsync(b.Config, copy.ToBase64()));

        Assert.Contains("not a co-signer", ex.Message);
        Assert.Empty(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    // ─── US2: a proposal this device already holds (T014, T015) ───

    [Fact]
    public async Task Receiving_the_same_proposal_twice_keeps_one_entry_and_reports_up_to_date()
    {
        var (_, b, _, proposal) = await TwoDevicesAsync();

        var first = await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);
        var second = await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.Equal(ProposalReceiveOutcome.Added, first.Outcome);
        Assert.Equal(ProposalReceiveOutcome.AlreadyUpToDate, second.Outcome);
        Assert.Equal(first.Pending.Id, second.Pending.Id);
        Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Fact]
    public async Task A_copy_with_more_signatures_merges_into_the_existing_proposal()
    {
        var (_, b, keys, proposal) = await TwoDevicesAsync();
        await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        var result = await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[0]));

        Assert.Equal(ProposalReceiveOutcome.Merged, result.Outcome);
        Assert.Equal(1, result.NewSignatures);
        Assert.Equal(1, result.Pending.SignatureCount);
        Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Fact]
    public async Task Merging_up_to_the_threshold_makes_the_proposal_ready_to_finalize()
    {
        var (_, b, keys, proposal) = await TwoDevicesAsync();
        await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[0]));

        var result = await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[2]));

        Assert.Equal(ProposalReceiveOutcome.Merged, result.Outcome);
        Assert.Equal(2, result.Pending.SignatureCount);
        Assert.Equal(PendingTxStatus.ReadyToFinalize, result.Pending.Status);
    }

    [Fact]
    public async Task A_merge_drops_a_co_signer_signature_that_does_not_verify_and_keeps_the_rest()
    {
        var (_, b, keys, proposal) = await TwoDevicesAsync();
        await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);
        var copy = PSBT.Parse(SignedCopy(proposal.PsbtBase64, keys[0]), Net);
        copy.Inputs[0].PartialSigs.Add(keys[1].PubKey, new TransactionSignature(keys[1].Sign(uint256.One), SigHash.All));

        var result = await b.Sut.ReceiveProposalAsync(b.Config, copy.ToBase64());

        Assert.Equal(ProposalReceiveOutcome.Merged, result.Outcome);
        Assert.Equal([keys[0].PubKey.ToHex()], result.Pending.SignedBy);
        Assert.Contains(keys[1].PubKey.ToHex(), result.RejectedSigners);
    }

    [Fact]
    public async Task A_copy_of_a_sent_proposal_is_reported_closed_and_changes_nothing()
    {
        var (a, _, keys, proposal) = await TwoDevicesAsync();
        await a.Sut.MarkBroadcastAsync(proposal, "abc123");

        var result = await a.Sut.ReceiveProposalAsync(a.Config, SignedCopy(proposal.PsbtBase64, keys[1]));

        Assert.Equal(ProposalReceiveOutcome.Closed, result.Outcome);
        Assert.Equal(PendingTxStatus.Broadcast, result.Pending.Status);
        Assert.Empty(result.Pending.SignedBy);
    }

    // ─── US4: cancelled, restore, other wallet, spent coins (T022, T023, T024) ───

    [Fact]
    public async Task Receiving_a_cancelled_proposal_changes_nothing_and_says_so()
    {
        var (a, _, keys, proposal) = await TwoDevicesAsync();
        await a.Sut.CancelPendingAsync(proposal);

        var result = await a.Sut.ReceiveProposalAsync(a.Config, SignedCopy(proposal.PsbtBase64, keys[1]));

        Assert.Equal(ProposalReceiveOutcome.Cancelled, result.Outcome);
        Assert.Equal(PendingTxStatus.Cancelled, result.Pending.Status);
        Assert.Empty(result.Pending.SignedBy);
        Assert.Single(await a.Sut.GetPendingTransactionsAsync(a.Config.WalletId));
    }

    [Fact]
    public async Task Restore_brings_a_cancelled_proposal_back_with_the_copys_valid_signatures()
    {
        var (a, _, keys, proposal) = await TwoDevicesAsync();
        await a.Sut.CancelPendingAsync(proposal);

        var restored = await a.Sut.RestoreCancelledAsync(proposal, SignedCopy(proposal.PsbtBase64, keys[1]));

        Assert.Equal(PendingTxStatus.AwaitingSignatures, restored.Status);
        Assert.Null(restored.CancelledAt);
        Assert.Equal([keys[1].PubKey.ToHex()], restored.SignedBy);
        var saved = Assert.Single(await a.Sut.GetPendingTransactionsAsync(a.Config.WalletId));
        Assert.Equal(PendingTxStatus.AwaitingSignatures, saved.Status);
    }

    [Fact]
    public async Task Restore_returns_to_ready_when_the_signatures_reach_the_threshold()
    {
        var (a, _, keys, proposal) = await TwoDevicesAsync();
        await a.Sut.CancelPendingAsync(proposal);
        var both = PSBT.Parse(SignedCopy(proposal.PsbtBase64, keys[0]), Net);
        both.SignWithKeys(keys[1]);

        var restored = await a.Sut.RestoreCancelledAsync(proposal, both.ToBase64());

        Assert.Equal(PendingTxStatus.ReadyToFinalize, restored.Status);
    }

    [Fact]
    public async Task Restore_refuses_a_proposal_that_is_not_cancelled_and_a_different_transaction()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0, utxos: [.. Utxos(), .. Utxos()]);
        var first = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var second = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 2m, 10);

        await Assert.ThrowsAsync<PsbtImportException>(() => a.Sut.RestoreCancelledAsync(first, first.PsbtBase64));

        await a.Sut.CancelPendingAsync(first);
        await Assert.ThrowsAsync<PsbtImportException>(() => a.Sut.RestoreCancelledAsync(first, second.PsbtBase64));
        Assert.Equal(PendingTxStatus.Cancelled, first.Status);
    }

    [Fact]
    public async Task FindWalletForProposal_returns_the_other_multisig_the_payment_spends_from()
    {
        var keysOne = Keys();
        var keysTwo = Keys();
        var device = await DeviceAsync(keysOne, 0, name: "First");
        var second = await device.Sut.CreateMultisigWalletAsync("Second", 2, CoSigners(keysTwo, 0), ownerWalletId: Owner);
        var maker = await DeviceAsync(keysTwo, 0);
        var proposal = await maker.Sut.CreateSpendingPSBTAsync(maker.Config, Destination(), 1m, 10);

        var match = await device.Sut.FindWalletForProposalAsync(proposal.PsbtBase64, Owner, keysOne[0].PubKey.ToHex(), device.Config.WalletId);

        Assert.NotNull(match);
        Assert.Equal(second.WalletId, match!.WalletId);
        Assert.Equal("Second", match.Name);
    }

    [Fact]
    public async Task FindWalletForProposal_finds_nothing_for_an_unknown_wallet_the_excluded_wallet_or_garbage()
    {
        var keys = Keys();
        var device = await DeviceAsync(keys, 0);
        var mine = await device.Sut.CreateSpendingPSBTAsync(device.Config, Destination(), 1m, 10);
        var stranger = await DeviceAsync(Keys(), 0);
        var theirs = await stranger.Sut.CreateSpendingPSBTAsync(stranger.Config, Destination(), 1m, 10);
        var key = keys[0].PubKey.ToHex();

        Assert.Null(await device.Sut.FindWalletForProposalAsync(theirs.PsbtBase64, Owner, key, device.Config.WalletId));
        Assert.Null(await device.Sut.FindWalletForProposalAsync(mine.PsbtBase64, Owner, key, device.Config.WalletId));
        Assert.Null(await device.Sut.FindWalletForProposalAsync("not a psbt", Owner, key, device.Config.WalletId));
    }

    [Fact]
    public async Task GetSpentInputIds_lists_inputs_that_are_no_longer_unspent()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var inputs = MultisigProposalReader.Read(proposal.PsbtBase64, a.Config, Net).Inputs.Select(i => i.OutPoint).ToList();

        Assert.Empty(await a.Sut.GetSpentInputIdsAsync(a.Config, inputs));

        a.Chain.Utxos = [];
        var spent = await a.Sut.GetSpentInputIdsAsync(a.Config, inputs);

        Assert.Equal(inputs.Select(i => $"{i.Hash}:{i.N}"), spent);
    }

    [Fact]
    public async Task GetSpentInputIds_marks_nothing_when_chain_data_is_unavailable()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var inputs = MultisigProposalReader.Read(proposal.PsbtBase64, a.Config, Net).Inputs.Select(i => i.OutPoint).ToList();
        a.Chain.Utxos = [];
        a.Chain.Offline = true;

        Assert.Empty(await a.Sut.GetSpentInputIdsAsync(a.Config, inputs));
    }

    // ─── Several recipients (specs/029-multisig-multiple-recipients, T017) ───

    private static async Task<(Device A, Device B, Key[] Keys, PendingMultisigTransaction Proposal)> TwoDevicesSeveralRecipientsAsync()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, [(Destination(), 1m), (Destination(), 0.5m), (Destination(), 0.25m)], 10, "payroll");
        return (a, b, keys, proposal);
    }

    private static PendingMultisigTransaction CopyWith(PendingMultisigTransaction p, List<ProposalRecipient> recipients, long? fee = null) => new()
    {
        Id = p.Id,
        WalletId = p.WalletId,
        PsbtBase64 = p.PsbtBase64,
        DestinationAddress = recipients[0].Address,
        AmountSatoshis = recipients.Sum(r => r.AmountSatoshis),
        FeeSatoshis = fee ?? p.FeeSatoshis,
        RequiredSignatures = p.RequiredSignatures,
        UnsignedTxId = p.UnsignedTxId,
        Recipients = recipients,
    };

    [Fact]
    public async Task Receiving_a_payment_to_several_recipients_saves_every_line_from_the_psbt()
    {
        var (_, b, _, proposal) = await TwoDevicesSeveralRecipientsAsync();

        var result = await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.Equal(ProposalReceiveOutcome.Added, result.Outcome);
        var saved = Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
        Assert.Equal(proposal.Recipients, saved.Recipients);
        Assert.Equal(proposal.Recipients[0].Address, saved.DestinationAddress);
        Assert.Equal(175_000_000, saved.AmountSatoshis);
        Assert.Equal("Send 1.75000000 DGB to 3 recipients", saved.Description);
    }

    [Fact]
    public async Task A_saved_copy_whose_recipients_were_reordered_or_changed_is_refused()
    {
        var (a, _, _, proposal) = await TwoDevicesSeveralRecipientsAsync();
        var lines = proposal.Recipients;
        const string reason = "This device's saved recipients, amounts or fee do not match the payment itself.";

        Assert.Null(a.Sut.CheckProposalMatches(proposal, a.Config, proposal.PsbtBase64));
        Assert.Equal(reason, a.Sut.CheckProposalMatches(CopyWith(proposal, [lines[1], lines[0], lines[2]]), a.Config, proposal.PsbtBase64));
        Assert.Equal(reason, a.Sut.CheckProposalMatches(CopyWith(proposal, [lines[0], lines[1], lines[2] with { AmountSatoshis = 26_000_000 }]), a.Config, proposal.PsbtBase64));
        Assert.Equal(reason, a.Sut.CheckProposalMatches(CopyWith(proposal, [lines[0], lines[1]]), a.Config, proposal.PsbtBase64));
        Assert.Equal(reason, a.Sut.CheckProposalMatches(CopyWith(proposal, [.. lines], fee: proposal.FeeSatoshis + 1), a.Config, proposal.PsbtBase64));
    }

    [Fact]
    public async Task A_legacy_single_recipient_record_still_matches_its_psbt()
    {
        var (a, _, _, proposal) = await TwoDevicesAsync();
        var legacy = new PendingMultisigTransaction
        {
            Id = proposal.Id,
            WalletId = proposal.WalletId,
            PsbtBase64 = proposal.PsbtBase64,
            DestinationAddress = proposal.DestinationAddress,
            AmountSatoshis = proposal.AmountSatoshis,
            FeeSatoshis = proposal.FeeSatoshis,
            RequiredSignatures = proposal.RequiredSignatures,
            UnsignedTxId = proposal.UnsignedTxId,
        };

        Assert.Empty(legacy.Recipients);
        Assert.Null(a.Sut.CheckProposalMatches(legacy, a.Config, proposal.PsbtBase64));
    }

    [Fact]
    public async Task Signatures_merge_into_a_payment_to_several_recipients()
    {
        var (_, b, keys, proposal) = await TwoDevicesSeveralRecipientsAsync();
        await b.Sut.ReceiveProposalAsync(b.Config, proposal.PsbtBase64);

        var first = await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[0]));
        var second = await b.Sut.ReceiveProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[2]));

        Assert.Equal(ProposalReceiveOutcome.Merged, first.Outcome);
        Assert.Equal(2, second.Pending.SignatureCount);
        Assert.Equal(PendingTxStatus.ReadyToFinalize, second.Pending.Status);
    }
}

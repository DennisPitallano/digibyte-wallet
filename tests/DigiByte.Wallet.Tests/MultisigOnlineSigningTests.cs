using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Wallet side of online signing rooms (specs/017-multisig-online-signing): finding the local multisig a room is for,
/// adopting a room's payment, merging room signatures (each one verified), and recording a broadcast made by someone else.
/// T018, T030, T034.
/// </summary>
public class MultisigOnlineSigningTests
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

    /// <summary>A device holding the 2-of-3 multisig of <paramref name="keys"/>, with <paramref name="localIndex"/> as its key.</summary>
    private static async Task<Device> DeviceAsync(Key[] keys, int localIndex, string owner = Owner, List<UtxoInfo>? utxos = null, string name = "Room")
    {
        var chain = new MultisigFakeChain(utxos: utxos ?? Utxos());
        var sut = new MultisigWalletService(new MultisigFakeStorage(), chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync(name, 2,
            keys.Select((k, i) => new CoSigner { Name = $"Label{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == localIndex }).ToList(),
            ownerWalletId: owner);
        return new Device { Sut = sut, Chain = chain, Config = config };
    }

    private static Key[] Keys() => [new Key(), new Key(), new Key()];
    private static List<string> Hexes(Key[] keys) => keys.Select(k => k.PubKey.ToHex()).ToList();
    private static string Destination() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString();

    private static string SignedCopy(string psbtBase64, Key key)
    {
        var psbt = PSBT.Parse(psbtBase64, Net);
        psbt.SignWithKeys(key);
        return psbt.ToBase64();
    }

    // ─── Finding the local multisig (T018, T034) ───

    [Fact]
    public async Task FindByRedeemScript_matches_keys_in_any_order()
    {
        var keys = Keys();
        var device = await DeviceAsync(keys, 1);
        var shuffled = Hexes(keys);
        shuffled.Reverse();

        var found = await device.Sut.FindByRedeemScriptAsync(shuffled, 2, Owner, keys[1].PubKey.ToHex());

        Assert.NotNull(found);
        Assert.Equal(device.Config.WalletId, found!.WalletId);
        Assert.Equal(1, found.OwnKeyIndex);
    }

    [Fact]
    public async Task FindByRedeemScript_finds_nothing_for_wrong_threshold_foreign_key_or_other_owner()
    {
        var keys = Keys();
        var device = await DeviceAsync(keys, 0);

        Assert.Null(await device.Sut.FindByRedeemScriptAsync(Hexes(keys), 1, Owner, keys[0].PubKey.ToHex()));
        Assert.Null(await device.Sut.FindByRedeemScriptAsync([.. Hexes(keys)[..2], new Key().PubKey.ToHex()], 2, Owner, keys[0].PubKey.ToHex()));
        Assert.Null(await device.Sut.FindByRedeemScriptAsync(Hexes(keys), 2, "wallet-b", new Key().PubKey.ToHex()));
    }

    [Fact]
    public async Task A_watch_only_copy_is_found_but_has_no_signing_key()
    {
        var keys = Keys();
        var device = await DeviceAsync(keys, -1);

        var found = await device.Sut.FindByRedeemScriptAsync(Hexes(keys), 2, Owner, new Key().PubKey.ToHex());

        Assert.NotNull(found);
        Assert.Equal(-1, found!.OwnKeyIndex);
    }

    [Fact]
    public async Task Owners_of_a_matching_multisig_on_the_device_are_listed_for_the_switch_offer()
    {
        var keys = Keys();
        var device = await DeviceAsync(keys, 2, owner: "wallet-b");

        var owners = await device.Sut.FindOwnerWalletIdsByRedeemScriptAsync(Hexes(keys), 2);

        Assert.Equal(["wallet-b"], owners);
        Assert.Empty(await device.Sut.FindOwnerWalletIdsByRedeemScriptAsync(Hexes(keys), 3));
    }

    // ─── Adopting a room's payment (T018) ───

    [Fact]
    public async Task Adopt_saves_a_proposal_whose_facts_come_from_the_psbt()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var dest = Destination();
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, dest, 1m, 10, memo: "lunch");

        var adopted = await b.Sut.AdoptProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.Equal(proposal.UnsignedTxId, adopted.UnsignedTxId);
        Assert.Equal(dest, adopted.DestinationAddress);
        Assert.Equal(100_000_000, adopted.AmountSatoshis);
        Assert.Equal(proposal.FeeSatoshis, adopted.FeeSatoshis);
        Assert.Equal("lunch", adopted.Memo);
        Assert.Equal(b.Config.WalletId, adopted.WalletId);
        Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Fact]
    public async Task Adopt_twice_returns_the_same_proposal()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);

        var first = await b.Sut.AdoptProposalAsync(b.Config, proposal.PsbtBase64);
        var second = await b.Sut.AdoptProposalAsync(b.Config, proposal.PsbtBase64);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await b.Sut.GetPendingTransactionsAsync(b.Config.WalletId));
    }

    [Fact]
    public async Task Adopt_refuses_a_payment_of_another_multisig()
    {
        var a = await DeviceAsync(Keys(), 0);
        var other = await DeviceAsync(Keys(), 0);
        var proposal = await other.Sut.CreateSpendingPSBTAsync(other.Config, Destination(), 1m, 10);

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => a.Sut.AdoptProposalAsync(a.Config, proposal.PsbtBase64));
        Assert.Contains("not this multisig", ex.Message);
    }

    [Fact]
    public async Task Adopt_refuses_when_the_local_copy_was_cancelled()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        await a.Sut.CancelPendingAsync(proposal);

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => a.Sut.AdoptProposalAsync(a.Config, proposal.PsbtBase64));
        Assert.Contains("cancelled", ex.Message);
    }

    [Fact]
    public async Task Adopt_and_merge_never_change_local_co_signer_labels()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);

        var adopted = await b.Sut.AdoptProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[0]));
        await b.Sut.MergeRoomSignaturesAsync(adopted, SignedCopy(proposal.PsbtBase64, keys[2]));

        var config = await b.Sut.GetConfigAsync(b.Config.WalletId, "wallet-b", keys[1].PubKey.ToHex());
        Assert.Equal(["Label0", "Label1", "Label2"], config!.CoSigners.Select(c => c.Name));
    }

    // ─── Checking a room's payment against the local copy (T018) ───

    [Fact]
    public async Task CheckProposalMatches_accepts_the_same_payment_and_refuses_a_different_one()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0, utxos: [.. Utxos(), .. Utxos()]);
        var first = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var second = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 2m, 10);

        Assert.Null(a.Sut.CheckProposalMatches(first, a.Config, first.PsbtBase64));
        Assert.NotNull(a.Sut.CheckProposalMatches(first, a.Config, second.PsbtBase64));
    }

    [Fact]
    public async Task CheckProposalMatches_refuses_a_local_copy_whose_saved_amount_differs_from_its_psbt()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var tampered = new PendingMultisigTransaction
        {
            Id = proposal.Id, WalletId = proposal.WalletId, PsbtBase64 = proposal.PsbtBase64,
            DestinationAddress = proposal.DestinationAddress, AmountSatoshis = proposal.AmountSatoshis + 1,
            FeeSatoshis = proposal.FeeSatoshis, RequiredSignatures = 2, UnsignedTxId = proposal.UnsignedTxId,
        };

        Assert.NotNull(a.Sut.CheckProposalMatches(tampered, a.Config, proposal.PsbtBase64));
    }

    // ─── Merging room signatures (T018, T034) ───

    [Fact]
    public async Task Merge_accepts_a_valid_co_signer_signature_and_reaches_ready_at_the_threshold()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        await a.Sut.SignPendingTransactionAsync(proposal, keys[0]);

        var result = await a.Sut.MergeRoomSignaturesAsync(proposal, SignedCopy(proposal.PsbtBase64, keys[1]));

        Assert.Contains(keys[1].PubKey.ToHex(), result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.Equal(2, proposal.SignatureCount);
        Assert.Equal(PendingTxStatus.ReadyToFinalize, proposal.Status);
    }

    [Fact]
    public async Task Merge_discards_a_signature_that_does_not_verify()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var forged = PSBT.Parse(proposal.PsbtBase64, Net);
        forged.Inputs[0].PartialSigs.Add(keys[1].PubKey, new TransactionSignature(keys[1].Sign(uint256.One), SigHash.All));

        var result = await a.Sut.MergeRoomSignaturesAsync(proposal, forged.ToBase64());

        Assert.Contains(keys[1].PubKey.ToHex(), result.Rejected);
        Assert.Empty(proposal.SignedBy);
        Assert.Empty(PSBT.Parse(proposal.PsbtBase64, Net).Inputs[0].PartialSigs);
    }

    [Fact]
    public async Task Merge_discards_a_signature_from_a_key_outside_the_multisig()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var outsider = new Key();

        var result = await a.Sut.MergeRoomSignaturesAsync(proposal, SignedCopy(proposal.PsbtBase64, outsider));

        Assert.Empty(result.Accepted);
        Assert.Empty(proposal.SignedBy);
    }

    [Fact]
    public async Task Merge_refuses_another_transaction()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0, utxos: [.. Utxos(), .. Utxos()]);
        var first = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var second = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 2m, 10);

        await Assert.ThrowsAsync<PsbtImportException>(() => a.Sut.MergeRoomSignaturesAsync(first, SignedCopy(second.PsbtBase64, keys[1])));
    }

    [Fact]
    public async Task Merge_counts_each_key_once()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        var copy = SignedCopy(proposal.PsbtBase64, keys[1]);

        await a.Sut.MergeRoomSignaturesAsync(proposal, copy);
        await a.Sut.MergeRoomSignaturesAsync(proposal, copy);

        Assert.Equal(1, proposal.SignatureCount);
    }

    [Fact]
    public async Task Joining_with_an_offline_imported_signature_keeps_both_sides()
    {
        var keys = Keys();
        var utxos = Utxos();
        var a = await DeviceAsync(keys, 0, utxos: utxos);
        var b = await DeviceAsync(keys, 1, owner: "wallet-b", utxos: utxos);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);

        // B already holds key 2's signature from an earlier offline import; the room has A's.
        var bLocal = await b.Sut.AdoptProposalAsync(b.Config, SignedCopy(proposal.PsbtBase64, keys[2]));
        await b.Sut.MergeRoomSignaturesAsync(bLocal, SignedCopy(proposal.PsbtBase64, keys[0]));

        Assert.Equal(2, bLocal.SignatureCount);
        Assert.Contains(keys[0].PubKey.ToHex(), bLocal.SignedBy);
        Assert.Contains(keys[2].PubKey.ToHex(), bLocal.SignedBy);
    }

    // ─── Broadcast made by another participant (T030) ───

    [Fact]
    public async Task MarkBroadcast_records_the_txid_and_is_idempotent()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);

        await a.Sut.MarkBroadcastAsync(proposal, "abc123");
        await a.Sut.MarkBroadcastAsync(proposal, "abc123");

        Assert.Equal(PendingTxStatus.Broadcast, proposal.Status);
        Assert.Equal("abc123", proposal.BroadcastTxId);
    }

    [Fact]
    public async Task MarkBroadcast_refuses_a_cancelled_proposal()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        await a.Sut.CancelPendingAsync(proposal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Sut.MarkBroadcastAsync(proposal, "abc123"));
    }

    [Theory]
    [InlineData("txn-already-in-mempool")]
    [InlineData("Transaction already in block chain")]
    [InlineData("txn-already-known")]
    public async Task Broadcast_treats_already_known_as_success(string nodeMessage)
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        await a.Sut.SignPendingTransactionAsync(proposal, keys[0]);
        await a.Sut.MergeRoomSignaturesAsync(proposal, SignedCopy(proposal.PsbtBase64, keys[1]));
        a.Chain.BroadcastFailure = new InvalidOperationException(nodeMessage);

        var txId = await a.Sut.FinalizeAndBroadcastAsync(proposal);

        Assert.Equal(64, txId.Length);
        Assert.Equal(PendingTxStatus.Broadcast, proposal.Status);
        Assert.Equal(txId, proposal.BroadcastTxId);
    }

    [Fact]
    public async Task Broadcast_still_fails_for_other_errors()
    {
        var keys = Keys();
        var a = await DeviceAsync(keys, 0);
        var proposal = await a.Sut.CreateSpendingPSBTAsync(a.Config, Destination(), 1m, 10);
        await a.Sut.SignPendingTransactionAsync(proposal, keys[0]);
        await a.Sut.MergeRoomSignaturesAsync(proposal, SignedCopy(proposal.PsbtBase64, keys[1]));
        a.Chain.BroadcastFailure = new InvalidOperationException("bad-txns-inputs-missingorspent");

        await Assert.ThrowsAsync<InvalidOperationException>(() => a.Sut.FinalizeAndBroadcastAsync(proposal));
        Assert.Equal(PendingTxStatus.Failed, proposal.Status);
    }
}

/// <summary>Multisig follows the app's network setting (needed to use multisig and signing rooms on testnet/regtest).</summary>
public class MultisigNetworkSourceTests
{
    [Fact]
    public void NetworkModeSource_wins_over_SetNetworkMode_and_is_read_on_every_use()
    {
        var sut = new MultisigWalletService(new MultisigFakeStorage(), new MultisigFakeChain(), new MultisigFakeCrypto());
        var mode = "regtest";
        sut.SetNetworkMode("mainnet");
        sut.NetworkModeSource = () => mode;

        Assert.Equal(DigiByteNetwork.Regtest, sut.ActiveNetwork);
        mode = "testnet";
        Assert.Equal(DigiByteNetwork.Testnet, sut.ActiveNetwork);
    }

    [Fact]
    public async Task A_multisig_created_in_regtest_gets_a_regtest_address()
    {
        var sut = new MultisigWalletService(new MultisigFakeStorage(), new MultisigFakeChain(), new MultisigFakeCrypto())
        {
            NetworkModeSource = () => "regtest",
        };
        var keys = new[] { new Key(), new Key() };
        var config = await sut.CreateMultisigWalletAsync("R", 2,
            keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == 0 }).ToList(),
            ownerWalletId: "w");

        Assert.Equal(DigiByteNetwork.Regtest, BitcoinAddress.Create(config.Address, DigiByteNetwork.Regtest).Network);
        Assert.ThrowsAny<FormatException>(() => BitcoinAddress.Create(config.Address, DigiByteNetwork.Mainnet));
    }
}

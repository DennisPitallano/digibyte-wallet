using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

public class MultisigWalletServiceTests
{
    private const string Owner = "wallet-a";

    private static Key[] GenerateKeys(int count)
    {
        var keys = new Key[count];
        for (int i = 0; i < count; i++)
            keys[i] = new Key();
        return keys;
    }

    private static MultisigWalletService CreateService(
        MultisigFakeStorage? storage = null,
        MultisigFakeChain? blockchain = null)
    {
        return new MultisigWalletService(
            storage ?? new MultisigFakeStorage(),
            blockchain ?? new MultisigFakeChain(),
            new MultisigFakeCrypto());
    }

    // ─── Drafts ───

    [Fact]
    public async Task Drafts_SaveListUpdateDelete_RoundTrip()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);

        var saved = await sut.SaveDraftAsync(new MultisigDraft
        {
            OwnerWalletId = Owner,
            Name = "Family",
            RequiredSignatures = 2,
            TotalSigners = 3,
            OwnPublicKeyHex = "02aa",
            CoSigners = [new DraftCoSigner { Name = "Alice", PublicKeyHex = "" }, new DraftCoSigner()],
        });
        Assert.False(string.IsNullOrEmpty(saved.Id));

        // A fresh service on the same storage sees it (it survives a reload).
        var reloaded = await CreateService(storage).GetDraftAsync(saved.Id, Owner);
        Assert.NotNull(reloaded);
        Assert.Equal("Family", reloaded!.Name);
        Assert.Equal("Alice", reloaded.CoSigners[0].Name);

        // Saving again with the same id replaces it rather than adding a second one.
        reloaded.CoSigners[0].PublicKeyHex = "03bb";
        await sut.SaveDraftAsync(reloaded);
        var all = await sut.ListDraftsAsync(Owner);
        Assert.Single(all);
        Assert.Equal("03bb", all[0].CoSigners[0].PublicKeyHex);

        await sut.DeleteDraftAsync(saved.Id);
        Assert.Empty(await sut.ListDraftsAsync(Owner));
        Assert.Null(await sut.GetDraftAsync(saved.Id, Owner));
    }

    [Fact]
    public async Task Drafts_RequireAName_AndListNewestFirst()
    {
        var sut = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SaveDraftAsync(new MultisigDraft { Name = "  " }));

        var first = await sut.SaveDraftAsync(new MultisigDraft { Name = "First", OwnerWalletId = Owner });
        await Task.Delay(5);
        var second = await sut.SaveDraftAsync(new MultisigDraft { Name = "Second", OwnerWalletId = Owner });
        var all = await sut.ListDraftsAsync(Owner);
        Assert.Equal([second.Id, first.Id], all.Select(d => d.Id).ToArray());
    }

    // ─── FindOwnKeyIndex ───

    [Fact]
    public async Task FindOwnKeyIndex_LocatesOwnKeyInSortedScript_AndReturnsMinusOneForOutsiders()
    {
        var keys = GenerateKeys(3);
        var coSigners = keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex() }).ToList();
        var config = await CreateService().CreateMultisigWalletAsync("Idx", 2, coSigners, ownerWalletId: Owner);

        // Keys are BIP67-sorted inside the script, so the index must come from the script itself.
        var sorted = PayToMultiSigTemplate.Instance
            .ExtractScriptPubKeyParameters(new Script(Convert.FromHexString(config.RedeemScriptHex)))!
            .PubKeys.Select(p => p.ToHex()).ToList();
        foreach (var k in keys)
        {
            var expected = sorted.FindIndex(h => h.Equals(k.PubKey.ToHex(), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected, MultisigWalletService.FindOwnKeyIndex(config.RedeemScriptHex, k.PubKey.ToHex()));
        }

        Assert.Equal(-1, MultisigWalletService.FindOwnKeyIndex(config.RedeemScriptHex, new Key().PubKey.ToHex()));
        Assert.Equal(-1, MultisigWalletService.FindOwnKeyIndex(config.RedeemScriptHex, null));
        Assert.Equal(-1, MultisigWalletService.FindOwnKeyIndex("not-hex", keys[0].PubKey.ToHex()));
    }

    // ─── CreateMultisigWalletAsync ───

    [Fact]
    public async Task CreateMultisigWallet_ReturnsValidConfig()
    {
        var keys = GenerateKeys(3);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = i == 0,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Test Wallet", 2, coSigners, ownerWalletId: Owner);

        Assert.Equal(2, config.RequiredSignatures);
        Assert.Equal(3, config.TotalSigners);
        Assert.Equal(3, config.CoSigners.Count);
        Assert.NotEmpty(config.RedeemScriptHex);
        Assert.NotEmpty(config.Address);
        Assert.Equal(0, config.OwnKeyIndex);
        Assert.Equal("2-of-3 Multisig", config.Label);
    }

    [Fact]
    public async Task CreateMultisigWallet_PersistsToStorage()
    {
        var storage = new MultisigFakeStorage();
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = i == 0,
        }).ToList();

        var sut = CreateService(storage);
        var config = await sut.CreateMultisigWalletAsync("Persisted", 2, coSigners, ownerWalletId: Owner);

        // Verify it was stored
        Assert.True(await storage.ContainsKeyAsync($"multisig_config_{config.WalletId}"));
    }

    [Fact]
    public async Task CreateMultisigWallet_P2WSH_UsesNativeSegwit()
    {
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = false,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Native", 2, coSigners, addressType: "p2wsh", ownerWalletId: Owner);

        Assert.Equal("p2wsh", config.AddressType);
        Assert.StartsWith("dgb1", config.Address);
    }

    [Fact]
    public async Task CreateMultisigWallet_NoLocalKey_OwnKeyIndexIsNegative()
    {
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = k.PubKey.ToHex(),
            IsLocal = false,
        }).ToList();

        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("WatchOnly", 2, coSigners, ownerWalletId: Owner);

        Assert.Equal(-1, config.OwnKeyIndex);
    }

    // ─── ImportMultisigWalletAsync ───

    [Fact]
    public async Task ImportMultisigWallet_FromRedeemScript_ReturnsCorrectConfig()
    {
        var keys = GenerateKeys(3);
        var msService = new MultisigService(DigiByteNetwork.Mainnet);
        var script = msService.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var sut = CreateService();
        var config = await sut.ImportMultisigWalletAsync("Imported", script.ToHex(), ownKeyIndex: 1, ownerWalletId: Owner);

        Assert.Equal(2, config.RequiredSignatures);
        Assert.Equal(3, config.TotalSigners);
        Assert.Equal(1, config.OwnKeyIndex);
        Assert.True(config.CoSigners[1].IsLocal);
        Assert.False(config.CoSigners[0].IsLocal);
    }

    [Fact]
    public async Task ImportMultisigWallet_InvalidScript_Throws()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ImportMultisigWalletAsync("Bad", "deadbeef", ownKeyIndex: 0));
    }

    [Fact]
    public async Task ImportMultisigWallet_WatchOnly_OwnKeyNegative()
    {
        var keys = GenerateKeys(2);
        var msService = new MultisigService(DigiByteNetwork.Mainnet);
        var script = msService.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var sut = CreateService();
        var config = await sut.ImportMultisigWalletAsync("Watch", script.ToHex(), ownerWalletId: Owner);

        Assert.Equal(-1, config.OwnKeyIndex);
        Assert.True(config.CoSigners.All(cs => !cs.IsLocal));
    }

    // ─── ListWalletsAsync / GetConfigAsync / DeleteWalletAsync ───

    [Fact]
    public async Task ListWallets_EmptyInitially()
    {
        var sut = CreateService();
        var list = await sut.ListWalletsAsync(Owner);
        Assert.Empty(list);
    }

    [Fact]
    public async Task ListWallets_AfterCreate_ContainsWallet()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        await sut.CreateMultisigWalletAsync("MyMultisig", 2, coSigners, ownerWalletId: Owner);
        var list = await sut.ListWalletsAsync(Owner);

        Assert.Single(list);
        Assert.Equal("MyMultisig", list[0].Name);
    }

    [Fact]
    public async Task GetConfig_ReturnsStoredConfig()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var created = await sut.CreateMultisigWalletAsync("Test", 2, coSigners, ownerWalletId: Owner);
        var loaded = await sut.GetConfigAsync(created.WalletId, Owner);

        Assert.NotNull(loaded);
        Assert.Equal(created.WalletId, loaded.WalletId);
        Assert.Equal(created.Address, loaded.Address);
        Assert.Equal(created.RequiredSignatures, loaded.RequiredSignatures);
    }

    [Fact]
    public async Task GetConfig_NonExistent_ReturnsNull()
    {
        var sut = CreateService();
        var result = await sut.GetConfigAsync("nonexistent", Owner);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteWallet_RemovesFromList()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("ToDelete", 2, coSigners, ownerWalletId: Owner);
        await sut.DeleteWalletAsync(config.WalletId, Owner);

        var list = await sut.ListWalletsAsync(Owner);
        Assert.Empty(list);

        var loaded = await sut.GetConfigAsync(config.WalletId, Owner);
        Assert.Null(loaded);
    }

    // ─── GetBalanceAsync ───

    [Fact]
    public async Task GetBalance_DelegatesToBlockchain()
    {
        var blockchain = new MultisigFakeChain(balance: 500_000_000);
        var sut = CreateService(blockchain: blockchain);
        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("Bal", 2, coSigners, ownerWalletId: Owner);
        var balance = await sut.GetBalanceAsync(config);

        Assert.Equal(500_000_000, balance);
    }

    // ─── PendingTransactions ───

    [Fact]
    public async Task GetPendingTransactions_EmptyInitially()
    {
        var sut = CreateService();
        var list = await sut.GetPendingTransactionsAsync("some-wallet-id");
        Assert.Empty(list);
    }

    // ─── SetNetworkMode ───

    [Fact]
    public async Task SetNetworkMode_Testnet_AffectsAddresses()
    {
        var sut = CreateService();
        sut.SetNetworkMode("testnet");

        var keys = GenerateKeys(2);
        var coSigners = keys.Select((k, i) => new CoSigner
        {
            Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false,
        }).ToList();

        var config = await sut.CreateMultisigWalletAsync("Testnet", 2, coSigners, addressType: "p2wsh", ownerWalletId: Owner);

        Assert.StartsWith("dgbt1", config.Address);
    }

    // ─── Proposals paying several recipients (specs/029-multisig-multiple-recipients) ───

    private static string MainnetAddress() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();

    private static async Task<(MultisigWalletService Sut, MultisigWalletConfig Config, Key[] Keys, MultisigFakeChain Chain)> FundedTwoOfThreeAsync(long amount = 1_000_000_000)
    {
        var keys = GenerateKeys(3);
        var chain = new MultisigFakeChain(utxos:
        [
            new UtxoInfo { TxId = RandomUtils.GetUInt256().ToString(), OutputIndex = 0, AmountSatoshis = amount, ScriptPubKey = "", Confirmations = 10 },
        ]);
        var sut = CreateService(blockchain: chain);
        var config = await sut.CreateMultisigWalletAsync("Treasury", 2,
            keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == 0 }).ToList(),
            ownerWalletId: Owner);
        return (sut, config, keys, chain);
    }

    [Fact]
    public async Task CreateSpending_SeveralRecipients_SavesOneProposalPayingEachInOrder()
    {
        var (sut, config, _, _) = await FundedTwoOfThreeAsync();
        var (a, b, c) = (MainnetAddress(), MainnetAddress(), MainnetAddress());

        var proposal = await sut.CreateSpendingPSBTAsync(config, [(a, 1m), (b, 0.5m), (c, 0.25m)], 10, "payroll");

        Assert.Equal([new ProposalRecipient(a, 100_000_000), new(b, 50_000_000), new(c, 25_000_000)], proposal.Recipients);
        Assert.Equal(a, proposal.DestinationAddress);
        Assert.Equal(175_000_000, proposal.AmountSatoshis);
        Assert.Equal("Send 1.75000000 DGB to 3 recipients", proposal.Description);

        var outputs = PSBT.Parse(proposal.PsbtBase64, DigiByteNetwork.Mainnet).GetGlobalTransaction().Outputs;
        Assert.Equal(a, outputs[0].ScriptPubKey.GetDestinationAddress(DigiByteNetwork.Mainnet)!.ToString());
        Assert.Equal(b, outputs[1].ScriptPubKey.GetDestinationAddress(DigiByteNetwork.Mainnet)!.ToString());
        Assert.Equal(c, outputs[2].ScriptPubKey.GetDestinationAddress(DigiByteNetwork.Mainnet)!.ToString());
        Assert.Single(await sut.GetPendingTransactionsAsync(config.WalletId));
    }

    [Fact]
    public async Task CreateSpending_SeveralRecipients_RefusesBadInput()
    {
        var (sut, config, _, _) = await FundedTwoOfThreeAsync();
        var repeated = MainnetAddress();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, Array.Empty<(string, decimal)>(), 10));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config,
            Enumerable.Range(0, 21).Select(_ => (MainnetAddress(), 0.01m)).ToList(), 10));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, [(repeated, 1m), (repeated, 1m)], 10));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, [(MainnetAddress(), 1m), (config.Address, 1m)], 10));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, [(MainnetAddress(), 1m), (MainnetAddress(), 0.0009m)], 10));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, [(MainnetAddress(), 1m), (MainnetAddress(), 0.123456789m)], 10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSpendingPSBTAsync(config, [(MainnetAddress(), 6m), (MainnetAddress(), 5m)], 10));
        Assert.Empty(await sut.GetPendingTransactionsAsync(config.WalletId));
    }

    [Fact]
    public async Task CreateSpending_SeveralRecipients_SameSubmissionToken_ReturnsTheFirstProposal()
    {
        var (sut, config, _, _) = await FundedTwoOfThreeAsync();
        List<(string, decimal)> recipients = [(MainnetAddress(), 1m), (MainnetAddress(), 2m)];

        var first = await sut.CreateSpendingPSBTAsync(config, recipients, 10, submissionToken: "tap-1");
        var again = await sut.CreateSpendingPSBTAsync(config, recipients, 10, submissionToken: "tap-1");

        Assert.Equal(first.Id, again.Id);
        Assert.Single(await sut.GetPendingTransactionsAsync(config.WalletId));
    }

    [Fact]
    public async Task CreateSpending_OneRecipient_SavesTheSameProposalAsBefore()
    {
        var (sut, config, _, _) = await FundedTwoOfThreeAsync();
        var dest = MainnetAddress();

        var proposal = await sut.CreateSpendingPSBTAsync(config, dest, 1m, 10);

        Assert.Equal(dest, proposal.DestinationAddress);
        Assert.Equal(100_000_000, proposal.AmountSatoshis);
        Assert.Equal($"Send 1.00000000 DGB to {dest[..12]}...", proposal.Description);
        Assert.Equal([new ProposalRecipient(dest, 100_000_000)], proposal.AllRecipients);
        Assert.Equal(2, PSBT.Parse(proposal.PsbtBase64, DigiByteNetwork.Mainnet).GetGlobalTransaction().Outputs.Count);
    }

    [Fact]
    public async Task A_received_payment_to_three_recipients_signed_by_two_co_signers_broadcasts_every_output()
    {
        var (sut, config, keys, chain) = await FundedTwoOfThreeAsync();
        var network = DigiByteNetwork.Mainnet;
        var (a, b, c) = (MainnetAddress(), MainnetAddress(), MainnetAddress());

        // Built with the Crypto builder and received like a co-signer's copy, so this doesn't depend on creating it here.
        var multisig = BitcoinAddress.Create(config.Address, network);
        var coin = new Coin(uint256.Parse(chain.Utxos[0].TxId), 0, Money.Satoshis(1_000_000_000), multisig.ScriptPubKey);
        var psbt = new MultisigService(network).CreateMultisigPSBTWithFee(
            new Script(Convert.FromHexString(config.RedeemScriptHex)), [coin],
            [(BitcoinAddress.Create(a, network), Money.Satoshis(100_000_000)),
             (BitcoinAddress.Create(b, network), Money.Satoshis(50_000_000)),
             (BitcoinAddress.Create(c, network), Money.Satoshis(25_000_000))],
            multisig, 20_000, MultisigSendHelper.DustThresholdSatoshis);
        var received = (await sut.ReceiveProposalAsync(config, psbt.ToBase64())).Pending;

        var signed = await sut.SignPendingTransactionAsync(received, keys[0]);
        signed = await sut.SignPendingTransactionAsync(signed, keys[2]);
        var txId = await sut.FinalizeAndBroadcastAsync(signed);

        var tx = Transaction.Parse(chain.LastBroadcastHex!, network);
        Assert.False(string.IsNullOrEmpty(txId));
        Assert.Equal(4, tx.Outputs.Count);
        Assert.Equal(100_000_000, tx.Outputs[0].Value.Satoshi);
        Assert.Equal(a, tx.Outputs[0].ScriptPubKey.GetDestinationAddress(network)!.ToString());
        Assert.Equal(50_000_000, tx.Outputs[1].Value.Satoshi);
        Assert.Equal(b, tx.Outputs[1].ScriptPubKey.GetDestinationAddress(network)!.ToString());
        Assert.Equal(25_000_000, tx.Outputs[2].Value.Satoshi);
        Assert.Equal(c, tx.Outputs[2].ScriptPubKey.GetDestinationAddress(network)!.ToString());
        Assert.Equal(multisig.ScriptPubKey, tx.Outputs[3].ScriptPubKey);
        Assert.Equal(1_000_000_000 - 175_000_000 - 20_000, tx.Outputs[3].Value.Satoshi);

        var saved = Assert.Single(await sut.GetPendingTransactionsAsync(config.WalletId));
        Assert.Equal(PendingTxStatus.Broadcast, saved.Status);
    }
}

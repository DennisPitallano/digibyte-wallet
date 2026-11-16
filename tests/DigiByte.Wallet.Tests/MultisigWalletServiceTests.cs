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
}

using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>A multisig belongs to the wallet(s) that own it: lists, drafts and direct opens are scoped (spec 016, US1).</summary>
public class MultisigScopingTests
{
    private const string A = "wallet-a";
    private const string B = "wallet-b";

    private static MultisigWalletService CreateService(MultisigFakeStorage? storage = null) =>
        new(storage ?? new MultisigFakeStorage(), new MultisigFakeChain(), new MultisigFakeCrypto());

    private static List<CoSigner> Signers(params Key[] keys) =>
        keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = false }).ToList();

    [Fact]
    public async Task Multisig_CreatedUnderWalletA_IsListedForA_AndNotForB()
    {
        var sut = CreateService();
        await sut.CreateMultisigWalletAsync("Treasury", 2, Signers(new Key(), new Key()), ownerWalletId: A);

        Assert.Single(await sut.ListWalletsAsync(A));
        Assert.Empty(await sut.ListWalletsAsync(B));
    }

    [Fact]
    public async Task Multisig_CreatedAndImportedUnderB_AppearOnlyForB()
    {
        var sut = CreateService();
        await sut.CreateMultisigWalletAsync("Created", 2, Signers(new Key(), new Key()), ownerWalletId: B);
        var script = new DigiByte.Crypto.Multisig.MultisigService().CreateRedeemScript(2, new[] { new Key(), new Key() }.Select(k => k.PubKey));
        await sut.ImportMultisigWalletAsync("Imported", script.ToHex(), ownerWalletId: B);

        Assert.Equal(2, (await sut.ListWalletsAsync(B)).Count);
        Assert.Empty(await sut.ListWalletsAsync(A));
    }

    [Fact]
    public async Task GetConfig_ForAnotherWalletsMultisig_ReturnsNull()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Mine", 2, Signers(new Key(), new Key()), ownerWalletId: A);

        Assert.NotNull(await sut.GetConfigAsync(config.WalletId, A));
        Assert.Null(await sut.GetConfigAsync(config.WalletId, B));
    }

    [Fact]
    public async Task Drafts_AreScopedByOwner()
    {
        var sut = CreateService();
        await sut.SaveDraftAsync(new MultisigDraft { Name = "Draft A", OwnerWalletId = A });

        Assert.Single(await sut.ListDraftsAsync(A));
        Assert.Empty(await sut.ListDraftsAsync(B));
    }

    [Fact]
    public async Task LegacyDraft_WithoutOwner_IsAttributedByOwnPublicKey()
    {
        var sut = CreateService();
        var keyA = new Key().PubKey.ToHex();
        var saved = await sut.SaveDraftAsync(new MultisigDraft { Name = "Old draft", OwnPublicKeyHex = keyA });

        Assert.Empty(await sut.ListDraftsAsync(B, new Key().PubKey.ToHex()));
        var forA = await sut.ListDraftsAsync(A, keyA);
        Assert.Single(forA);
        Assert.Equal(A, forA[0].OwnerWalletId);

        // Attribution sticks, so the draft no longer needs the key to be listed for A.
        Assert.Single(await sut.ListDraftsAsync(A));
        Assert.Equal(saved.Id, (await sut.ListDraftsAsync(A))[0].Id);
    }

    [Fact]
    public async Task LegacyMultisig_WhoseKeyMatchesTheActiveWallet_IsAttributedToIt()
    {
        var mine = new Key();
        var sut = CreateService();
        await sut.CreateMultisigWalletAsync("Legacy", 2, Signers(mine, new Key()));   // no owner = saved before scoping

        var forA = await sut.ListWalletsAsync(A, mine.PubKey.ToHex());
        Assert.Single(forA);

        // Another wallet without a key in it does not get it, and A keeps it afterwards without needing the key again.
        Assert.Empty(await sut.ListWalletsAsync(B, new Key().PubKey.ToHex()));
        Assert.Single(await sut.ListWalletsAsync(A));
    }

    [Fact]
    public async Task UnmatchedLegacyMultisig_IsHiddenEverywhere_ButListedAsUnassigned()
    {
        var sut = CreateService();
        await sut.CreateMultisigWalletAsync("Orphan", 2, Signers(new Key(), new Key()));

        Assert.Empty(await sut.ListWalletsAsync(A, new Key().PubKey.ToHex()));
        Assert.Empty(await sut.ListWalletsAsync(B));
        var unassigned = await sut.ListUnassignedAsync();
        Assert.Single(unassigned);
        Assert.Equal("Orphan", unassigned[0].Name);
    }

    [Fact]
    public async Task Claim_AssignsTheMultisig_IsIdempotent_AndRefusesWhenOwnedByAnother()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Orphan", 2, Signers(new Key(), new Key()));

        await sut.ClaimAsync(config.WalletId, A);
        await sut.ClaimAsync(config.WalletId, A);              // idempotent
        Assert.Single(await sut.ListWalletsAsync(A));
        Assert.Empty(await sut.ListUnassignedAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ClaimAsync(config.WalletId, B));
        Assert.Empty(await sut.ListWalletsAsync(B));
    }

    [Fact]
    public async Task Migration_NeverDeletesData()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var config = await sut.CreateMultisigWalletAsync("Keep me", 2, Signers(new Key(), new Key()));

        await sut.ListWalletsAsync(A, new Key().PubKey.ToHex());
        await sut.ListWalletsAsync(B);

        Assert.True(await storage.ContainsKeyAsync($"multisig_config_{config.WalletId}"));
        Assert.Single(await sut.ListUnassignedAsync());
    }

    [Fact]
    public async Task DeleteWallet_FromTheOnlyOwner_RemovesConfigPendingAndEntry()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var config = await sut.CreateMultisigWalletAsync("Gone", 2, Signers(new Key(), new Key()), ownerWalletId: A);
        storage.Put($"multisig_pending_{config.WalletId}", "[]");

        await sut.DeleteWalletAsync(config.WalletId, A);

        Assert.False(await storage.ContainsKeyAsync($"multisig_config_{config.WalletId}"));
        Assert.False(await storage.ContainsKeyAsync($"multisig_pending_{config.WalletId}"));
        Assert.Empty(await sut.ListWalletsAsync(A));
    }

    [Fact]
    public async Task MultisigCoSignedByTwoWallets_ShowsInBoth_AndDeleteFromOneLeavesItForTheOther()
    {
        var keyA = new Key();
        var keyB = new Key();
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Shared", 2, Signers(keyA, keyB, new Key()));

        Assert.Single(await sut.ListWalletsAsync(A, keyA.PubKey.ToHex()));
        Assert.Single(await sut.ListWalletsAsync(B, keyB.PubKey.ToHex()));
        Assert.Empty(await sut.ListWalletsAsync("wallet-c", new Key().PubKey.ToHex()));

        await sut.DeleteWalletAsync(config.WalletId, A);

        Assert.Empty(await sut.ListWalletsAsync(A));
        Assert.Single(await sut.ListWalletsAsync(B));
        Assert.NotNull(await sut.GetConfigAsync(config.WalletId, B));
    }

    [Fact]
    public async Task GetAccessState_DistinguishesOwned_OtherWallet_Unassigned_AndNotFound()
    {
        var mine = new Key();
        var sut = CreateService();
        var owned = await sut.CreateMultisigWalletAsync("Owned", 2, Signers(new Key(), new Key()), ownerWalletId: A);
        var legacyMine = await sut.CreateMultisigWalletAsync("Legacy mine", 2, Signers(mine, new Key()));
        var legacyOrphan = await sut.CreateMultisigWalletAsync("Orphan", 2, Signers(new Key(), new Key()));

        Assert.Equal(MultisigAccessState.Owned, await sut.GetAccessStateAsync(owned.WalletId, A, null));
        Assert.Equal(MultisigAccessState.OtherWallet, await sut.GetAccessStateAsync(owned.WalletId, B, null));
        // A direct link to the user's own legacy multisig works even though the list was never opened (spec N2).
        Assert.Equal(MultisigAccessState.Owned, await sut.GetAccessStateAsync(legacyMine.WalletId, A, mine.PubKey.ToHex()));
        Assert.Equal(MultisigAccessState.Unassigned, await sut.GetAccessStateAsync(legacyOrphan.WalletId, A, new Key().PubKey.ToHex()));
        Assert.Equal(MultisigAccessState.NotFound, await sut.GetAccessStateAsync("missing", A, null));

        Assert.NotNull(await sut.GetConfigAsync(legacyMine.WalletId, A, mine.PubKey.ToHex()));
        Assert.Null(await sut.GetConfigAsync(legacyOrphan.WalletId, A, new Key().PubKey.ToHex()));
    }
}

using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>Renaming a multisig, a draft, or a co-signer label is local only and never touches the script (spec 016, US2).</summary>
public class MultisigRenameTests
{
    private const string A = "wallet-a";

    private static MultisigWalletService CreateService(MultisigFakeStorage? storage = null) =>
        new(storage ?? new MultisigFakeStorage(), new MultisigFakeChain(), new MultisigFakeCrypto());

    private static List<CoSigner> Signers(int n = 3) =>
        Enumerable.Range(0, n).Select(i => new CoSigner { Name = $"S{i}", PublicKeyHex = new Key().PubKey.ToHex() }).ToList();

    [Fact]
    public async Task Rename_TrimsTheName_AndPersistsAcrossAFreshService()
    {
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage);
        var config = await sut.CreateMultisigWalletAsync("Old", 2, Signers(), ownerWalletId: A);

        var result = await sut.RenameWalletAsync(config.WalletId, "  Family Treasury  ");

        Assert.True(result.Ok);
        Assert.Equal("Family Treasury", result.Name);
        var reloaded = await CreateService(storage).GetConfigAsync(config.WalletId, A);
        Assert.Equal("Family Treasury", reloaded!.DisplayName);
        Assert.Equal("Family Treasury", (await CreateService(storage).ListWalletsAsync(A))[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_EmptyOrWhitespace_IsRejected_AndKeepsThePreviousName(string name)
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Keep", 2, Signers(), ownerWalletId: A);

        var result = await sut.RenameWalletAsync(config.WalletId, name);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal("Keep", (await sut.ListWalletsAsync(A))[0].Name);
    }

    [Fact]
    public async Task Rename_EnforcesOneToFortyCharacters_WithoutSilentlyCapping()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Start", 2, Signers(), ownerWalletId: A);

        Assert.True((await sut.RenameWalletAsync(config.WalletId, new string('x', 40))).Ok);

        var tooLong = await sut.RenameWalletAsync(config.WalletId, new string('y', 41));
        Assert.False(tooLong.Ok);
        Assert.Equal(new string('x', 40), (await sut.ListWalletsAsync(A))[0].Name);
    }

    [Fact]
    public async Task Rename_ToANameAnotherMultisigAlreadyUses_IsAllowedWithAWarning()
    {
        var sut = CreateService();
        await sut.CreateMultisigWalletAsync("Savings", 2, Signers(), ownerWalletId: A);
        var second = await sut.CreateMultisigWalletAsync("Other", 2, Signers(), ownerWalletId: A);

        var result = await sut.RenameWalletAsync(second.WalletId, "savings");

        Assert.True(result.Ok);
        Assert.True(result.DuplicateWarning);
    }

    [Fact]
    public async Task Rename_NeverChangesAddressScriptThresholdOrKeys()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Before", 2, Signers(), ownerWalletId: A);
        var keysBefore = config.CoSigners.Select(c => c.PublicKeyHex).ToList();

        await sut.RenameWalletAsync(config.WalletId, "After");
        var after = (await sut.GetConfigAsync(config.WalletId, A))!;

        Assert.Equal(config.Address, after.Address);
        Assert.Equal(config.RedeemScriptHex, after.RedeemScriptHex);
        Assert.Equal(config.RequiredSignatures, after.RequiredSignatures);
        Assert.Equal(keysBefore, after.CoSigners.Select(c => c.PublicKeyHex).ToList());
    }

    [Fact]
    public async Task RenameCoSigner_ChangesOnlyTheLabel()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("W", 2, Signers(), ownerWalletId: A);
        var key = config.CoSigners[1].PublicKeyHex;

        var ok = await sut.RenameCoSignerAsync(config.WalletId, key, " Treasury ");
        var bad = await sut.RenameCoSignerAsync(config.WalletId, key, "");
        var tooLong = await sut.RenameCoSignerAsync(config.WalletId, key, new string('z', 41));

        Assert.True(ok.Ok);
        Assert.False(bad.Ok);
        Assert.False(tooLong.Ok);
        var after = (await sut.GetConfigAsync(config.WalletId, A))!;
        var signer = after.CoSigners.First(c => c.PublicKeyHex == key);
        Assert.Equal("Treasury", signer.Name);
        Assert.Equal(config.RedeemScriptHex, after.RedeemScriptHex);
    }

    [Fact]
    public async Task RenameDraft_KeepsAllOtherProgress()
    {
        var sut = CreateService();
        var draft = await sut.SaveDraftAsync(new MultisigDraft
        {
            Name = "Old draft",
            OwnerWalletId = A,
            RequiredSignatures = 2,
            TotalSigners = 3,
            CoSigners = [new DraftCoSigner { Name = "Alice", PublicKeyHex = "02aa" }],
        });

        var result = await sut.RenameDraftAsync(draft.Id, "New draft");

        Assert.True(result.Ok);
        var after = (await sut.GetDraftAsync(draft.Id, A))!;
        Assert.Equal("New draft", after.Name);
        Assert.Equal(3, after.TotalSigners);
        Assert.Equal("Alice", after.CoSigners[0].Name);
        Assert.Equal("02aa", after.CoSigners[0].PublicKeyHex);
        Assert.False((await sut.RenameDraftAsync(draft.Id, " ")).Ok);
    }

    [Fact]
    public async Task Backup_MentionsTheNameOnlyAsANote()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Family Treasury", 2, Signers(), ownerWalletId: A);

        var (_, content) = MultisigWalletService.BuildBackup(config);

        Assert.Contains("Family Treasury", content);
        Assert.Contains(config.RedeemScriptHex, content);
    }

    [Fact]
    public async Task Import_NeverOverwritesExistingLocalLabels()
    {
        var sut = CreateService();
        var created = await sut.CreateMultisigWalletAsync("Mine", 2, Signers(2), ownerWalletId: A);
        await sut.RenameCoSignerAsync(created.WalletId, created.CoSigners[0].PublicKeyHex, "Alice");

        // Importing the same script again creates a separate entry; the first one's labels stay as the user set them.
        await sut.ImportMultisigWalletAsync("Same script", created.RedeemScriptHex, ownerWalletId: A);

        var first = (await sut.GetConfigAsync(created.WalletId, A))!;
        Assert.Equal("Alice", first.CoSigners[0].Name);
    }

    [Fact]
    public async Task RenameByOneOwnerWallet_IsSeenByTheOther()
    {
        var keyA = new Key();
        var keyB = new Key();
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Shared", 2,
            [new CoSigner { Name = "a", PublicKeyHex = keyA.PubKey.ToHex() }, new CoSigner { Name = "b", PublicKeyHex = keyB.PubKey.ToHex() }]);
        await sut.ListWalletsAsync(A, keyA.PubKey.ToHex());
        await sut.ListWalletsAsync("wallet-b", keyB.PubKey.ToHex());

        await sut.RenameWalletAsync(config.WalletId, "Renamed");
        await sut.RenameCoSignerAsync(config.WalletId, keyB.PubKey.ToHex(), "Bob");

        Assert.Equal("Renamed", (await sut.ListWalletsAsync("wallet-b"))[0].Name);
        var seenByB = (await sut.GetConfigAsync(config.WalletId, "wallet-b"))!;
        Assert.Equal("Bob", seenByB.CoSigners.First(c => c.PublicKeyHex == keyB.PubKey.ToHex()).Name);
    }
}

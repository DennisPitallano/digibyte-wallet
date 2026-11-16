using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which co-signer is "you" depends on the wallet that is looking, not on the wallet that created the multisig.
/// Two wallets on one device that both hold a key must each see their own key as theirs.
/// </summary>
public class MultisigIdentityTests
{
    private const string WalletA = "wallet-a";
    private const string WalletB = "wallet-b";

    private static readonly Key KeyA = new();
    private static readonly Key KeyB = new();
    private static readonly Key KeyC = new();

    private static MultisigWalletService CreateService() =>
        new(new MultisigFakeStorage(), new MultisigFakeChain(), new MultisigFakeCrypto());

    /// <summary>Wallet A creates a 2-of-3 with its own key marked local and named the way the old Create page named it.</summary>
    private static async Task<(MultisigWalletService Sut, string Id)> CreatedByWalletAAsync()
    {
        var sut = CreateService();
        var config = await sut.CreateMultisigWalletAsync("Shared", 2,
        [
            new CoSigner { Name = "You (This Wallet)", PublicKeyHex = KeyA.PubKey.ToHex(), IsLocal = true },
            new CoSigner { Name = "Treasury", PublicKeyHex = KeyC.PubKey.ToHex() },
            new CoSigner { Name = "Admin", PublicKeyHex = KeyB.PubKey.ToHex() },
        ], ownerWalletId: WalletA);
        return (sut, config.WalletId);
    }

    [Fact]
    public async Task EachWalletSeesItsOwnKeyAsLocal()
    {
        var (sut, id) = await CreatedByWalletAAsync();

        var asA = (await sut.GetConfigAsync(id, WalletA, KeyA.PubKey.ToHex()))!;
        var asB = (await sut.GetConfigAsync(id, WalletB, KeyB.PubKey.ToHex()))!;

        Assert.Equal([KeyA.PubKey.ToHex()], asA.CoSigners.Where(c => c.IsLocal).Select(c => c.PublicKeyHex));
        Assert.Equal([KeyB.PubKey.ToHex()], asB.CoSigners.Where(c => c.IsLocal).Select(c => c.PublicKeyHex));
        Assert.Equal(asA.CoSigners.FindIndex(c => c.IsLocal), asA.OwnKeyIndex);
        Assert.Equal(asB.CoSigners.FindIndex(c => c.IsLocal), asB.OwnKeyIndex);
    }

    [Fact]
    public async Task AViewForOneWallet_NeverChangesWhatIsStoredForTheOther()
    {
        var (sut, id) = await CreatedByWalletAAsync();

        await sut.GetConfigAsync(id, WalletB, KeyB.PubKey.ToHex());
        var asA = (await sut.GetConfigAsync(id, WalletA, KeyA.PubKey.ToHex()))!;

        Assert.True(asA.CoSigners.Single(c => c.PublicKeyHex == KeyA.PubKey.ToHex()).IsLocal);
        Assert.False(asA.CoSigners.Single(c => c.PublicKeyHex == KeyB.PubKey.ToHex()).IsLocal);
    }

    [Fact]
    public async Task AWalletWithNoKeyInTheMultisig_OrNoKeyAtAll_IsWatchOnly()
    {
        var (sut, id) = await CreatedByWalletAAsync();
        // The creator is the owner, so it can open it; a locked wallet (no public key) must not be shown as a signer.
        var locked = (await sut.GetConfigAsync(id, WalletA, null))!;

        Assert.DoesNotContain(locked.CoSigners, c => c.IsLocal);
        Assert.Equal(-1, locked.OwnKeyIndex);
    }

    [Fact]
    public async Task TheCreatorsPlaceholderName_IsNotShownAsYouToOtherWallets()
    {
        var (sut, id) = await CreatedByWalletAAsync();

        var asA = (await sut.GetConfigAsync(id, WalletA, KeyA.PubKey.ToHex()))!;
        var asB = (await sut.GetConfigAsync(id, WalletB, KeyB.PubKey.ToHex()))!;

        Assert.Equal("You (This Wallet)", asA.CoSigners.Single(c => c.PublicKeyHex == KeyA.PubKey.ToHex()).Name);
        var seenByB = asB.CoSigners.Single(c => c.PublicKeyHex == KeyA.PubKey.ToHex());
        Assert.DoesNotContain("You", seenByB.Name);
        Assert.False(string.IsNullOrWhiteSpace(seenByB.Name));
        // Labels the users chose are never touched.
        Assert.Equal("Treasury", asB.CoSigners.Single(c => c.PublicKeyHex == KeyC.PubKey.ToHex()).Name);
    }

    [Fact]
    public async Task NeedsMySignature_IsPerWallet()
    {
        var (sut, id) = await CreatedByWalletAAsync();
        var proposal = new PendingMultisigTransaction
        {
            Id = "p1",
            WalletId = id,
            PsbtBase64 = "x",
            DestinationAddress = "d",
            AmountSatoshis = 1,
            FeeSatoshis = 1,
            RequiredSignatures = 2,
            SignedBy = [KeyA.PubKey.ToHex()],   // wallet A signed it
        };

        var asA = (await sut.GetConfigAsync(id, WalletA, KeyA.PubKey.ToHex()))!;
        var asB = (await sut.GetConfigAsync(id, WalletB, KeyB.PubKey.ToHex()))!;

        Assert.False(MultisigWalletService.NeedsMySignature(proposal, asA));   // A already signed
        Assert.True(MultisigWalletService.NeedsMySignature(proposal, asB));    // B still has to
        Assert.Equal(1, MultisigWalletService.CountNeedingMySignature([proposal], asB));
        Assert.Equal(0, MultisigWalletService.CountNeedingMySignature([proposal], asA));
    }

    [Fact]
    public async Task ListWallets_StillAttributesBothWallets_ByKey()
    {
        var (sut, _) = await CreatedByWalletAAsync();

        Assert.Single(await sut.ListWalletsAsync(WalletB, KeyB.PubKey.ToHex()));
        Assert.Single(await sut.ListWalletsAsync(WalletA, KeyA.PubKey.ToHex()));
    }
}

using System.Text;
using DigiByte.Crypto.Tests;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>The wallet as the PSBT feature sees it: which scripts it owns, how it signs, and how a PIN is checked.</summary>
public class WalletServicePsbtTests
{
    private static async Task<WalletService> CreateAsync(Env env)
    {
        var service = env.NewService();
        await service.CreateWalletAsync("Test", env.Words, Pin);
        return service;
    }

    private static Key ReceiveKey(Env env, int index) => env.Hd.DeriveReceivingKey(index).PrivateKey;

    private static PsbtBuilder SpendFrom(params FundedCoin[] coins)
    {
        var builder = PsbtTestFixtures.Builder();
        foreach (var coin in coins) builder.AddInput(coin);
        return builder.Pay(coins.Sum(c => c.Amount) - 100_000);
    }

    // ─── Owned scripts ───

    [Fact]
    public async Task NewHdWallet_OwnsReceiving0To19AndChange0To9_InBothForms()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        var owned = await ((IPsbtKeyProvider)service).GetOwnedScriptsAsync();

        Assert.Equal(60, owned.Count);
        Assert.Contains(PsbtTestFixtures.SegWit(ReceiveKey(env, 19)), owned.Keys);
        Assert.Contains(PsbtTestFixtures.Legacy(ReceiveKey(env, 0)), owned.Keys);
        Assert.Contains(PsbtTestFixtures.SegWit(env.Hd.DeriveChangeKey(9).PrivateKey), owned.Keys);
        Assert.Equal(env.ReceiveAddress(0), owned[PsbtTestFixtures.SegWit(ReceiveKey(env, 0))]);
    }

    [Fact]
    public async Task UsedAddressesPastTheMinimum_AreOwnedInNativeSegWit()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.Credit(env.ReceiveAddress(25), OneDgb);

        var owned = await ((IPsbtKeyProvider)service).GetOwnedScriptsAsync();

        Assert.Contains(PsbtTestFixtures.SegWit(ReceiveKey(env, 25)), owned.Keys);
        Assert.DoesNotContain(PsbtTestFixtures.Legacy(ReceiveKey(env, 25)), owned.Keys);
    }

    [Fact]
    public async Task ACoinFoundByDiscovery_IsOwned_WhenScriptsAreReadStraightAfterUnlock()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var walletId = service.ActiveWallet!.Id;
        env.Chain.Credit(env.ReceiveAddress(30), OneDgb);

        var reloaded = await env.ReloadAsync(walletId);
        var owned = await ((IPsbtKeyProvider)reloaded).GetOwnedScriptsAsync();

        Assert.Contains(PsbtTestFixtures.SegWit(ReceiveKey(env, 30)), owned.Keys);
    }

    [Fact]
    public async Task SlowDiscovery_DoesNotBlockReadingTheScripts()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        env.Chain.HistoryGate = new TaskCompletionSource().Task;
        service.AddressDiscoverySendWait = TimeSpan.FromMilliseconds(20);

        var owned = await ((IPsbtKeyProvider)service).GetOwnedScriptsAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(60, owned.Count);
    }

    [Fact]
    public async Task WifWallet_OwnsTheImportedKeysTwoAddresses()
    {
        var env = new Env();
        var service = env.NewService();
        var key = new Key();
        await service.CreateWalletFromWifAsync("Imported", key.GetWif(ChangeAddressTests.Network).ToString(), Pin);

        var owned = await ((IPsbtKeyProvider)service).GetOwnedScriptsAsync();

        Assert.Equal(2, owned.Count);
        Assert.Contains(PsbtTestFixtures.SegWit(key), owned.Keys);
        Assert.Contains(PsbtTestFixtures.Legacy(key), owned.Keys);
        Assert.True(((IPsbtKeyProvider)service).CanSign);
    }

    [Fact]
    public async Task WatchOnlyWallet_OwnsScripts_ButCantSign()
    {
        var env = new Env();
        var service = env.NewService();
        await service.CreateWatchOnlyWalletAsync("Watch", env.Hd.GetAccountExtPubKey().ToString(ChangeAddressTests.Network), Pin);
        var provider = (IPsbtKeyProvider)service;

        var owned = await provider.GetOwnedScriptsAsync();

        Assert.Equal(60, owned.Count);
        Assert.False(provider.CanSign);
        var psbt = SpendFrom(PsbtTestFixtures.Fund(PsbtTestFixtures.SegWit(ReceiveKey(env, 0)), OneDgb)).Build();
        Assert.Throws<InvalidOperationException>(() => provider.SignInputs(psbt, [0]));
    }

    [Fact]
    public async Task LockedWallet_CantSign()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        Assert.True(((IPsbtKeyProvider)service).CanSign);

        await service.LockWalletAsync();

        Assert.False(((IPsbtKeyProvider)service).CanSign);
    }

    [Fact]
    public async Task Network_IsTheActiveWalletsNetwork()
    {
        var env = new Env();
        var service = await CreateAsync(env);

        Assert.Equal(ChangeAddressTests.Network, ((IPsbtKeyProvider)service).Network);
    }

    // ─── Signing ───

    [Fact]
    public async Task SignInputs_SignsAWalletInput_AndLeavesTheOriginalUnchanged()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var psbt = SpendFrom(PsbtTestFixtures.Fund(PsbtTestFixtures.SegWit(ReceiveKey(env, 3)), OneDgb)).Build();
        var before = psbt.ToBase64();

        var signed = ((IPsbtKeyProvider)service).SignInputs(psbt, [0]);

        Assert.Single(signed.Inputs[0].PartialSigs);
        Assert.Equal(before, psbt.ToBase64());
    }

    [Fact]
    public async Task SignInputs_SignsAWifWalletsLegacyInput()
    {
        var env = new Env();
        var service = env.NewService();
        var key = new Key();
        await service.CreateWalletFromWifAsync("Imported", key.GetWif(ChangeAddressTests.Network).ToString(), Pin);
        var psbt = SpendFrom(PsbtTestFixtures.Fund(PsbtTestFixtures.Legacy(key), OneDgb)).Build();

        var signed = ((IPsbtKeyProvider)service).SignInputs(psbt, [0]);

        Assert.Single(signed.Inputs[0].PartialSigs);
    }

    [Fact]
    public async Task SignInputs_SkipsAListedIndexWhoseScriptIsNotOwned()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var mine = PsbtTestFixtures.Fund(PsbtTestFixtures.SegWit(ReceiveKey(env, 0)), OneDgb);
        var theirs = PsbtTestFixtures.Fund(PsbtTestFixtures.Stranger(), OneDgb);
        var psbt = SpendFrom(mine, theirs).Build();

        var signed = ((IPsbtKeyProvider)service).SignInputs(psbt, [0, 1]);

        Assert.Single(signed.Inputs[0].PartialSigs);
        Assert.Empty(signed.Inputs[1].PartialSigs);
    }

    [Fact]
    public async Task SignInputs_NeverSignsAMultisigInputContainingTheWalletsKey()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var redeem = PsbtTestFixtures.MultisigRedeem(ReceiveKey(env, 0), new Key());
        var coin = PsbtTestFixtures.Fund(redeem.WitHash.ScriptPubKey, OneDgb);
        var psbt = PsbtTestFixtures.Builder().AddInput(coin, witnessScript: redeem).Pay(OneDgb - 100_000).Build();

        var signed = ((IPsbtKeyProvider)service).SignInputs(psbt, [0]);

        Assert.Empty(signed.Inputs[0].PartialSigs);
    }

    [Fact]
    public async Task SignInputs_NeverSignsWrappedSegWitOnTheWalletsKey()
    {
        var env = new Env();
        var service = await CreateAsync(env);
        var key = ReceiveKey(env, 0);
        var coin = PsbtTestFixtures.Fund(PsbtTestFixtures.WrappedSegWit(key), OneDgb);
        var psbt = PsbtTestFixtures.Builder().AddInput(coin, redeemScript: key.PubKey.WitHash.ScriptPubKey).Pay(OneDgb - 100_000).Build();

        var signed = ((IPsbtKeyProvider)service).SignInputs(psbt, [0]);

        Assert.Empty(signed.Inputs[0].PartialSigs);
    }

    // ─── PIN check ───

    /// <summary>Keeps the PIN with the stored seed, so a wrong PIN fails to decrypt, unlike the pass-through crypto.</summary>
    private sealed class PinCheckingCrypto : LegacyCryptoBase
    {
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) =>
            Task.FromResult(Encoding.UTF8.GetBytes(pin + "|").Concat(plaintext).ToArray());

        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin)
        {
            var prefix = Encoding.UTF8.GetBytes(pin + "|");
            return Task.FromResult(ciphertext.AsSpan().StartsWith(prefix) ? ciphertext[prefix.Length..] : null);
        }
    }

    private static WalletService PinCheckingService(InMemoryStorage storage)
    {
        var service = new WalletService(new WalletKeyStore(storage), new PinCheckingCrypto(), new FakeChain(), new TransactionTracker(storage));
        service.SetNetworkMode("mainnet");
        return service;
    }

    [Fact]
    public async Task VerifyPin_CorrectPin_IsTrue_WrongPin_IsFalse()
    {
        var env = new Env();
        var service = PinCheckingService(env.Storage);
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);

        Assert.True(await service.VerifyPinAsync(wallet.Id, Pin));
        Assert.False(await service.VerifyPinAsync(wallet.Id, "000000"));
    }

    [Fact]
    public async Task VerifyPin_MissingWallet_IsFalse()
    {
        var env = new Env();
        var service = PinCheckingService(env.Storage);
        await service.CreateWalletAsync("Test", env.Words, Pin);

        Assert.False(await service.VerifyPinAsync("no-such-wallet", Pin));
    }

    [Fact]
    public async Task VerifyPin_LeavesTheSessionAndActiveWalletUnchanged()
    {
        var env = new Env();
        var service = PinCheckingService(env.Storage);
        var first = await service.CreateWalletAsync("First", env.Words, Pin);
        var second = await service.CreateWalletAsync("Second", string.Join(' ', new Mnemonic(Wordlist.English, WordCount.Twelve).Words), Pin);
        var sessionBefore = service.ActiveWallet;

        Assert.True(await service.VerifyPinAsync(first.Id, Pin));
        Assert.False(await service.VerifyPinAsync(second.Id, "000000"));

        Assert.Same(sessionBefore, service.ActiveWallet);
        Assert.Equal(second.Id, service.ActiveWallet!.Id);
        Assert.Equal(second.Id, await service.GetActiveWalletIdAsync());
        Assert.True(service.IsUnlocked);
    }
}

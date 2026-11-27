using System.Text.Json;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// specs/035 T035 (User Story 3, contracts/wallet-dd-key.md W2): the wallet exports its DigiDollar account key for DigiPay
/// with the first DD receive address, and only seed wallets have one.
/// </summary>
public class DigiDollarKeyExportTests
{
    private const string Pin = "123456";
    private const string Mnemonic = FakeSessionProvider.Mnemonic;

    private sealed class Env
    {
        public TestMemoryStorage Storage { get; } = new();
        public WalletKeyStore Keys { get; }
        public WalletService Service { get; }

        public Env(string network)
        {
            Keys = new WalletKeyStore(Storage);
            Service = new WalletService(Keys, new TestAesCrypto(), new MigrationStubBlockchain(), new TransactionTracker(Storage));
            Service.SetNetworkMode(network);
        }
    }

    private static string Encode(DigiDollarKeyDerivation hd) => hd.GetAddress(hd.Derive(DigiDollarChain.Receive, 0));

    [Theory]
    [InlineData("mainnet", "xpub")]
    [InlineData("testnet", "tpub")]
    public async Task SeedWallet_ReturnsTheAccountKey_AndItsFirstAddress(string network, string prefix)
    {
        var env = new Env(network);
        await env.Service.CreateWalletAsync("Shop", Mnemonic, Pin);

        var result = await env.Service.GetDigiDollarAccountKeyAsync();

        Assert.NotNull(result);
        var (key, firstAddress) = result.Value;
        Assert.StartsWith(prefix, key);

        // DigiPay's side: the pasted key parses as an account key for this network and gives the same first address.
        var parsed = DigiDollarAccountKey.Parse(key, network);
        Assert.True(parsed.Ok, parsed.ToString());
        Assert.Equal(firstAddress, parsed.Key!.Address(0));

        // The wallet's own side: the address the wallet scans and hands out for DD at 0/0.
        Assert.Equal(Encode(new DigiDollarKeyDerivation(new Mnemonic(Mnemonic), network: network)), firstAddress);
    }

    [Fact]
    public async Task MainnetAndTestnet_GiveDifferentKeys_EachRefusedOnTheOtherNetwork()
    {
        var main = new Env("mainnet");
        await main.Service.CreateWalletAsync("Shop", Mnemonic, Pin);
        var test = new Env("testnet");
        await test.Service.CreateWalletAsync("Shop", Mnemonic, Pin);

        var mainKey = (await main.Service.GetDigiDollarAccountKeyAsync())!.Value;
        var testKey = (await test.Service.GetDigiDollarAccountKeyAsync())!.Value;

        Assert.NotEqual(mainKey.Key, testKey.Key);
        Assert.NotEqual(mainKey.FirstAddress, testKey.FirstAddress);
        Assert.Equal(DigiDollarAccountKey.WrongNetwork, DigiDollarAccountKey.Parse(mainKey.Key, "testnet").Reason);
        Assert.Equal(DigiDollarAccountKey.WrongNetwork, DigiDollarAccountKey.Parse(testKey.Key, "mainnet").Reason);
    }

    [Fact]
    public async Task TheExportedKey_IsPublicOnly()
    {
        var env = new Env("mainnet");
        await env.Service.CreateWalletAsync("Shop", Mnemonic, Pin);

        var key = (await env.Service.GetDigiDollarAccountKeyAsync())!.Value.Key;

        Assert.DoesNotContain("xprv", key);
        Assert.NotEqual(DigiDollarAccountKey.PrivateKey, DigiDollarAccountKey.Parse(key, "mainnet").Reason);
        Assert.Equal(3, ExtPubKey.Parse(key, DigiByteNetwork.Mainnet).Depth); // m/86'/20'/0'
    }

    [Fact]
    public async Task UnlockedAgain_GivesTheSameKey()
    {
        var env = new Env("testnet");
        var wallet = await env.Service.CreateWalletAsync("Shop", Mnemonic, Pin);
        var first = await env.Service.GetDigiDollarAccountKeyAsync();

        await env.Service.LockWalletAsync();
        Assert.True(await env.Service.UnlockWalletAsync(wallet.Id, Pin));

        Assert.Equal(first, await env.Service.GetDigiDollarAccountKeyAsync());
    }

    // ---------- wallets that have no DigiDollar account key ----------

    [Fact]
    public async Task WatchOnlyWallet_ReturnsNull()
    {
        var env = new Env("mainnet");
        var xpub = new HdKeyDerivation(new Mnemonic(Mnemonic), network: DigiByteNetwork.Mainnet)
            .GetAccountExtPubKey().ToString(DigiByteNetwork.Mainnet);
        await env.Service.CreateWatchOnlyWalletAsync("Watch", xpub, Pin);

        Assert.Null(await env.Service.GetDigiDollarAccountKeyAsync());
    }

    [Fact]
    public async Task MultisigWallet_ReturnsNull()
    {
        var env = new Env("mainnet");
        var wallet = await env.Service.CreateWalletAsync("Shared", Mnemonic, Pin);
        // A wallet of the multisig kind keeps a seed for its own signing key, but it is not a DigiDollar wallet (FR-030).
        wallet.WalletType = "multisig";
        await env.Keys.SaveWalletInfoAsync(wallet.Id, JsonSerializer.Serialize(wallet));
        await env.Service.LockWalletAsync();
        Assert.True(await env.Service.UnlockWalletAsync(wallet.Id, Pin));

        Assert.Null(await env.Service.GetDigiDollarAccountKeyAsync());
    }

    [Fact]
    public async Task PrivateKeyWallet_ReturnsNull()
    {
        var env = new Env("mainnet");
        var wif = new Key().GetWif(DigiByteNetwork.Mainnet).ToString();
        await env.Service.CreateWalletFromWifAsync("Paper", wif, Pin);

        Assert.Null(await env.Service.GetDigiDollarAccountKeyAsync());
    }

    [Fact]
    public async Task Regtest_ReturnsNull()
    {
        var env = new Env("regtest");
        await env.Service.CreateWalletAsync("Dev", Mnemonic, Pin);

        Assert.Null(await env.Service.GetDigiDollarAccountKeyAsync());
    }

    [Fact]
    public async Task LockedWallet_Throws()
    {
        var env = new Env("mainnet");
        await env.Service.CreateWalletAsync("Shop", Mnemonic, Pin);
        await env.Service.LockWalletAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.GetDigiDollarAccountKeyAsync());
    }
}

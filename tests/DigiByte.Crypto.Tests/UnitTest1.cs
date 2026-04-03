using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class MnemonicTests
{
    [Fact]
    public void Generate_Returns24Words()
    {
        var mnemonic = MnemonicGenerator.Generate();
        var words = mnemonic.ToString().Split(' ');
        Assert.Equal(24, words.Length);
    }

    [Fact]
    public void Generate_ReturnsDifferentMnemonicsEachTime()
    {
        var m1 = MnemonicGenerator.Generate();
        var m2 = MnemonicGenerator.Generate();
        Assert.NotEqual(m1.ToString(), m2.ToString());
    }

    [Fact]
    public void IsValid_ReturnsTrueForValidMnemonic()
    {
        var mnemonic = MnemonicGenerator.Generate();
        Assert.True(MnemonicGenerator.IsValid(mnemonic.ToString()));
    }

    [Fact]
    public void IsValid_ReturnsFalseForGarbage()
    {
        Assert.False(MnemonicGenerator.IsValid("not a valid mnemonic phrase at all"));
    }

    [Fact]
    public void FromWords_RoundTrips()
    {
        var original = MnemonicGenerator.Generate();
        var recovered = MnemonicGenerator.FromWords(original.ToString());
        Assert.Equal(original.ToString(), recovered.ToString());
    }
}

public class HdKeyDerivationTests
{
    private readonly Mnemonic _testMnemonic = MnemonicGenerator.Generate();

    [Fact]
    public void DeriveReceivingKey_ReturnsDeterministicKeys()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var key1 = hd.DeriveReceivingKey(0);
        var key2 = hd.DeriveReceivingKey(0);
        Assert.Equal(key1.PrivateKey, key2.PrivateKey);
    }

    [Fact]
    public void DeriveReceivingKey_DifferentIndicesProduceDifferentKeys()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var key0 = hd.DeriveReceivingKey(0);
        var key1 = hd.DeriveReceivingKey(1);
        Assert.NotEqual(key0.PrivateKey, key1.PrivateKey);
    }

    [Fact]
    public void GetAddress_ReturnsSegwitAddress()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var key = hd.DeriveReceivingKey(0);
        var address = hd.GetAddress(key);
        Assert.StartsWith("dgb1", address.ToString());
    }

    [Fact]
    public void GetAddress_Legacy_StartsWithD()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var key = hd.DeriveReceivingKey(0);
        var address = hd.GetAddress(key, ScriptPubKeyType.Legacy);
        Assert.StartsWith("D", address.ToString());
    }

    [Fact]
    public void DeriveReceivingAddresses_ReturnsBatch()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var addresses = hd.DeriveReceivingAddresses(5);
        Assert.Equal(5, addresses.Count);
        Assert.All(addresses, a => Assert.StartsWith("dgb1", a.Address.ToString()));
    }

    [Fact]
    public void DeriveChangeKey_DiffersFromReceiving()
    {
        var hd = new HdKeyDerivation(_testMnemonic, network: DigiByteNetwork.Mainnet);
        var receiving = hd.DeriveReceivingKey(0);
        var change = hd.DeriveChangeKey(0);
        Assert.NotEqual(receiving.PrivateKey, change.PrivateKey);
    }
}

public class AddressValidatorTests
{
    [Fact]
    public void IsValid_AcceptsGeneratedSegwitAddress()
    {
        var mnemonic = MnemonicGenerator.Generate();
        var hd = new HdKeyDerivation(mnemonic, network: DigiByteNetwork.Mainnet);
        var key = hd.DeriveReceivingKey(0);
        var address = hd.GetAddress(key);
        Assert.True(AddressValidator.IsValid(address.ToString()));
    }

    [Fact]
    public void IsValid_RejectsBitcoinAddress()
    {
        // A valid Bitcoin address should not be valid on DigiByte network
        Assert.False(AddressValidator.IsValid("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"));
    }

    [Fact]
    public void IsValid_RejectsGarbage()
    {
        Assert.False(AddressValidator.IsValid("notanaddress"));
    }

    [Fact]
    public void GetAddressType_DetectsSegwit()
    {
        var mnemonic = MnemonicGenerator.Generate();
        var hd = new HdKeyDerivation(mnemonic, network: DigiByteNetwork.Mainnet);
        var key = hd.DeriveReceivingKey(0);
        var address = hd.GetAddress(key);
        Assert.Equal(AddressType.SegWit, AddressValidator.GetAddressType(address.ToString()));
    }
}

// EncryptionService tests removed — encryption now happens via browser SubtleCrypto (ICryptoService)
// and can't be tested in a unit test context. Covered by integration/E2E tests instead.

public class DigiIdTests
{
    [Fact]
    public void ParseUri_ValidUri_ReturnsRequest()
    {
        var uri = "digiid://example.com/auth?x=abc123";
        var request = DigiByte.Crypto.DigiId.DigiIdService.ParseUri(uri);
        Assert.NotNull(request);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal("abc123", request.Nonce);
        Assert.False(request.IsUnsecure);
        Assert.Contains("example.com/auth", request.CallbackUrl);
    }

    [Fact]
    public void ParseUri_UnsecureUri_DetectsFlag()
    {
        var uri = "digiid://example.com/callback?x=nonce123&u=1";
        var request = DigiByte.Crypto.DigiId.DigiIdService.ParseUri(uri);
        Assert.NotNull(request);
        Assert.True(request.IsUnsecure);
        Assert.StartsWith("http://", request.CallbackUrl);
    }

    [Fact]
    public void ParseUri_InvalidUri_ReturnsNull()
    {
        Assert.Null(DigiByte.Crypto.DigiId.DigiIdService.ParseUri("https://example.com"));
        Assert.Null(DigiByte.Crypto.DigiId.DigiIdService.ParseUri("digiid://example.com"));
    }

    [Fact]
    public void DeriveSiteIndex_DeterministicForSameDomain()
    {
        var i1 = DigiByte.Crypto.DigiId.DigiIdService.DeriveSiteIndex("example.com");
        var i2 = DigiByte.Crypto.DigiId.DigiIdService.DeriveSiteIndex("example.com");
        Assert.Equal(i1, i2);
    }

    [Fact]
    public void DeriveSiteIndex_DifferentForDifferentDomains()
    {
        var i1 = DigiByte.Crypto.DigiId.DigiIdService.DeriveSiteIndex("example.com");
        var i2 = DigiByte.Crypto.DigiId.DigiIdService.DeriveSiteIndex("another.com");
        Assert.NotEqual(i1, i2);
    }

    [Fact]
    public void Sign_ProducesValidResponse()
    {
        var uri = "digiid://example.com/auth?x=testnonce";
        var request = DigiByte.Crypto.DigiId.DigiIdService.ParseUri(uri)!;
        var key = new Key();
        var response = DigiByte.Crypto.DigiId.DigiIdService.Sign(request, key);

        Assert.NotEmpty(response.Address);
        Assert.Equal(uri, response.Uri);
        Assert.NotEmpty(response.Signature);
        Assert.StartsWith("D", response.Address); // Legacy DGB address
    }
}

public class PaymentRequestTests
{
    [Fact]
    public void ToUri_BasicAddress()
    {
        var req = new DigiByte.Wallet.Models.PaymentRequest
        {
            Id = "test",
            Address = "dgb1qtest"
        };
        Assert.Equal("digibyte:dgb1qtest", req.ToUri());
    }

    [Fact]
    public void ToUri_WithAmountAndLabel()
    {
        var req = new DigiByte.Wallet.Models.PaymentRequest
        {
            Id = "test",
            Address = "dgb1qtest",
            AmountDgb = 10.5m,
            Label = "Invoice 123"
        };
        var uri = req.ToUri();
        Assert.Contains("amount=10.50000000", uri);
        Assert.Contains("label=Invoice%20123", uri);
    }

    [Fact]
    public void FromUri_RoundTrips()
    {
        var original = new DigiByte.Wallet.Models.PaymentRequest
        {
            Id = "test",
            Address = "dgb1qtest",
            AmountDgb = 5.25m,
            Label = "Coffee",
            Message = "Thanks!"
        };
        var uri = original.ToUri();
        var parsed = DigiByte.Wallet.Models.PaymentRequest.FromUri(uri);

        Assert.NotNull(parsed);
        Assert.Equal("dgb1qtest", parsed.Address);
        Assert.Equal(5.25m, parsed.AmountDgb);
        Assert.Equal("Coffee", parsed.Label);
        Assert.Equal("Thanks!", parsed.Message);
    }

    [Fact]
    public void FromUri_InvalidScheme_ReturnsNull()
    {
        Assert.Null(DigiByte.Wallet.Models.PaymentRequest.FromUri("bitcoin:1abc"));
    }
}

public class UtxoSelectorTests
{
    private static Utxo MakeUtxo(long satoshis)
    {
        return new Utxo
        {
            TransactionId = uint256.One,
            OutputIndex = 0,
            Amount = Money.Satoshis(satoshis),
            ScriptPubKey = Script.Empty,
            PrivateKey = new Key()
        };
    }

    [Fact]
    public void SelectLargestFirst_PicksLargestUtxo()
    {
        var utxos = new[] { MakeUtxo(100), MakeUtxo(500), MakeUtxo(200) };
        var selected = UtxoSelector.SelectLargestFirst(utxos, Money.Satoshis(400));

        Assert.Single(selected);
        Assert.Equal(Money.Satoshis(500), selected[0].Amount);
    }

    [Fact]
    public void SelectLargestFirst_ThrowsOnInsufficientFunds()
    {
        var utxos = new[] { MakeUtxo(100), MakeUtxo(200) };
        Assert.Throws<InsufficientFundsException>(
            () => UtxoSelector.SelectLargestFirst(utxos, Money.Satoshis(500)));
    }

    [Fact]
    public void SelectClosestMatch_FindsExactMatch()
    {
        var utxos = new[] { MakeUtxo(100), MakeUtxo(500), MakeUtxo(300) };
        var selected = UtxoSelector.SelectClosestMatch(utxos, Money.Satoshis(300));

        Assert.Single(selected);
        Assert.Equal(Money.Satoshis(300), selected[0].Amount);
    }
}

using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

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

public class PrivateKeyImporterTests
{
    [Fact]
    public void IsValidWif_AcceptsGeneratedKey()
    {
        var network = DigiByteNetwork.Regtest;
        var key = new Key();
        var wif = key.GetWif(network).ToString();
        Assert.True(PrivateKeyImporter.IsValidWif(wif, network));
    }

    [Fact]
    public void IsValidWif_RejectsGarbage()
    {
        Assert.False(PrivateKeyImporter.IsValidWif("notakey"));
        Assert.False(PrivateKeyImporter.IsValidWif(""));
        Assert.False(PrivateKeyImporter.IsValidWif(null!));
    }

    [Fact]
    public void ParseWif_RoundTrips()
    {
        var network = DigiByteNetwork.Regtest;
        var originalKey = new Key();
        var wif = originalKey.GetWif(network).ToString();

        var parsed = PrivateKeyImporter.ParseWif(wif, network);
        Assert.Equal(originalKey.ToBytes(), parsed.ToBytes());
    }

    [Fact]
    public void GetAddress_ReturnsValidAddress()
    {
        var network = DigiByteNetwork.Regtest;
        var key = new Key();
        var wif = key.GetWif(network).ToString();

        var address = PrivateKeyImporter.GetAddress(wif, network, ScriptPubKeyType.Segwit);
        Assert.StartsWith("dgbrt1", address);
    }

    [Fact]
    public void GetAddress_Legacy_StartsWithCorrectPrefix()
    {
        var network = DigiByteNetwork.Mainnet;
        var key = new Key();
        var wif = key.GetWif(network).ToString();

        var address = PrivateKeyImporter.GetAddress(wif, network, ScriptPubKeyType.Legacy);
        Assert.StartsWith("D", address);
    }

    [Fact]
    public void GetAllAddresses_ReturnsBothFormats()
    {
        var network = DigiByteNetwork.Regtest;
        var key = new Key();
        var wif = key.GetWif(network).ToString();

        var addresses = PrivateKeyImporter.GetAllAddresses(wif, network);
        Assert.Contains("legacy", addresses.Keys);
        Assert.Contains("segwit", addresses.Keys);
        Assert.NotEqual(addresses["legacy"], addresses["segwit"]);
    }

    [Fact]
    public void DetectNetwork_FindsCorrectNetwork()
    {
        var key = new Key();
        var wif = key.GetWif(DigiByteNetwork.Mainnet).ToString();
        var detected = PrivateKeyImporter.DetectNetwork(wif);
        Assert.Equal(DigiByteNetwork.Mainnet, detected);
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

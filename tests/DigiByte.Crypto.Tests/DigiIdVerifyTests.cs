using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class DigiIdVerifyTests
{
    [Fact]
    public void Verify_accepts_a_roundtrip_signature()
    {
        var key = new Key();
        var request = DigiIdService.ParseUri("digiid://example.com/callback?x=abc123");
        Assert.NotNull(request);

        var response = DigiIdService.Sign(request!, key);

        Assert.True(DigiIdService.Verify(response.Address, response.Uri, response.Signature));
    }

    [Fact]
    public void Verify_rejects_tampered_address()
    {
        var key = new Key();
        var request = DigiIdService.ParseUri("digiid://example.com/callback?x=abc123")!;
        var response = DigiIdService.Sign(request, key);

        // Address from a different key.
        var otherAddress = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString();

        Assert.False(DigiIdService.Verify(otherAddress, response.Uri, response.Signature));
    }

    [Fact]
    public void Verify_rejects_tampered_uri()
    {
        var key = new Key();
        var request = DigiIdService.ParseUri("digiid://example.com/callback?x=abc123")!;
        var response = DigiIdService.Sign(request, key);

        Assert.False(DigiIdService.Verify(response.Address,
            "digiid://example.com/callback?x=DIFFERENT",
            response.Signature));
    }

    [Fact]
    public void Verify_rejects_malformed_signature()
    {
        var key = new Key();
        var request = DigiIdService.ParseUri("digiid://example.com/callback?x=abc123")!;
        var response = DigiIdService.Sign(request, key);

        Assert.False(DigiIdService.Verify(response.Address, response.Uri, "not-base64!"));
        Assert.False(DigiIdService.Verify(response.Address, response.Uri, ""));
    }
}

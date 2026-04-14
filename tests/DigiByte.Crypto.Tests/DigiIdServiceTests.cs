using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// Tests for DigiIdService — the cryptographic backbone of the Digi-ID
/// passwordless authentication feature (Feature #2).
/// </summary>
public class DigiIdServiceTests
{
    // ─── ParseUri ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseUri_ValidSecureUri_ReturnsRequest()
    {
        var uri = "digiid://example.com/callback?x=abc123nonce";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
        Assert.Equal(uri, result.OriginalUri);
        Assert.Equal("example.com", result.Domain);
        Assert.Equal("abc123nonce", result.Nonce);
        Assert.False(result.IsUnsecure);
        Assert.StartsWith("https://", result.CallbackUrl);
    }

    [Fact]
    public void ParseUri_UnsecureFlag_SetsHttpCallback()
    {
        var uri = "digiid://example.com/callback?x=nonce123&u=1";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
        Assert.True(result.IsUnsecure);
        Assert.StartsWith("http://", result.CallbackUrl);
    }

    [Fact]
    public void ParseUri_UnsecureFlagZero_TreatedAsSecure()
    {
        var uri = "digiid://example.com/callback?x=nonce&u=0";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
        Assert.False(result.IsUnsecure);
        Assert.StartsWith("https://", result.CallbackUrl);
    }

    [Fact]
    public void ParseUri_MissingNonce_ReturnsNull()
    {
        var uri = "digiid://example.com/callback";

        var result = DigiIdService.ParseUri(uri);

        Assert.Null(result);
    }

    [Fact]
    public void ParseUri_NonDigiIdScheme_ReturnsNull()
    {
        var uri = "https://example.com/callback?x=nonce";

        var result = DigiIdService.ParseUri(uri);

        Assert.Null(result);
    }

    [Fact]
    public void ParseUri_EmptyString_ReturnsNull()
    {
        var result = DigiIdService.ParseUri("");

        Assert.Null(result);
    }

    [Fact]
    public void ParseUri_CaseInsensitivePrefix_Parses()
    {
        var uri = "DiGiId://example.com/callback?x=nonce";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
    }

    [Fact]
    public void ParseUri_DeepPath_PreservedInCallback()
    {
        var uri = "digiid://example.com/auth/digiid/callback?x=nonce";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
        Assert.Contains("/auth/digiid/callback", result.CallbackUrl);
    }

    [Fact]
    public void ParseUri_CallbackUrl_ContainsNonceQuery()
    {
        var uri = "digiid://example.com/callback?x=abc123";

        var result = DigiIdService.ParseUri(uri);

        Assert.NotNull(result);
        // The callback URL returned to the server must include the nonce
        Assert.Contains("x=abc123", result.CallbackUrl);
    }

    // ─── DeriveSiteIndex ──────────────────────────────────────────────────────

    [Fact]
    public void DeriveSiteIndex_SameDomain_ReturnsSameIndex()
    {
        var index1 = DigiIdService.DeriveSiteIndex("example.com");
        var index2 = DigiIdService.DeriveSiteIndex("example.com");

        Assert.Equal(index1, index2);
    }

    [Fact]
    public void DeriveSiteIndex_DifferentDomains_ReturnDifferentIndices()
    {
        var index1 = DigiIdService.DeriveSiteIndex("example.com");
        var index2 = DigiIdService.DeriveSiteIndex("another.com");

        Assert.NotEqual(index1, index2);
    }

    [Fact]
    public void DeriveSiteIndex_CaseInsensitive_SameResult()
    {
        var lower = DigiIdService.DeriveSiteIndex("EXAMPLE.COM");
        var upper = DigiIdService.DeriveSiteIndex("example.com");

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void DeriveSiteIndex_AlwaysNonNegative()
    {
        // The index is used as a BIP32 non-hardened child index
        var domains = new[] { "example.com", "test.net", "digiid.io", "a.b.c.d.e" };
        foreach (var domain in domains)
        {
            var index = DigiIdService.DeriveSiteIndex(domain);
            Assert.True(index >= 0, $"Expected non-negative index for '{domain}', got {index}");
        }
    }

    [Fact]
    public void DeriveSiteIndex_MaxValueFits31Bits()
    {
        // BIP32 non-hardened path requires index < 0x80000000: guaranteed by the
        // & 0x7FFFFFFF mask in DeriveSiteIndex, so result must be non-negative.
        var index = DigiIdService.DeriveSiteIndex("any-domain.example");
        Assert.True(index >= 0, $"Index {index} exceeds 31-bit BIP32 limit");
    }

    [Fact]
    public void DeriveSiteIndex_SimilarDomains_GiveDifferentIndices()
    {
        // Privacy: site-specific keys should be unique per subdomain too
        var root = DigiIdService.DeriveSiteIndex("example.com");
        var sub = DigiIdService.DeriveSiteIndex("auth.example.com");

        Assert.NotEqual(root, sub);
    }

    // ─── Sign ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sign_ReturnsAddress_And_Signature()
    {
        var request = MakeRequest("example.com");
        var key = new Key();

        var response = DigiIdService.Sign(request, key);

        Assert.NotNull(response);
        Assert.NotNull(response.Address);
        Assert.NotNull(response.Signature);
        Assert.NotEmpty(response.Address);
        Assert.NotEmpty(response.Signature);
    }

    [Fact]
    public void Sign_SignatureIs65BytesWhenDecoded()
    {
        var request = MakeRequest("example.com");
        var key = new Key();

        var response = DigiIdService.Sign(request, key);

        var sigBytes = Convert.FromBase64String(response.Signature);
        Assert.Equal(65, sigBytes.Length);
    }

    [Fact]
    public void Sign_FirstByteIndicatesCompressedKey()
    {
        // Compact recoverable signatures for compressed pubkeys:
        // header byte = 27 + recovery_id (0..3) + 4 (compressed flag)
        // So valid range is 31..34
        var request = MakeRequest("example.com");
        var key = new Key();

        var response = DigiIdService.Sign(request, key);

        var sigBytes = Convert.FromBase64String(response.Signature);
        Assert.InRange(sigBytes[0], 31, 34);
    }

    [Fact]
    public void Sign_OriginalUriEmbeddedInResponse()
    {
        var uri = "digiid://example.com/auth?x=testnonce";
        var request = DigiIdService.ParseUri(uri)!;
        var key = new Key();

        var response = DigiIdService.Sign(request, key);

        // The signed URI must match exactly so the server can verify
        Assert.Equal(uri, response.Uri);
    }

    [Fact]
    public void Sign_AddressIsLegacyFormat()
    {
        // DigiId spec requires a Legacy (P2PKH) address starting with D (mainnet)
        var request = MakeRequest("example.com");
        var key = new Key();

        var response = DigiIdService.Sign(request, key);

        Assert.StartsWith("D", response.Address);
    }

    [Fact]
    public void Sign_DifferentKeysProduceDifferentAddresses()
    {
        var request = MakeRequest("example.com");
        var key1 = new Key();
        var key2 = new Key();

        var resp1 = DigiIdService.Sign(request, key1);
        var resp2 = DigiIdService.Sign(request, key2);

        Assert.NotEqual(resp1.Address, resp2.Address);
    }

    [Fact]
    public void Sign_SameDomainSameKey_ProducesSameAddress()
    {
        // Determinism: identical key + identical request → identical result
        var request1 = MakeRequest("example.com", "nonce1");
        var request2 = MakeRequest("example.com", "nonce2");
        var key = new Key();

        var resp1 = DigiIdService.Sign(request1, key);
        var resp2 = DigiIdService.Sign(request2, key);

        // Address comes only from the key, not from the nonce
        Assert.Equal(resp1.Address, resp2.Address);
    }

    [Fact]
    public void Sign_SignaturesAreDifferentPerNonce()
    {
        // Each unique URI produces a unique signature (nonce prevents replay)
        var key = new Key();
        var req1 = MakeRequest("example.com", "nonce_A");
        var req2 = MakeRequest("example.com", "nonce_B");

        var resp1 = DigiIdService.Sign(req1, key);
        var resp2 = DigiIdService.Sign(req2, key);

        Assert.NotEqual(resp1.Signature, resp2.Signature);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DigiIdRequest MakeRequest(string domain, string nonce = "testnonce")
    {
        var uri = $"digiid://{domain}/callback?x={nonce}";
        return DigiIdService.ParseUri(uri)!;
    }
}

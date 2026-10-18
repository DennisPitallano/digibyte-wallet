using DigiByte.Crypto.Hashing;

namespace DigiByte.Crypto.Tests;

/// <summary>HKDF-SHA256 (RFC 5869), used to derive the DigiDollar storage key from the unlocked seed.</summary>
public class HkdfTests
{
    // RFC 5869 Appendix A.1 (SHA-256, basic test case); we use the first 32 bytes of OKM.
    [Fact]
    public void Matches_Rfc5869_TestCase1()
    {
        var ikm = Enumerable.Repeat((byte)0x0b, 22).ToArray();
        var salt = Convert.FromHexString("000102030405060708090a0b0c");
        var info = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9");

        var okm = Hkdf.DeriveSha256(ikm, salt, info, 32);

        Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf",
            Convert.ToHexString(okm).ToLowerInvariant());
    }

    // RFC 5869 Appendix A.3: zero-length salt and info.
    [Fact]
    public void Matches_Rfc5869_TestCase3_EmptySaltAndInfo()
    {
        var ikm = Enumerable.Repeat((byte)0x0b, 22).ToArray();

        var okm = Hkdf.DeriveSha256(ikm, [], [], 32);

        Assert.Equal("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d",
            Convert.ToHexString(okm).ToLowerInvariant());
    }

    [Fact]
    public void DifferentInfo_GivesDifferentKeys()
    {
        var ikm = new byte[32];
        Assert.NotEqual(
            Hkdf.DeriveSha256(ikm, [], "a"u8.ToArray(), 32),
            Hkdf.DeriveSha256(ikm, [], "b"u8.ToArray(), 32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(33)]
    public void Rejects_UnsupportedLength(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Hkdf.DeriveSha256(new byte[32], [], [], length));

    [Fact]
    public void Rejects_EmptyInputKeyMaterial() =>
        Assert.Throws<ArgumentException>(() => Hkdf.DeriveSha256([], [], [], 32));
}

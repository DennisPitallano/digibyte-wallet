using DigiByte.P2P.Shared.Sponsors;

namespace DigiByte.Wallet.Tests;

public class SponsorRulesTests
{
    // ── name ──
    [Theory]
    [InlineData("Alice")]
    [InlineData("  Bob  ")]
    [InlineData("A")]
    public void Name_Valid(string name) => Assert.True(SponsorRules.TryNormalizeName(name, out var n) && n.Length >= 1);

    [Fact]
    public void Name_IsTrimmed()
    {
        Assert.True(SponsorRules.TryNormalizeName("  Bob  ", out var n));
        Assert.Equal("Bob", n);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0007name")]
    [InlineData("line\nbreak")]
    public void Name_Invalid(string? name) => Assert.False(SponsorRules.TryNormalizeName(name, out _));

    [Fact]
    public void Name_Boundary60Ok_61Rejected()
    {
        Assert.True(SponsorRules.TryNormalizeName(new string('a', 60), out _));
        Assert.False(SponsorRules.TryNormalizeName(new string('a', 61), out _));
    }

    [Fact]
    public void Name_MarkupIsKeptAsPlainText_NotRejected()
    {
        Assert.True(SponsorRules.TryNormalizeName("<script>alert(1)</script>", out var n));
        Assert.Equal("<script>alert(1)</script>", n);
    }

    // ── link ──
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("https://sub.example.org/a/b")]
    public void Link_ValidHttps(string link) => Assert.True(SponsorRules.IsValidLink(link));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user@example.com")]
    [InlineData("https://user:pw@example.com")]
    [InlineData("example.com")]
    [InlineData("https://exa mple.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Link_Invalid(string? link) => Assert.False(SponsorRules.IsValidLink(link));

    [Fact]
    public void Link_Over200Characters_Rejected()
    {
        var ok = "https://example.com/" + new string('a', 200 - "https://example.com/".Length);
        Assert.Equal(200, ok.Length);
        Assert.True(SponsorRules.IsValidLink(ok));
        Assert.False(SponsorRules.IsValidLink(ok + "a"));
    }

    // ── transaction ID ──
    [Fact]
    public void TxId_64Hex_Valid_AnyCase()
    {
        Assert.True(SponsorRules.IsValidTxId(new string('a', 64)));
        Assert.True(SponsorRules.IsValidTxId(new string('F', 64)));
        Assert.True(SponsorRules.IsValidTxId("0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789ABCDEF"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void TxId_Invalid_Short(string? txid) => Assert.False(SponsorRules.IsValidTxId(txid));

    [Fact]
    public void TxId_Invalid_LengthAndCharset()
    {
        Assert.False(SponsorRules.IsValidTxId(new string('a', 63)));
        Assert.False(SponsorRules.IsValidTxId(new string('a', 65)));
        Assert.False(SponsorRules.IsValidTxId(new string('g', 64)));
        Assert.False(SponsorRules.IsValidTxId(" " + new string('a', 63)));
    }

    [Fact]
    public void TxId_BranchSuffix_Is16LowercaseHex()
    {
        var suffix = SponsorRules.BranchSuffix("ABCDEF0123456789" + new string('0', 48));
        Assert.Equal("abcdef0123456789", suffix);
    }

    // ── list image name ──
    [Theory]
    [InlineData("a1b2c3d4e5f6.webp")]
    [InlineData("alice.png")]
    [InlineData("alice-2.jpg")]
    [InlineData("alice_2.jpeg")]
    public void ListImageName_Valid(string name) => Assert.True(SponsorRules.IsValidListImageName(name));

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("dir/evil.png")]
    [InlineData("dir\\evil.png")]
    [InlineData("UPPER.png")]
    [InlineData("alice.svg")]
    [InlineData("alice.gif")]
    [InlineData("alice")]
    [InlineData(".hidden.png")]
    [InlineData("")]
    [InlineData(null)]
    public void ListImageName_Invalid(string? name) => Assert.False(SponsorRules.IsValidListImageName(name));

    // ── image signature ──
    [Fact]
    public void ImageType_Detects_Png_Jpeg_WebP()
    {
        Assert.Equal(SponsorImageType.Png, SponsorRules.DetectImageType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal(SponsorImageType.Jpeg, SponsorRules.DetectImageType([0xFF, 0xD8, 0xFF, 0xE0, 0]));
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        Assert.Equal(SponsorImageType.WebP, SponsorRules.DetectImageType(webp));
    }

    [Fact]
    public void ImageType_Rejects_Others()
    {
        Assert.Equal(SponsorImageType.None, SponsorRules.DetectImageType([]));
        Assert.Equal(SponsorImageType.None, SponsorRules.DetectImageType("<svg></svg>"u8.ToArray()));
        Assert.Equal(SponsorImageType.None, SponsorRules.DetectImageType("GIF89a"u8.ToArray()));
        var riffWave = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 2, 3, 4, (byte)'W', (byte)'A', (byte)'V', (byte)'E' };
        Assert.Equal(SponsorImageType.None, SponsorRules.DetectImageType(riffWave));
    }

    [Theory]
    [InlineData(SponsorImageType.Png, "png")]
    [InlineData(SponsorImageType.Jpeg, "jpg")]
    [InlineData(SponsorImageType.WebP, "webp")]
    public void ImageExtension_ByType(SponsorImageType type, string ext) => Assert.Equal(ext, SponsorRules.ImageExtension(type));

    [Fact]
    public void ImageSize_Limit200KB()
    {
        Assert.True(SponsorRules.IsImageSizeOk(200 * 1024));
        Assert.False(SponsorRules.IsImageSizeOk(200 * 1024 + 1));
        Assert.False(SponsorRules.IsImageSizeOk(0));
    }

    // ── entry id ──
    [Fact]
    public void EntryId_StableForIdenticalContent_CaseInsensitiveName()
    {
        var a = SponsorRules.ComputeEntryId("Alice", "https://a.example", [1, 2, 3]);
        var b = SponsorRules.ComputeEntryId("alice", "https://a.example", [1, 2, 3]);
        Assert.Equal(a, b);
        Assert.Equal(12, a.Length);
        Assert.Matches("^[a-f0-9]{12}$", a);
    }

    [Fact]
    public void EntryId_MatchesTheSponsorsBranchScripts()
    {
        // Same vector as tests/test_sponsors.py on the sponsors branch (Python): the id must be computed identically everywhere.
        Assert.Equal("861f32e0e299", SponsorRules.ComputeEntryId("Alice", "https://a.example", [1, 2, 3]));
    }

    [Fact]
    public void EntryId_DiffersWhenNameLinkOrImageDiffer()
    {
        var baseId = SponsorRules.ComputeEntryId("Alice", "https://a.example", [1, 2, 3]);
        Assert.NotEqual(baseId, SponsorRules.ComputeEntryId("Alicia", "https://a.example", [1, 2, 3]));
        Assert.NotEqual(baseId, SponsorRules.ComputeEntryId("Alice", "https://b.example", [1, 2, 3]));
        Assert.NotEqual(baseId, SponsorRules.ComputeEntryId("Alice", "https://a.example", [9]));
        Assert.NotEqual(baseId, SponsorRules.ComputeEntryId("Alice", null, [1, 2, 3]));
    }
}

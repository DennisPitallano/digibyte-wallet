using DigiByte.Wallet.Storage;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

public class EnvelopeCodecTests
{
    private static readonly byte[] Salt = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    private static readonly byte[] Nonce = Enumerable.Range(50, 12).Select(i => (byte)i).ToArray();
    private static readonly byte[] Cipher = Enumerable.Range(0, 40).Select(i => (byte)(i * 3)).ToArray();

    private static string Format(KdfParameters? p = null, CredentialKind kind = CredentialKind.Pin) =>
        EnvelopeCodec.Format(p ?? KdfProfile.Standard, kind, Salt, Nonce, Cipher);

    [Theory]
    [InlineData(CredentialKind.Pin)]
    [InlineData(CredentialKind.Passphrase)]
    public void Format_then_Parse_round_trips(CredentialKind kind)
    {
        var text = Format(KdfProfile.LowMemory, kind);
        Assert.StartsWith("dgb2$", text);

        var result = EnvelopeCodec.Parse(text);

        Assert.Equal(EnvelopeStatus.Ok, result.Status);
        var e = result.Envelope!;
        Assert.Equal(KdfProfile.LowMemory, e.Kdf);
        Assert.Equal(kind, e.Kind);
        Assert.Equal(Salt, e.Salt);
        Assert.Equal(Nonce, e.Nonce);
        Assert.Equal(Cipher, e.CiphertextAndTag);
    }

    [Fact]
    public void Header_is_the_first_nine_bytes_and_encodes_the_parameters()
    {
        var e = EnvelopeCodec.Parse(Format(KdfProfile.Standard, CredentialKind.Passphrase)).Envelope!;

        Assert.Equal(9, e.Header.Length);
        Assert.Equal(0x02, e.Header[0]);
        Assert.Equal(0x01, e.Header[1]);
        var memory = (e.Header[2] << 24) | (e.Header[3] << 16) | (e.Header[4] << 8) | e.Header[5];
        Assert.Equal(KdfProfile.Standard.MemoryKiB, memory);
        Assert.Equal(KdfProfile.Standard.Passes, e.Header[6]);
        Assert.Equal(1, e.Header[7]);
        Assert.Equal(0x02, e.Header[8]);
    }

    [Fact]
    public void Legacy_base64_value_is_reported_as_legacy()
    {
        var legacy = Convert.ToBase64String(Enumerable.Range(0, 80).Select(i => (byte)i).ToArray());

        Assert.True(EnvelopeCodec.IsLegacy(legacy));
        Assert.Equal(EnvelopeStatus.Legacy, EnvelopeCodec.Parse(legacy).Status);
        Assert.False(EnvelopeCodec.IsLegacy(Format()));
    }

    [Fact]
    public void Unknown_future_version_prefix_is_unsupported_not_unreadable()
    {
        Assert.Equal(EnvelopeStatus.UnsupportedVersion, EnvelopeCodec.Parse("dgb3$AAAA").Status);
        Assert.Equal(EnvelopeStatus.UnsupportedVersion, EnvelopeCodec.Parse("dgb12$AAAA").Status);
    }

    [Fact]
    public void Truncated_input_is_unreadable()
    {
        var text = Format();
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(text[..20]).Status);
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse("dgb2$").Status);
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse("").Status);
    }

    [Fact]
    public void Invalid_base64url_is_unreadable()
    {
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse("dgb2$!!!not base64!!!").Status);
    }

    private static string Tamper(Action<byte[]> mutate)
    {
        var text = Format();
        var raw = Base64UrlDecode(text["dgb2$".Length..]);
        mutate(raw);
        return "dgb2$" + Base64UrlEncode(raw);
    }

    [Fact]
    public void Unknown_kdf_id_is_unreadable() =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b => b[1] = 0x09)).Status);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Credential_kind_outside_1_and_2_is_unreadable(byte kind) =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b => b[8] = kind)).Status);

    [Fact]
    public void Memory_below_the_floor_is_unreadable() =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b =>
        {
            // 19,455 KiB = 0x00004BFF
            b[2] = 0; b[3] = 0; b[4] = 0x4B; b[5] = 0xFF;
        })).Status);

    [Fact]
    public void Passes_below_two_is_unreadable() =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b => b[6] = 1)).Status);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Lanes_other_than_one_is_unreadable(byte lanes) =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b => b[7] = lanes)).Status);

    [Fact]
    public void Version_byte_other_than_two_is_unreadable() =>
        Assert.Equal(EnvelopeStatus.Unreadable, EnvelopeCodec.Parse(Tamper(b => b[0] = 0x05)).Status);

    [Fact]
    public void Floor_profile_is_accepted_exactly_at_the_floor()
    {
        var result = EnvelopeCodec.Parse(Format(KdfProfile.Floor));
        Assert.Equal(EnvelopeStatus.Ok, result.Status);
        Assert.Equal(19_456, KdfProfile.Floor.MemoryKiB);
        Assert.Equal(2, KdfProfile.Floor.Passes);
        Assert.Equal(1, KdfProfile.Floor.Lanes);
    }

    [Theory]
    [InlineData(19_455, 2, 1)]
    [InlineData(19_456, 1, 1)]
    [InlineData(19_456, 2, 2)]
    public void Format_refuses_to_write_below_the_floor(int memory, int passes, int lanes)
    {
        Assert.Throws<ArgumentException>(() =>
            EnvelopeCodec.Format(new KdfParameters(memory, passes, lanes), CredentialKind.Pin, Salt, Nonce, Cipher));
    }

    [Fact]
    public void Profiles_are_ordered_and_all_at_or_above_the_floor()
    {
        Assert.True(KdfProfile.Standard.MemoryKiB >= KdfProfile.LowMemory.MemoryKiB);
        Assert.True(KdfProfile.LowMemory.MemoryKiB >= KdfProfile.Floor.MemoryKiB);
        Assert.True(KdfProfile.IsAtOrAboveFloor(KdfProfile.Standard));
        Assert.False(KdfProfile.IsAtOrAboveFloor(new KdfParameters(1024, 3, 1)));
    }

    internal static string Base64UrlEncode(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        return Convert.FromBase64String(t);
    }
}

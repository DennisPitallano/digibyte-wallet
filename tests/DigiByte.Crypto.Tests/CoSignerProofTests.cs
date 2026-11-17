using DigiByte.Crypto.Multisig;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class CoSignerProofTests
{
    private const string Domain = CoSignerProof.CreationRoomDomain;
    private const string Nonce = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string Conn = "conn-abc123";

    private static (Key Key, string PubHex, string Proof) Signed(string purpose = "join", string target = "X7K9M2")
    {
        var key = new Key();
        var proof = CoSignerProof.Sign(key, Domain, purpose, target, Nonce, Conn);
        return (key, key.PubKey.ToHex(), proof);
    }

    [Fact]
    public void Message_has_the_exact_documented_layout()
    {
        Assert.Equal(
            "DigiByte multisig creation room\njoin\nX7K9M2\n" + Nonce + "\n" + Conn,
            CoSignerProof.BuildMessage(Domain, "join", "X7K9M2", Nonce, Conn));
        Assert.Equal("DigiByte multisig signing room", CoSignerProof.SigningRoomDomain);
    }

    [Theory]
    [InlineData("create", "Family vault")]
    [InlineData("join", "X7K9M2")]
    public void Roundtrip_verifies(string purpose, string target)
    {
        var (_, pub, proof) = Signed(purpose, target);
        Assert.True(CoSignerProof.TryVerify(pub, proof, Domain, purpose, target, Nonce, Conn));
    }

    [Fact]
    public void Proof_is_base64_of_65_byte_compact_signature_over_double_sha256()
    {
        var key = new Key();
        var proof = CoSignerProof.Sign(key, Domain, "join", "X7K9M2", Nonce, Conn);
        var bytes = Convert.FromBase64String(proof);
        Assert.Equal(65, bytes.Length);

        var msg = System.Text.Encoding.UTF8.GetBytes(CoSignerProof.BuildMessage(Domain, "join", "X7K9M2", Nonce, Conn));
        var hash = new uint256(NBitcoin.Crypto.Hashes.SHA256(NBitcoin.Crypto.Hashes.SHA256(msg)));
        var recovered = PubKey.RecoverCompact(hash, new CompactSignature((bytes[0] - 27) & 3, bytes[1..]));
        Assert.Equal(key.PubKey, recovered);
    }

    [Fact]
    public void Uppercase_public_key_hex_is_accepted()
    {
        var (_, pub, proof) = Signed();
        Assert.True(CoSignerProof.TryVerify(pub.ToUpperInvariant(), proof, Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Wrong_key_fails()
    {
        var (_, _, proof) = Signed();
        Assert.False(CoSignerProof.TryVerify(new Key().PubKey.ToHex(), proof, Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Changed_nonce_fails()
    {
        var (_, pub, proof) = Signed();
        var other = "ff" + Nonce[2..];
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "join", "X7K9M2", other, Conn));
    }

    [Fact]
    public void Changed_connection_fails()
    {
        var (_, pub, proof) = Signed();
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "join", "X7K9M2", Nonce, "conn-other"));
    }

    [Fact]
    public void Changed_purpose_fails()
    {
        var (_, pub, proof) = Signed("join");
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "create", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Changed_target_fails()
    {
        var (_, pub, proof) = Signed("join", "X7K9M2");
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "join", "X7K9M3", Nonce, Conn));
    }

    [Fact]
    public void Changed_domain_fails()
    {
        var (_, pub, proof) = Signed();
        Assert.False(CoSignerProof.TryVerify(pub, proof, CoSignerProof.SigningRoomDomain, "join", "X7K9M2", Nonce, Conn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]                 // 3 bytes
    public void Malformed_proof_fails(string proof)
    {
        var pub = new Key().PubKey.ToHex();
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Proof_with_64_or_66_bytes_fails()
    {
        var (_, pub, proof) = Signed();
        var bytes = Convert.FromBase64String(proof);
        Assert.False(CoSignerProof.TryVerify(pub, Convert.ToBase64String(bytes[..64]), Domain, "join", "X7K9M2", Nonce, Conn));
        Assert.False(CoSignerProof.TryVerify(pub, Convert.ToBase64String([.. bytes, 0]), Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Proof_with_invalid_header_byte_fails()
    {
        var (_, pub, proof) = Signed();
        var bytes = Convert.FromBase64String(proof);
        bytes[0] = 99;
        Assert.False(CoSignerProof.TryVerify(pub, Convert.ToBase64String(bytes), Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Proof_with_zeroed_signature_fails()
    {
        var pub = new Key().PubKey.ToHex();
        var bytes = new byte[65];
        bytes[0] = 31;
        Assert.False(CoSignerProof.TryVerify(pub, Convert.ToBase64String(bytes), Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02zz")]
    [InlineData("not-a-key")]
    public void Malformed_public_key_fails(string pub)
    {
        var (_, _, proof) = Signed();
        Assert.False(CoSignerProof.TryVerify(pub, proof, Domain, "join", "X7K9M2", Nonce, Conn));
    }

    [Fact]
    public void Fields_containing_a_newline_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CoSignerProof.BuildMessage(Domain, "join", "A\nB", Nonce, Conn));
        Assert.Throws<ArgumentException>(() => CoSignerProof.BuildMessage(Domain, "join", "X", Nonce, "c\nd"));
        var pub = new Key().PubKey.ToHex();
        Assert.False(CoSignerProof.TryVerify(pub, "AAAA", Domain, "join", "A\nB", Nonce, Conn));
    }

    [Fact]
    public void Empty_fields_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CoSignerProof.BuildMessage(Domain, "join", "", Nonce, Conn));
        Assert.Throws<ArgumentException>(() => CoSignerProof.BuildMessage(Domain, "join", "X", "", Conn));
    }
}

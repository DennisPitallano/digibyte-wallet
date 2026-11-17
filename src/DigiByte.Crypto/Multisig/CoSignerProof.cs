using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace DigiByte.Crypto.Multisig;

/// <summary>
/// Proof that a caller holds a multisig co-signer key, for co-signer coordination relays (constitution 1.2.0,
/// Security &amp; Custody → Auth boundary). The relay hands out a single-use nonce bound to a connection; the client
/// signs <see cref="BuildMessage"/> with its co-signer key and the relay recovers the public key from the proof.
/// <para>
/// Message: <c>"{domain}\n{purpose}\n{target}\n{nonceHex}\n{connectionId}"</c>.
/// Proof: Base64 of the 65-byte compact signature (<c>Key.SignCompact</c>) over SHA256(SHA256(UTF8(message))).
/// </para>
/// The proof reveals nothing about the private key and only covers the exact domain, purpose, target, nonce and
/// connection it was made for.
/// </summary>
public static class CoSignerProof
{
    /// <summary>Domain for the wallet-creation room hub (<c>/hubs/multisig-room</c>).</summary>
    public const string CreationRoomDomain = "DigiByte multisig creation room";

    /// <summary>Domain for the online signing room hub (specs/017-multisig-online-signing).</summary>
    public const string SigningRoomDomain = "DigiByte multisig signing room";

    /// <summary>Length of a proof once decoded from Base64.</summary>
    public const int ProofLength = 65;

    /// <summary>Builds the message to sign. Throws <see cref="ArgumentException"/> for empty fields or fields with line breaks.</summary>
    public static string BuildMessage(string domain, string purpose, string target, string nonceHex, string connectionId)
    {
        Check(domain, nameof(domain));
        Check(purpose, nameof(purpose));
        Check(target, nameof(target));
        Check(nonceHex, nameof(nonceHex));
        Check(connectionId, nameof(connectionId));
        return $"{domain}\n{purpose}\n{target}\n{nonceHex}\n{connectionId}";
    }

    /// <summary>Signs the proof message with <paramref name="key"/> and returns it as Base64.</summary>
    public static string Sign(Key key, string domain, string purpose, string target, string nonceHex, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = Hash(BuildMessage(domain, purpose, target, nonceHex, connectionId));
        var compact = key.SignCompact(hash);
        var bytes = new byte[ProofLength];
        bytes[0] = (byte)(27 + compact.RecoveryId + (key.IsCompressed ? 4 : 0));
        Buffer.BlockCopy(compact.Signature, 0, bytes, 1, 64);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// True when <paramref name="proofBase64"/> is a valid proof by the holder of <paramref name="publicKeyHex"/> (a compressed
    /// public key) for exactly these fields. Never throws; any malformed input returns false.
    /// </summary>
    public static bool TryVerify(string publicKeyHex, string proofBase64, string domain, string purpose, string target,
        string nonceHex, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(publicKeyHex) || string.IsNullOrWhiteSpace(proofBase64))
            return false;

        PubKey expected;
        try { expected = new PubKey(publicKeyHex.Trim()); }
        catch { return false; }
        if (!expected.IsCompressed) return false;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(proofBase64); }
        catch (FormatException) { return false; }
        if (bytes.Length != ProofLength) return false;

        // Compressed keys only: header 31..34.
        var header = bytes[0];
        if (header < 31 || header > 34) return false;

        uint256 hash;
        try { hash = Hash(BuildMessage(domain, purpose, target, nonceHex, connectionId)); }
        catch (ArgumentException) { return false; }

        PubKey recovered;
        try { recovered = PubKey.RecoverCompact(hash, new CompactSignature(header - 31, bytes[1..])); }
        catch { return false; }

        return recovered.Compress() == expected;
    }

    private static uint256 Hash(string message) =>
        new(Hashes.SHA256(Hashes.SHA256(Encoding.UTF8.GetBytes(message))));

    private static void Check(string value, string name)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Proof fields must not be empty.", name);
        if (value.AsSpan().IndexOfAny('\n', '\r') >= 0)
            throw new ArgumentException("Proof fields must not contain line breaks.", name);
    }
}

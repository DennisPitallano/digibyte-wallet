using System.Text;
using DigiByte.Crypto.Networks;
using NBitcoin;
using NBitcoin.Crypto;

namespace DigiByte.Crypto.DigiId;

/// <summary>
/// Implements the Digi-ID authentication protocol.
/// Digi-ID allows signing in to websites by signing a challenge URI with a DigiByte private key.
///
/// Protocol:
/// 1. Website shows QR code with digiid://callback-url?x=nonce
/// 2. Wallet scans QR, derives a site-specific key from the callback domain
/// 3. Wallet signs the full URI with that key
/// 4. Wallet POSTs { address, uri, signature } to the callback URL
/// 5. Website verifies signature → user is authenticated
/// </summary>
public class DigiIdService
{
    /// <summary>
    /// Parses a Digi-ID URI and validates its format.
    /// Expected format: digiid://domain/path?x=nonce[&u=1 for unsecure]
    /// </summary>
    public static DigiIdRequest? ParseUri(string uri)
    {
        if (!uri.StartsWith("digiid://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            // Replace digiid:// with https:// to parse as standard URL
            var httpUri = new Uri("https://" + uri["digiid://".Length..]);
            var queryParams = System.Web.HttpUtility.ParseQueryString(httpUri.Query);
            var nonce = queryParams["x"];
            var unsecure = queryParams["u"] == "1";

            if (string.IsNullOrEmpty(nonce))
                return null;

            var callbackScheme = unsecure ? "http" : "https";
            var callbackUrl = $"{callbackScheme}://{httpUri.Host}{httpUri.AbsolutePath}";
            if (!string.IsNullOrEmpty(httpUri.Query))
                callbackUrl += httpUri.Query;

            return new DigiIdRequest
            {
                OriginalUri = uri,
                CallbackUrl = callbackUrl,
                Domain = httpUri.Host,
                Nonce = nonce,
                IsUnsecure = unsecure,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Signs a Digi-ID challenge URI with the given private key.
    /// Uses Bitcoin-style message signing with DigiByte's message magic prefix.
    /// Returns a compact recoverable signature (65 bytes, base64-encoded).
    /// </summary>
    public static DigiIdResponse Sign(DigiIdRequest request, Key privateKey)
    {
        var network = DigiByteNetwork.Mainnet;
        var address = privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network);

        // Build the message hash: SHA256(SHA256( VarInt(magic.len) + magic + VarInt(msg.len) + msg ))
        var messageBytes = Encoding.UTF8.GetBytes(request.OriginalUri);
        var magic = "DigiByte Signed Message:\n"u8;

        using var ms = new MemoryStream();
        WriteVarInt(ms, (ulong)magic.Length);   // 25 = 0x19
        ms.Write(magic);
        WriteVarInt(ms, (ulong)messageBytes.Length);
        ms.Write(messageBytes);

        var hash = new uint256(Hashes.SHA256(Hashes.SHA256(ms.ToArray())));

        // Compact recoverable signature (servers need this to recover the pubkey)
        var compact = privateKey.SignCompact(hash);
        var sigBytes = new byte[65];
        sigBytes[0] = (byte)(27 + compact.RecoveryId + 4); // +4 = compressed pubkey
        Buffer.BlockCopy(compact.Signature, 0, sigBytes, 1, 64);

        return new DigiIdResponse
        {
            Address = address.ToString(),
            Uri = request.OriginalUri,
            Signature = Convert.ToBase64String(sigBytes),
        };
    }

    /// <summary>
    /// Verifies a Digi-ID response: recovers the public key from the compact signature
    /// and checks the derived Legacy address matches the claimed one.
    /// Returns true if the signature is valid for the given uri + address.
    /// </summary>
    public static bool Verify(string address, string originalUri, string signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            string.IsNullOrWhiteSpace(originalUri) ||
            string.IsNullOrWhiteSpace(signatureBase64))
            return false;

        byte[] sigBytes;
        try { sigBytes = Convert.FromBase64String(signatureBase64); }
        catch { return false; }

        if (sigBytes.Length != 65) return false;

        // Compute the same double-SHA256 digest that Sign() produced.
        var messageBytes = Encoding.UTF8.GetBytes(originalUri);
        var magic = "DigiByte Signed Message:\n"u8;

        using var ms = new MemoryStream();
        WriteVarInt(ms, (ulong)magic.Length);
        ms.Write(magic);
        WriteVarInt(ms, (ulong)messageBytes.Length);
        ms.Write(messageBytes);
        var hash = new uint256(Hashes.SHA256(Hashes.SHA256(ms.ToArray())));

        // First byte encodes recovery id + compressed flag.
        // Valid header range: 27..34 (uncompressed 27-30, compressed 31-34).
        var header = sigBytes[0];
        if (header < 27 || header > 34) return false;
        var recoveryId = (header - 27) & 3;
        var rs = new byte[64];
        Buffer.BlockCopy(sigBytes, 1, rs, 0, 64);

        PubKey recovered;
        try { recovered = PubKey.RecoverCompact(hash, new CompactSignature(recoveryId, rs)); }
        catch { return false; }

        // Digi-ID always uses Legacy (P2PKH) on mainnet.
        var derived = recovered.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString();
        return string.Equals(derived, address.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes a Bitcoin-style variable-length integer to the stream.
    /// </summary>
    private static void WriteVarInt(Stream s, ulong value)
    {
        if (value < 0xFD)
        {
            s.WriteByte((byte)value);
        }
        else if (value <= 0xFFFF)
        {
            s.WriteByte(0xFD);
            s.WriteByte((byte)(value & 0xFF));
            s.WriteByte((byte)((value >> 8) & 0xFF));
        }
        else if (value <= 0xFFFFFFFF)
        {
            s.WriteByte(0xFE);
            s.Write(BitConverter.GetBytes((uint)value));
        }
        else
        {
            s.WriteByte(0xFF);
            s.Write(BitConverter.GetBytes(value));
        }
    }

    /// <summary>
    /// Derives a deterministic site-specific key index from the callback domain.
    /// This ensures the same key is used for the same site every time,
    /// but different sites get different keys (privacy).
    ///
    /// Path: m/13'/site-hash'/0'/0
    /// 13 = Digi-ID purpose (like BIP84's 84)
    /// </summary>
    public static int DeriveSiteIndex(string domain)
    {
        var domainBytes = Encoding.UTF8.GetBytes(domain.ToLower());
        var hash = Hashes.SHA256(domainBytes);
        // Use first 4 bytes as a 31-bit positive integer
        return (int)(BitConverter.ToUInt32(hash, 0) & 0x7FFFFFFF);
    }
}

public class DigiIdRequest
{
    public required string OriginalUri { get; init; }
    public required string CallbackUrl { get; init; }
    public required string Domain { get; init; }
    public required string Nonce { get; init; }
    public bool IsUnsecure { get; init; }
}

public class DigiIdResponse
{
    public required string Address { get; init; }
    public required string Uri { get; init; }
    public required string Signature { get; init; }
}

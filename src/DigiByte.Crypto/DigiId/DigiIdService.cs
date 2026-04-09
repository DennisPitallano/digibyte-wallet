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
    /// Returns the response to send to the callback URL.
    /// </summary>
    public static DigiIdResponse Sign(DigiIdRequest request, Key privateKey)
    {
        var network = DigiByteNetwork.Mainnet;
        var address = privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network);

        // Sign the full original URI using Bitcoin-style message signing
        var messageBytes = Encoding.UTF8.GetBytes(request.OriginalUri);
        var prefixBytes = Encoding.UTF8.GetBytes("\x18DigiByte Signed Message:\n");
        var lenBytes = new byte[] { (byte)messageBytes.Length };

        var toHash = new byte[prefixBytes.Length + lenBytes.Length + messageBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, toHash, 0, prefixBytes.Length);
        Buffer.BlockCopy(lenBytes, 0, toHash, prefixBytes.Length, lenBytes.Length);
        Buffer.BlockCopy(messageBytes, 0, toHash, prefixBytes.Length + lenBytes.Length, messageBytes.Length);

        var hash = new uint256(Hashes.SHA256(Hashes.SHA256(toHash)));
        var sig = privateKey.Sign(hash);
        var signature = Convert.ToBase64String(sig.ToDER());

        return new DigiIdResponse
        {
            Address = address.ToString(),
            Uri = request.OriginalUri,
            Signature = signature,
        };
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

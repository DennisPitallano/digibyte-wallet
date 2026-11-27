using System.Security.Cryptography;
using System.Text;
using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.Networks;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>
/// A merchant's DigiDollar account public key (<c>xpub</c>/<c>tpub</c> at <c>m/86'/coin'/0'</c>), from which DigiPay derives
/// DigiDollar receive addresses without any private key (specs/035 research R1, R2). Address <c>i</c> is the BIP86 key-path
/// Taproot output key of child <c>0/i</c>, the same address <see cref="DigiDollarKeyDerivation"/> derives from the seed.
/// </summary>
public sealed class DigiDollarAccountKey
{
    public const string Malformed = "malformed";
    public const string PrivateKey = "private_key";
    public const string WrongNetwork = "wrong_network";
    public const string NotAccountKey = "not_account_key";

    private const int AccountDepth = 3;
    private const int ExtendedKeyLength = 78;

    private readonly ExtPubKey _receive;

    private DigiDollarAccountKey(ExtPubKey account, string canonical, string network)
    {
        Canonical = canonical;
        Network = network;
        KeyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(network + ":" + canonical))).ToLowerInvariant();
        _receive = account.Derive(0);
    }

    /// <summary>The key exactly as stored: the trimmed text the merchant pasted.</summary>
    public string Canonical { get; }

    /// <summary>"mainnet" or "testnet".</summary>
    public string Network { get; }

    /// <summary>SHA-256 hex of <c>network:canonical</c>: the id of the key's shared address sequence.</summary>
    public string KeyId { get; }

    /// <summary>The first 8 hex characters of <see cref="KeyId"/>, for display only.</summary>
    public string Fingerprint => KeyId[..8];

    /// <summary>The DigiDollar receive address at <c>0/index</c>.</summary>
    public string Address(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        var outputKey = _receive.Derive((uint)index).PubKey.TaprootInternalKey.GetTaprootFullPubKey().OutputKey;
        return DigiDollarAddressValidator.Encode(outputKey, Network);
    }

    /// <summary>
    /// Parse a pasted key for <paramref name="network"/>. Refuses private keys, keys for the other network and keys at any
    /// depth but the account level. The pasted text never appears in the result, so a private key can't leak through it.
    /// </summary>
    public static ParseResult Parse(string? text, string network)
    {
        if (network is not ("mainnet" or "testnet"))
            throw new ArgumentException("DigiDollar supports 'mainnet' and 'testnet' only.", nameof(network));

        var trimmed = (text ?? "").Trim();
        byte[] data;
        try
        {
            data = Encoders.Base58Check.DecodeData(trimmed);
        }
        catch (FormatException)
        {
            return ParseResult.Fail(Malformed);
        }
        if (trimmed.Length == 0 || data.Length != ExtendedKeyLength)
            return ParseResult.Fail(Malformed);

        var version = data.AsSpan(0, 4);
        var main = DigiByteNetwork.Mainnet;
        var test = DigiByteNetwork.Testnet;
        if (Matches(version, main, Base58Type.EXT_SECRET_KEY) || Matches(version, test, Base58Type.EXT_SECRET_KEY))
            return ParseResult.Fail(PrivateKey);

        var isMainPub = Matches(version, main, Base58Type.EXT_PUBLIC_KEY);
        var isTestPub = Matches(version, test, Base58Type.EXT_PUBLIC_KEY);
        if (!isMainPub && !isTestPub)
            return ParseResult.Fail(Malformed);
        if ((network == "mainnet") != isMainPub)
            return ParseResult.Fail(WrongNetwork);

        ExtPubKey key;
        try
        {
            key = new ExtPubKey(data.AsSpan(4).ToArray());
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return ParseResult.Fail(Malformed);
        }
        if (key.Depth != AccountDepth)
            return ParseResult.Fail(NotAccountKey);

        return new ParseResult(new DigiDollarAccountKey(key, trimmed, network), null);
    }

    private static bool Matches(ReadOnlySpan<byte> version, Network net, Base58Type type) =>
        version.SequenceEqual(net.GetVersionBytes(type, true));

    /// <summary>Either a key, or the reason it was refused (<see cref="Malformed"/>, <see cref="PrivateKey"/>, ...).</summary>
    public sealed record ParseResult(DigiDollarAccountKey? Key, string? Reason)
    {
        public bool Ok => Key is not null;

        internal static ParseResult Fail(string reason) => new(null, reason);

        public override string ToString() => Ok ? $"DigiDollarAccountKey({Key!.Fingerprint})" : $"Refused({Reason})";
    }
}

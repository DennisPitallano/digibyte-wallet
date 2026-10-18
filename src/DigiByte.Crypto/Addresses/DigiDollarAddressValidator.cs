using NBitcoin;
using NBitcoin.DataEncoders;

namespace DigiByte.Crypto.Addresses;

/// <summary>
/// Client-side encoder/validator for DigiDollar (DD) address strings.
///
/// DD addresses are Base58Check-encoded P2TR outputs with a 2-byte version
/// prefix that differs from standard DigiByte addresses. Same underlying
/// chain — different envelope. Version bytes are hard-coded in DigiByte Core
/// (<c>src/base58.cpp:180-182</c>):
///   mainnet: 0x52, 0x85  →  "DD…"
///   testnet: 0xb1, 0x29  →  "TD…"
///   regtest: 0xa3, 0xa4  →  "RD…"
///
/// The encoded body is the 32-byte Taproot OUTPUT key, so decoded length is
/// exactly 34 bytes (2 version + 32 key). Core uses that key as-is in the
/// value-0 DD output script <c>OP_1 &lt;key&gt;</c> (see research R1/R2/R3), so the
/// scriptPubKey is a pure function of the address. Prefix alone is not proof
/// of validity — always call <see cref="IsValid"/>, which decodes + checksums.
/// </summary>
public static class DigiDollarAddressValidator
{
    private static readonly byte[] MainnetPrefix = [0x52, 0x85];
    private static readonly byte[] TestnetPrefix = [0xb1, 0x29];
    private static readonly byte[] RegtestPrefix = [0xa3, 0xa4];

    private const int KeyLength = 32;
    private const int ExpectedDecodedLength = 2 + KeyLength; // 2-byte version + 32-byte output key

    private static readonly Base58CheckEncoder Encoder = new();

    /// <summary>
    /// Encode a 32-byte Taproot output key as a DD address for the given network
    /// ("mainnet", "testnet" or "regtest").
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> outputKey, string? network)
    {
        if (outputKey.Length != KeyLength)
            throw new ArgumentException($"DigiDollar output key must be {KeyLength} bytes.", nameof(outputKey));

        var prefix = GetPrefix(network)
            ?? throw new ArgumentException($"Unknown DigiDollar network '{network}'.", nameof(network));

        var payload = new byte[ExpectedDecodedLength];
        prefix.CopyTo(payload, 0);
        outputKey.CopyTo(payload.AsSpan(2));
        return Encoder.EncodeData(payload);
    }

    /// <summary>Encode a Taproot output key as a DD address.</summary>
    public static string Encode(TaprootPubKey outputKey, string? network) =>
        Encode(outputKey.ToBytes(), network);

    /// <summary>
    /// Decode a DD address into its network and 32-byte output key. Passing
    /// <paramref name="network"/> as null accepts any network; otherwise the
    /// address must belong to that network. Returns false for anything that is
    /// not a well-formed DD address (bad checksum, wrong length, wrong prefix,
    /// surrounding whitespace, a DGB address, empty input).
    /// </summary>
    public static bool TryDecode(string? address, string? network, out string detectedNetwork, out byte[] outputKey)
    {
        detectedNetwork = "";
        outputKey = [];

        if (string.IsNullOrWhiteSpace(address)) return false;
        if (!string.Equals(address, address.Trim(), StringComparison.Ordinal)) return false;

        // Quick reject on prefix so we don't waste the Base58 decoder on the common typo case.
        var expectedPrefix = GetExpectedPrefix(network);
        if (expectedPrefix is not null && !address.StartsWith(expectedPrefix, StringComparison.Ordinal)) return false;

        byte[] decoded;
        try { decoded = Encoder.DecodeData(address); }
        catch { return false; } // bad checksum or non-base58 chars

        if (decoded.Length != ExpectedDecodedLength) return false;

        var detected =
            MatchesPrefix(decoded, MainnetPrefix) ? "mainnet"
            : MatchesPrefix(decoded, TestnetPrefix) ? "testnet"
            : MatchesPrefix(decoded, RegtestPrefix) ? "regtest"
            : null;
        if (detected is null) return false;
        if (network is not null && !string.Equals(network, detected, StringComparison.Ordinal)) return false;

        // The key must be a real x-only point: funds sent to an off-curve key could never be spent.
        var key = decoded[2..];
        if (!TaprootPubKey.TryCreate(key, out _)) return false;

        detectedNetwork = detected;
        outputKey = key;
        return true;
    }

    /// <summary>
    /// The DD output scriptPubKey for an address: <c>OP_1 &lt;32-byte output key&gt;</c>
    /// (canonical value-0 P2TR). Throws <see cref="ArgumentException"/> for an invalid address.
    /// </summary>
    public static Script ToScriptPubKey(string? address, string? network = null)
    {
        if (!TryDecode(address, network, out _, out var key))
            throw new ArgumentException("Not a valid DigiDollar address for this network.", nameof(address));
        return new Script(OpcodeType.OP_1, Op.GetPushOp(key));
    }

    /// <summary>
    /// Validate a DD address string. Passing <paramref name="network"/> as
    /// null accepts any of mainnet/testnet/regtest — callers should pass the
    /// explicit network when they know it to catch cross-network typos.
    /// </summary>
    public static bool IsValid(string? address, string? network = null) =>
        TryDecode(address, network, out _, out _);

    /// <summary>
    /// Identify which network a valid DD address belongs to. Returns null for
    /// invalid input — pair with <see cref="IsValid"/> when you need both.
    /// </summary>
    public static string? DetectNetwork(string? address) =>
        TryDecode(address, null, out var detected, out _) ? detected : null;

    private static byte[]? GetPrefix(string? network) => network switch
    {
        "mainnet" => MainnetPrefix,
        "testnet" => TestnetPrefix,
        "regtest" => RegtestPrefix,
        _ => null,
    };

    private static string? GetExpectedPrefix(string? network) => network switch
    {
        "mainnet" => "DD",
        "testnet" => "TD",
        "regtest" => "RD",
        _ => null, // accept any prefix; DecodeData will still catch bad addresses
    };

    private static bool MatchesPrefix(byte[] decoded, byte[] prefix) =>
        decoded.Length >= prefix.Length
        && decoded[0] == prefix[0]
        && decoded[1] == prefix[1];
}

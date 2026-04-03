using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Addresses;

public static class AddressValidator
{
    /// <summary>
    /// Validates a DigiByte address string.
    /// Supports legacy (D...), P2SH (3...), and Bech32 (dgb1...) formats.
    /// </summary>
    public static bool IsValid(string address, Network? network = null)
    {
        network ??= DigiByteNetwork.Mainnet;
        try
        {
            BitcoinAddress.Create(address, network);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a DigiByte address string into a BitcoinAddress object.
    /// </summary>
    public static BitcoinAddress Parse(string address, Network? network = null)
    {
        network ??= DigiByteNetwork.Mainnet;
        return BitcoinAddress.Create(address, network);
    }

    /// <summary>
    /// Returns the address type (Legacy, P2SH, or SegWit).
    /// </summary>
    public static AddressType GetAddressType(string address, Network? network = null)
    {
        network ??= DigiByteNetwork.Mainnet;
        var parsed = BitcoinAddress.Create(address, network);
        return parsed switch
        {
            BitcoinPubKeyAddress => AddressType.Legacy,
            BitcoinScriptAddress => AddressType.P2SH,
            BitcoinWitPubKeyAddress => AddressType.SegWit,
            BitcoinWitScriptAddress => AddressType.SegWitScript,
            _ => AddressType.Unknown
        };
    }
}

public enum AddressType
{
    Legacy,
    P2SH,
    SegWit,
    SegWitScript,
    Unknown
}

using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>
/// Import and validate WIF (Wallet Import Format) private keys.
/// Used for recovering wallets from paper wallets, legacy exports, etc.
/// </summary>
public static class PrivateKeyImporter
{
    /// <summary>
    /// Validates a WIF private key string. Tries all DigiByte networks if none specified.
    /// </summary>
    public static bool IsValidWif(string wif, Network? network = null)
    {
        if (string.IsNullOrWhiteSpace(wif)) return false;

        if (network != null)
        {
            try { new BitcoinSecret(wif, network); return true; }
            catch { return false; }
        }

        // Try all networks
        Network[] networks = [DigiByteNetwork.Mainnet, DigiByteNetwork.Testnet, DigiByteNetwork.Regtest];
        foreach (var net in networks)
        {
            try { new BitcoinSecret(wif, net); return true; }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Parse a WIF string and extract the private key.
    /// </summary>
    public static Key ParseWif(string wif, Network network)
    {
        var secret = new BitcoinSecret(wif, network);
        return secret.PrivateKey;
    }

    /// <summary>
    /// Detect which network a WIF key belongs to.
    /// Returns null if not valid on any network.
    /// </summary>
    public static Network? DetectNetwork(string wif)
    {
        Network[] networks = [DigiByteNetwork.Mainnet, DigiByteNetwork.Testnet, DigiByteNetwork.Regtest];
        foreach (var net in networks)
        {
            try { new BitcoinSecret(wif, net); return net; }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Get the address derived from a WIF private key.
    /// </summary>
    public static string GetAddress(string wif, Network network, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        var key = ParseWif(wif, network);
        return key.PubKey.GetAddress(type, network).ToString();
    }

    /// <summary>
    /// Get all address formats for a WIF key (Legacy, SegWit).
    /// </summary>
    public static Dictionary<string, string> GetAllAddresses(string wif, Network network)
    {
        var key = ParseWif(wif, network);
        return new Dictionary<string, string>
        {
            ["legacy"] = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString(),
            ["segwit"] = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString(),
        };
    }
}

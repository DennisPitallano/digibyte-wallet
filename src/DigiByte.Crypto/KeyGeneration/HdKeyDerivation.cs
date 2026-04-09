using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>
/// BIP84 HD key derivation for DigiByte (native SegWit).
/// Path: m/84'/20'/account'/change/index
/// Coin type 20 = DigiByte.
/// </summary>
public class HdKeyDerivation
{
    private const int Purpose = 84;
    private const int CoinType = 20;
    private readonly ExtKey? _masterKey;
    private readonly ExtPubKey? _accountPubKey;
    private readonly Network _network;

    /// <summary>
    /// Whether this instance is watch-only (xpub, no private keys).
    /// </summary>
    public bool IsWatchOnly => _masterKey == null;

    public HdKeyDerivation(Mnemonic mnemonic, string passphrase = "", Network? network = null)
    {
        _network = network ?? DigiByteNetwork.Mainnet;
        _masterKey = mnemonic.DeriveExtKey(passphrase);
    }

    public HdKeyDerivation(ExtKey masterKey, Network? network = null)
    {
        _network = network ?? DigiByteNetwork.Mainnet;
        _masterKey = masterKey;
    }

    /// <summary>
    /// Watch-only constructor from an account-level extended public key.
    /// Can derive addresses but cannot sign transactions.
    /// </summary>
    public HdKeyDerivation(ExtPubKey accountPubKey, Network? network = null)
    {
        _network = network ?? DigiByteNetwork.Mainnet;
        _accountPubKey = accountPubKey;
    }

    /// <summary>
    /// Derives the account-level extended key: m/84'/20'/account'
    /// </summary>
    public ExtKey DeriveAccount(int account = 0)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        var path = new KeyPath($"m/{Purpose}'/{CoinType}'/{account}'");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Derives a receiving address key: m/84'/20'/account'/0/index
    /// </summary>
    public ExtKey DeriveReceivingKey(int index, int account = 0)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        var path = new KeyPath($"m/{Purpose}'/{CoinType}'/{account}'/0/{index}");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Derives a change address key: m/84'/20'/account'/1/index
    /// </summary>
    public ExtKey DeriveChangeKey(int index, int account = 0)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        var path = new KeyPath($"m/{Purpose}'/{CoinType}'/{account}'/1/{index}");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Gets a DigiByte address from a derived key.
    /// </summary>
    public BitcoinAddress GetAddress(ExtKey key, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        return key.PrivateKey.PubKey.GetAddress(type, _network);
    }

    /// <summary>
    /// Gets a DigiByte address from a public key (works for watch-only wallets).
    /// </summary>
    public BitcoinAddress GetAddress(PubKey pubKey, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        return pubKey.GetAddress(type, _network);
    }

    /// <summary>
    /// Derives a receiving address from the account public key (watch-only compatible).
    /// Path relative to account: 0/index
    /// </summary>
    public BitcoinAddress DeriveReceivingAddress(int index, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        var pubKey = GetAccountPubKey().Derive(0).Derive((uint)index).PubKey;
        return pubKey.GetAddress(type, _network);
    }

    /// <summary>
    /// Derives a change address from the account public key (watch-only compatible).
    /// Path relative to account: 1/index
    /// </summary>
    public BitcoinAddress DeriveChangeAddress(int index, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        var pubKey = GetAccountPubKey().Derive(1).Derive((uint)index).PubKey;
        return pubKey.GetAddress(type, _network);
    }

    /// <summary>
    /// Returns the account-level ExtPubKey, works for both HD and watch-only wallets.
    /// </summary>
    private ExtPubKey GetAccountPubKey(int account = 0)
    {
        if (_accountPubKey != null)
            return _accountPubKey;
        return DeriveAccount(account).Neuter();
    }

    /// <summary>
    /// Derives a batch of receiving addresses.
    /// </summary>
    public List<(int Index, BitcoinAddress Address, ExtKey Key)> DeriveReceivingAddresses(
        int count, int startIndex = 0, int account = 0, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        var results = new List<(int, BitcoinAddress, ExtKey)>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            var key = DeriveReceivingKey(i, account);
            var address = GetAddress(key, type);
            results.Add((i, address, key));
        }
        return results;
    }

    /// <summary>
    /// Derives a batch of receiving addresses (watch-only compatible, no private keys).
    /// </summary>
    public List<(int Index, BitcoinAddress Address)> DeriveReceivingAddressRange(
        int count, int startIndex = 0, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
        var results = new List<(int, BitcoinAddress)>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            var address = DeriveReceivingAddress(i, type);
            results.Add((i, address));
        }
        return results;
    }

    /// <summary>
    /// Gets the master extended public key for watch-only purposes.
    /// </summary>
    public ExtPubKey GetAccountExtPubKey(int account = 0)
    {
        return GetAccountPubKey(account);
    }

    /// <summary>
    /// Derives a Digi-ID site-specific key: m/13'/siteIndex'/0'/0
    /// Purpose 13 is the Digi-ID authentication standard.
    /// The siteIndex is derived from the callback domain hash.
    /// </summary>
    public ExtKey DeriveDigiIdKey(int siteIndex)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive Digi-ID keys from a watch-only wallet.");
        var path = new KeyPath($"m/13'/{siteIndex}'/0'/0");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Derives a key using the old BIP44 path (m/44'/20'/account'/change/index).
    /// Used for migration: scanning legacy BIP44 addresses to sweep funds to BIP84.
    /// </summary>
    public ExtKey DeriveLegacyBip44Key(int index, int change = 0, int account = 0)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        var path = new KeyPath($"m/44'/{CoinType}'/{account}'/{change}/{index}");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Gets legacy BIP44 addresses (both Legacy and SegWit encodings) for migration scanning.
    /// Returns address → ExtKey mappings for the first N receiving + change addresses.
    /// </summary>
    public Dictionary<string, ExtKey> GetLegacyBip44AddressMap(int receivingCount = 20, int changeCount = 10)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");

        var map = new Dictionary<string, ExtKey>();
        for (int i = 0; i < receivingCount; i++)
        {
            var key = DeriveLegacyBip44Key(i, change: 0);
            map[key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, _network).ToString()] = key;
            map[key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, _network).ToString()] = key;
        }
        for (int i = 0; i < changeCount; i++)
        {
            var key = DeriveLegacyBip44Key(i, change: 1);
            map[key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, _network).ToString()] = key;
            map[key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, _network).ToString()] = key;
        }
        return map;
    }

    /// <summary>
    /// Derives addresses across multiple BIP purpose paths (44, 49, 84) for fund recovery.
    /// Returns a list of (address, privateKey, path label) for each address found.
    /// </summary>
    public static List<(string Address, Key PrivateKey, string PathLabel)> DeriveMultiPathAddresses(
        Mnemonic mnemonic, Network network, int receivingCount = 20, int changeCount = 5)
    {
        var masterKey = mnemonic.DeriveExtKey();
        return DeriveMultiPathAddressesFromKey(masterKey, network, receivingCount, changeCount);
    }

    /// <summary>
    /// Derives addresses across old (non-BIP84) derivation paths from the current wallet's master key.
    /// Scans BIP44 and BIP49 paths — BIP84 is skipped since it's the active derivation path.
    /// </summary>
    public List<(string Address, Key PrivateKey, string PathLabel)> DeriveOldPathAddresses(
        int receivingCount = 20, int changeCount = 10)
    {
        if (_masterKey == null)
            throw new InvalidOperationException("Cannot derive private keys from a watch-only wallet.");
        return DeriveMultiPathAddressesFromKey(_masterKey, _network, receivingCount, changeCount,
            skipPurpose: Purpose); // skip BIP84 — it's the current active path
    }

    private static List<(string Address, Key PrivateKey, string PathLabel)> DeriveMultiPathAddressesFromKey(
        ExtKey masterKey, Network network, int receivingCount, int changeCount, int? skipPurpose = null)
    {
        var results = new List<(string Address, Key PrivateKey, string PathLabel)>();
        var seen = new HashSet<string>();

        var purposes = new[]
        {
            (Purpose: 44, Label: "BIP44"),
            (Purpose: 49, Label: "BIP49"),
            (Purpose: 84, Label: "BIP84"),
        };

        // Check all address types for each key — funds may be on a non-standard encoding
        // (e.g., BIP44 key used with SegWit address, which was the original wallet bug)
        var addressTypes = new[]
        {
            (Type: ScriptPubKeyType.Legacy, Suffix: "Legacy"),
            (Type: ScriptPubKeyType.SegwitP2SH, Suffix: "SegWit-P2SH"),
            (Type: ScriptPubKeyType.Segwit, Suffix: "SegWit"),
        };

        foreach (var (purpose, label) in purposes)
        {
            if (skipPurpose.HasValue && purpose == skipPurpose.Value) continue;

            for (int change = 0; change <= 1; change++)
            {
                int count = change == 0 ? receivingCount : changeCount;
                for (int i = 0; i < count; i++)
                {
                    var path = new KeyPath($"m/{purpose}'/{CoinType}'/{0}'/{change}/{i}");
                    var key = masterKey.Derive(path);

                    foreach (var (scriptType, suffix) in addressTypes)
                    {
                        var address = key.PrivateKey.PubKey.GetAddress(scriptType, network).ToString();
                        if (seen.Add(address))
                            results.Add((address, key.PrivateKey, $"{label} {suffix}"));
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Gets all address formats (Legacy, SegWit-P2SH, SegWit) for a single WIF private key.
    /// Returns a list of (address, privateKey, path label).
    /// </summary>
    public static List<(string Address, Key PrivateKey, string PathLabel)> DeriveWifAddresses(
        Key privateKey, Network network)
    {
        return
        [
            (privateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString(), privateKey, "WIF Legacy"),
            (privateKey.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, network).ToString(), privateKey, "WIF SegWit-P2SH"),
            (privateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString(), privateKey, "WIF SegWit"),
        ];
    }

    /// <summary>
    /// Parses an xpub/tpub string and validates it as a DigiByte extended public key.
    /// Tries the specified network first, then falls back to all DigiByte networks.
    /// Returns null if invalid.
    /// </summary>
    public static ExtPubKey? ParseXpub(string xpub, Network? network = null)
    {
        var trimmed = xpub.Trim();
        // Try the specified network first
        var networks = new[] { network ?? DigiByteNetwork.Mainnet, DigiByteNetwork.Mainnet, DigiByteNetwork.Testnet, DigiByteNetwork.Regtest };
        foreach (var net in networks)
        {
            try { return ExtPubKey.Parse(trimmed, net); }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Detects which DigiByte network an xpub/tpub is encoded for.
    /// Returns null if the key is invalid on all networks.
    /// </summary>
    public static Network? DetectXpubNetwork(string xpub)
    {
        var trimmed = xpub.Trim();
        Network[] networks = [DigiByteNetwork.Mainnet, DigiByteNetwork.Testnet, DigiByteNetwork.Regtest];
        foreach (var net in networks)
        {
            try { ExtPubKey.Parse(trimmed, net); return net; }
            catch { }
        }
        return null;
    }
}

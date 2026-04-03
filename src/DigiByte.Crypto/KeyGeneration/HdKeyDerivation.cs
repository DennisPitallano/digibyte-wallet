using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>
/// BIP44 HD key derivation for DigiByte.
/// Path: m/44'/20'/account'/change/index
/// Coin type 20 = DigiByte.
/// </summary>
public class HdKeyDerivation
{
    private const int CoinType = 20;
    private readonly ExtKey _masterKey;
    private readonly Network _network;

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
    /// Derives the account-level extended key: m/44'/20'/account'
    /// </summary>
    public ExtKey DeriveAccount(int account = 0)
    {
        var path = new KeyPath($"m/44'/{CoinType}'/{account}'");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Derives a receiving address key: m/44'/20'/account'/0/index
    /// </summary>
    public ExtKey DeriveReceivingKey(int index, int account = 0)
    {
        var path = new KeyPath($"m/44'/{CoinType}'/{account}'/0/{index}");
        return _masterKey.Derive(path);
    }

    /// <summary>
    /// Derives a change address key: m/44'/20'/account'/1/index
    /// </summary>
    public ExtKey DeriveChangeKey(int index, int account = 0)
    {
        var path = new KeyPath($"m/44'/{CoinType}'/{account}'/1/{index}");
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
    /// Derives a batch of receiving addresses.
    /// </summary>
    public List<(int Index, BitcoinAddress Address, ExtKey Key)> DeriveReceivingAddresses(
        int count, int startIndex = 0, int account = 0, ScriptPubKeyType type = ScriptPubKeyType.Segwit)
    {
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
    /// Gets the master extended public key for watch-only purposes.
    /// </summary>
    public ExtPubKey GetAccountExtPubKey(int account = 0)
    {
        return DeriveAccount(account).Neuter();
    }
}

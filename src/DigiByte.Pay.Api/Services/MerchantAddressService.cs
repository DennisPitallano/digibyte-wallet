using DigiByte.Crypto.Addresses;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Derives per-session receive addresses from a merchant's account-level xpub.
/// Path relative to the xpub: 0/index (receiving chain).
/// </summary>
public class MerchantAddressService
{
    public static Network ResolveNetwork(string name) => name.ToLowerInvariant() switch
    {
        "testnet" => DigiByteNetwork.Testnet,
        "regtest" => DigiByteNetwork.Regtest,
        _ => DigiByteNetwork.Mainnet,
    };

    public static bool TryValidateXpub(string xpub, string networkName, out string? error)
    {
        var network = ResolveNetwork(networkName);
        var parsed = HdKeyDerivation.ParseXpub(xpub, network);
        if (parsed is null)
        {
            error = "Invalid xpub — expected a DigiByte account-level extended public key.";
            return false;
        }
        error = null;
        return true;
    }

    public enum MerchantKeyKind { Address, Xpub }

    /// <summary>
    /// Classifies the merchant-supplied receive key as either a plain DigiByte address
    /// or an account-level xpub. Returns null + error if neither.
    /// </summary>
    public static MerchantKeyKind? Classify(string addressOrXpub, string networkName, out string? error)
    {
        var trimmed = addressOrXpub.Trim();
        if (AddressValidator.IsValid(trimmed))
        {
            error = null;
            return MerchantKeyKind.Address;
        }
        var network = ResolveNetwork(networkName);
        if (HdKeyDerivation.ParseXpub(trimmed, network) is not null)
        {
            error = null;
            return MerchantKeyKind.Xpub;
        }
        error = "Expected a DigiByte address or a BIP84 account-level xpub.";
        return null;
    }

    public static string DeriveAddress(string xpub, string networkName, int index)
    {
        var network = ResolveNetwork(networkName);
        var accountPubKey = HdKeyDerivation.ParseXpub(xpub, network)
            ?? throw new InvalidOperationException("Invalid xpub.");
        var derivation = new HdKeyDerivation(accountPubKey, network);
        return derivation.DeriveReceivingAddress(index).ToString();
    }
}

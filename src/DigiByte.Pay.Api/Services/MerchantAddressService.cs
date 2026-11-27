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
        var parsed = HdKeyDerivation.ParseXpubStrict(xpub, ResolveNetwork(networkName), out var refusal);
        if (parsed is null)
        {
            error = XpubError(refusal);
            return false;
        }
        error = null;
        return true;
    }

    public enum MerchantKeyKind { Address, Xpub }

    /// <summary>The error for an address-or-xpub the store can't use. Generic text is unchanged from before the strict check.</summary>
    public const string GenericKeyError = "Expected a DigiByte address or a BIP84 account-level xpub.";

    /// <summary>
    /// Classifies the merchant-supplied receive key as either a plain DigiByte address
    /// or an account-level xpub for the store's network. Returns null + error if neither; <paramref name="reason"/> is then
    /// <c>private_key</c>, <c>wrong_network</c> or <c>malformed</c>. Neither ever contains the pasted text.
    /// </summary>
    public static MerchantKeyKind? Classify(string addressOrXpub, string networkName, out string? error, out string? reason)
    {
        var trimmed = addressOrXpub.Trim();
        var network = ResolveNetwork(networkName);
        if (AddressValidator.IsValid(trimmed, network))
        {
            error = null;
            reason = null;
            return MerchantKeyKind.Address;
        }
        // An address for another DigiByte network is refused, like an xpub for one.
        if (AddressValidator.IsValid(trimmed))
        {
            reason = HdKeyDerivation.XpubRefusal.WrongNetwork;
            error = "The address is for a different network than this store.";
            return null;
        }
        // Strict: the key's version bytes must be the store's network, and private keys are refused (specs/035 R2 follow-up).
        if (HdKeyDerivation.ParseXpubStrict(trimmed, ResolveNetwork(networkName), out var refusal) is not null)
        {
            error = null;
            reason = null;
            return MerchantKeyKind.Xpub;
        }
        reason = refusal;
        error = XpubError(refusal);
        return null;
    }

    private static string XpubError(string? refusal) => refusal switch
    {
        HdKeyDerivation.XpubRefusal.PrivateKey =>
            "That is an extended private key (xprv/tprv). DigiPay only takes an account-level xpub; never share a private key.",
        HdKeyDerivation.XpubRefusal.WrongNetwork => "The xpub is for a different network than this store.",
        _ => GenericKeyError,
    };

    /// <summary>
    /// Derives the address at <c>0/index</c>. Uses the lenient parser on purpose: keys saved before the strict check are
    /// still served, and the address is always encoded for the store's network.
    /// </summary>
    public static string DeriveAddress(string xpub, string networkName, int index)
    {
        var network = ResolveNetwork(networkName);
        var accountPubKey = HdKeyDerivation.ParseXpub(xpub, network)
            ?? throw new InvalidOperationException("Invalid xpub.");
        var derivation = new HdKeyDerivation(accountPubKey, network);
        return derivation.DeriveReceivingAddress(index).ToString();
    }
}

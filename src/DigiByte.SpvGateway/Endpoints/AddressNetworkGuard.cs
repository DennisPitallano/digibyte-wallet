using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.SpvGateway.Endpoints;

/// <summary>
/// Checks the addresses in a request before any work is done: a sensible count, each address valid for this gateway's network, and
/// a clear refusal when an address belongs to the other network. Error messages never repeat the address.
/// </summary>
public static class AddressNetworkGuard
{
    public static IReadOnlyList<BitcoinAddress> Require(GatewayNetwork network, IReadOnlyCollection<string>? addresses, int max)
    {
        if (addresses is null || addresses.Count == 0)
            throw new GatewayException(GatewayError.BadRequest, "At least one address is needed.");
        if (addresses.Count > max)
            throw new GatewayException(GatewayError.OverLimit, $"Too many addresses in one request (max {max}).");

        var own = network == GatewayNetwork.Testnet ? DigiByteNetwork.Testnet : DigiByteNetwork.Mainnet;
        var other = network == GatewayNetwork.Testnet ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet;

        var parsed = new List<BitcoinAddress>(addresses.Count);
        foreach (var text in addresses)
        {
            if (TryParse(text, own, out var address))
            {
                parsed.Add(address);
                continue;
            }
            if (TryParse(text, other, out _))
                throw new GatewayException(GatewayError.WrongNetwork, $"An address belongs to the other network; this gateway serves {(network == GatewayNetwork.Testnet ? "testnet" : "mainnet")}.");
            throw new GatewayException(GatewayError.BadRequest, "An address is not valid.");
        }
        return parsed;
    }

    private static bool TryParse(string? text, Network network, out BitcoinAddress address)
    {
        address = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            address = BitcoinAddress.Create(text.Trim(), network);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

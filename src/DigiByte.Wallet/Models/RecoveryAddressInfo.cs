using NBitcoin;

namespace DigiByte.Wallet.Models;

/// <summary>
/// Represents an address discovered during fund recovery scanning.
/// </summary>
public class RecoveryAddressInfo(string address, Key privateKey, string pathLabel, long balanceSatoshis)
{
    public string Address { get; } = address;
    public Key PrivateKey { get; } = privateKey;
    public string PathLabel { get; } = pathLabel;
    public long BalanceSatoshis { get; } = balanceSatoshis;
}

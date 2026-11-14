namespace DigiByte.Wallet.Services;

/// <summary>
/// Encoding-independent form of an address, for comparing and grouping. Explorers can return a bech32
/// address with a different checksum variant than the wallet generated; dropping the 6-character
/// checksum of dgb1/dgbt1 addresses makes those compare equal. Other addresses are lowercased.
/// </summary>
public static class AddressKey
{
    public static string Normalize(string? address)
    {
        if (address is { Length: > 10 } &&
            (address.StartsWith("dgb1", StringComparison.OrdinalIgnoreCase) ||
             address.StartsWith("dgbt1", StringComparison.OrdinalIgnoreCase)))
            return address[..^6].ToLowerInvariant();
        return address?.ToLowerInvariant() ?? "";
    }
}

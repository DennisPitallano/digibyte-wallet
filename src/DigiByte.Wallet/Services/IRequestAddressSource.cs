namespace DigiByte.Wallet.Services;

/// <summary>
/// Where payment requests get their addresses (spec 036). <see cref="WalletService"/> implements it; the request
/// service depends on this rather than on the wallet, and the wallet never depends on requests.
/// </summary>
public interface IRequestAddressSource
{
    /// <summary>A new receive address for a request, not shown on Receive. See <see cref="WalletService.HandOutRequestAddressAsync"/>.</summary>
    Task<(int Index, string Address)> HandOutRequestAddressAsync();

    /// <summary>The address Receive shows; for a single-key wallet, its one address.</summary>
    Task<string> GetReceivingAddressAsync(string format = "default");
}

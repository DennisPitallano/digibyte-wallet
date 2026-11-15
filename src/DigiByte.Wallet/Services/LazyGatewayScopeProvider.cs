namespace DigiByte.Wallet.Services;

/// <summary>
/// Hands the gateway source its wallet scope without creating a dependency loop: the wallet service reaches the blockchain chain, and
/// the chain reaches the gateway source, so the source asks for the wallet service only when a read happens.
/// </summary>
public sealed class LazyGatewayScopeProvider(Func<IGatewayWalletScopeProvider> resolve) : IGatewayWalletScopeProvider
{
    public Task<GatewayWalletScope?> GetScopeAsync() => resolve().GetScopeAsync();
}

namespace DigiByte.Wallet.Services;

/// <summary>Where the SPV gateway sits among the wallet's blockchain sources.</summary>
public enum GatewayPosition
{
    /// <summary>Asked after the explorers could not answer. The safe default.</summary>
    Backup,
    /// <summary>Asked before the explorers, while its monitor says it is active.</summary>
    First,
}

/// <summary>
/// Which parts of the gateway feature are finished. The wallet's own proof-of-work check ships last (specs/011-p2p-spv-gateway,
/// Phase 8); until it does, data from the gateway is labeled "checked", never "verified", and the gateway never goes above the explorers.
/// </summary>
public static class GatewayCapabilities
{
    /// <summary>
    /// True once the wallet checks proof of work itself and enables the "verified" label. Flipped in task T114, only after the
    /// WebAssembly speed of the hashers has been measured and accepted.
    /// </summary>
    public static bool WalletPowVerified { get; } = false;

    /// <summary>The position that will actually be used for a requested one: a request for <c>First</c> is ignored until proof of work is verified.</summary>
    public static GatewayPosition EffectivePosition(GatewayPosition requested, bool walletPowVerified) =>
        walletPowVerified ? requested : GatewayPosition.Backup;
}

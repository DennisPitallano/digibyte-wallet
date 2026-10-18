namespace DigiByte.Wallet.Services;

/// <summary>The two independent runtime switches (FR-022). Public, non-secret, read from wwwroot/appsettings.json.</summary>
public readonly record struct DigiDollarFlags(bool ViewEnabled, bool SendEnabled);

/// <summary>What kind of wallet is active, for the seed-only rule (FR-030).</summary>
public enum DdWalletKind
{
    None,
    Seed,
    PrivateKey,
    WatchOnly,
    Multisig,
}

public enum DdHiddenReason
{
    None,
    FlagOff,
    Network,
    WalletType,
}

/// <param name="Visible">The DigiDollar section, pages and navigation are shown.</param>
/// <param name="SendVisible">The Send page and actions are available.</param>
/// <param name="ShowUnavailableLine">Show only the neutral "DigiDollar is temporarily unavailable" home line.</param>
/// <param name="ShowUnsupportedNotice">Show "DigiDollar requires a wallet created from a recovery phrase".</param>
public sealed record DdVisibility(
    bool Visible,
    bool SendVisible,
    bool ShowUnavailableLine,
    bool ShowUnsupportedNotice,
    DdHiddenReason HiddenReason);

/// <summary>
/// Effective visibility of DigiDollar (contracts/wallet-ui-contract.md):
/// <c>ddVisible = ViewEnabled &amp;&amp; network is mainnet or testnet &amp;&amp; the active wallet is seed-based</c>;
/// <c>ddSendVisible = ddVisible &amp;&amp; SendEnabled &amp;&amp; backendReachable &amp;&amp; deploymentActive</c>.
/// </summary>
public static class DigiDollarVisibility
{
    public static DdWalletKind KindFromWalletType(string? walletType) => walletType?.ToLowerInvariant() switch
    {
        null or "" => DdWalletKind.None,
        "hd" => DdWalletKind.Seed,
        "privatekey" => DdWalletKind.PrivateKey,
        "xpub" => DdWalletKind.WatchOnly,
        "multisig" => DdWalletKind.Multisig,
        // An unrecognised type is never assumed to be seed-based: DigiDollar needs a seed to derive keys.
        _ => DdWalletKind.PrivateKey,
    };

    public static bool IsSupportedNetwork(string? network) => network is "mainnet" or "testnet";

    public static DdVisibility Evaluate(
        DigiDollarFlags flags, string? network, DdWalletKind walletKind,
        bool backendReachable, bool deploymentActive, bool usedDigiDollarBefore)
    {
        // FR-029: regtest (or anything else) behaves as if DigiDollar did not exist: no section, line, or notice.
        if (!IsSupportedNetwork(network))
            return new DdVisibility(false, false, false, false, DdHiddenReason.Network);

        var isSeed = walletKind == DdWalletKind.Seed;

        if (!flags.ViewEnabled)
        {
            // FR-002: hide everything, except one neutral line on devices that already used DigiDollar.
            return new DdVisibility(false, false, ShowUnavailableLine: isSeed && usedDigiDollarBefore,
                ShowUnsupportedNotice: false, DdHiddenReason.FlagOff);
        }

        if (!isSeed)
        {
            // FR-030: explain instead of showing balances, addresses, or actions.
            var needsNotice = walletKind is DdWalletKind.PrivateKey or DdWalletKind.WatchOnly or DdWalletKind.Multisig;
            return new DdVisibility(false, false, false, needsNotice, DdHiddenReason.WalletType);
        }

        var send = flags.SendEnabled && backendReachable && deploymentActive;
        return new DdVisibility(true, send, false, false, DdHiddenReason.None);
    }
}

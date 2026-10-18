using DigiByte.Crypto.KeyGeneration;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The active wallet as DigiDollar sees it. <see cref="Derivation"/> is non-null only for an unlocked seed-based wallet
/// on mainnet or testnet; otherwise the kind and network tell the UI why DigiDollar is hidden or unsupported.
/// The mnemonic never leaves the wallet service: only the derivation object (kept in memory) is shared.
/// </summary>
public sealed record DdWalletSession(
    string WalletId,
    DdWalletKind Kind,
    string Network,
    DigiDollarKeyDerivation? Derivation);

/// <summary>Narrow seam between <c>WalletService</c> and the DigiDollar service (keeps DD testable and DGB code untouched).</summary>
public interface IDdSessionProvider
{
    /// <summary>The active unlocked wallet, or null when no wallet is unlocked.</summary>
    DdWalletSession? GetDigiDollarSession();
}

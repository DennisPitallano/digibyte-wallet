using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The PSBT feature's only route to private keys (specs/007-psbt-signing/contracts/psbt-signing.md). Keys never leave the
/// implementation: callers learn which scripts are owned and get back a signed copy.
/// </summary>
public interface IPsbtKeyProvider
{
    /// <summary>False when the wallet is locked or watch-only.</summary>
    bool CanSign { get; }

    /// <summary>The active wallet's network, used to load PSBTs and turn scripts into addresses.</summary>
    Network Network { get; }

    /// <summary>
    /// The scripts the wallet currently scans, with their addresses (FR-004), after waiting a limited time for address
    /// discovery. Never fails because discovery did. Contains no key material, and works for watch-only wallets.
    /// </summary>
    Task<IReadOnlyDictionary<Script, string>> GetOwnedScriptsAsync();

    /// <summary>
    /// Signs a copy, one input at a time, only for listed indexes whose script is owned. Never modifies the original.
    /// Throws <see cref="InvalidOperationException"/> when <see cref="CanSign"/> is false.
    /// </summary>
    PSBT SignInputs(PSBT psbt, IReadOnlyList<int> inputIndexes);
}

namespace DigiByte.Wallet.Storage;

/// <summary>
/// Thrown when a new wallet cannot be protected with the memory-hard scheme on this device or browser. Creating it with
/// the older protection is allowed only after the user has seen a plain warning and agreed (FR-021): the caller shows the
/// warning and, on agreement, calls again with allowLegacyProtection set.
/// </summary>
public sealed class LegacyProtectionConsentRequiredException(CryptoFailure reason)
    : InvalidOperationException("This device cannot run the stronger wallet protection. Creating the wallet needs the user's agreement to the older protection.")
{
    /// <summary>Why: <see cref="CryptoFailure.Unsupported"/> (the browser cannot) or <see cref="CryptoFailure.ResourceExhausted"/> (not enough memory).</summary>
    public CryptoFailure Reason { get; } = reason;
}

namespace DigiByte.Wallet.Services;

/// <summary>Why the wallet cannot hand out a new receive address. Screens translate by this, not by the English message.</summary>
public enum ReceiveAddressFailure
{
    /// <summary>The wallet is a single-key (WIF) wallet: it has one address only.</summary>
    SingleAddress,
    /// <summary>The new address could not be saved; the current one is still good to use.</summary>
    SaveFailed,
    /// <summary><see cref="WalletService.ReceiveGapLimit"/> unused addresses are out and none has been paid: a restore would not reach further.</summary>
    TooManyUnusedPastLastUsed,
    /// <summary><see cref="WalletService.ReceiveGapLimit"/> unused addresses are out past the last paid one, which is as far as the wallet keeps looking.</summary>
    TooManyUnusedPastLastPaid,
}

/// <summary>
/// Thrown by <see cref="WalletService.GenerateNewReceivingAddressAsync"/> when no new address can be handed out. The message
/// is the English sentence for the user; <see cref="Reason"/> is what a translated screen reads.
/// </summary>
public sealed class ReceiveAddressException(ReceiveAddressFailure reason, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public ReceiveAddressFailure Reason { get; } = reason;
}

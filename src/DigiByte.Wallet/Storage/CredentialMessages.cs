namespace DigiByte.Wallet.Storage;

/// <summary>
/// The wording for a credential check that did not succeed. One place, so every page says the right thing for the
/// wallet's credential (PIN or passphrase) and never describes an environmental problem (out of memory, an unreadable
/// or newer-format value) as a wrong credential (FR-016, FR-017).
/// </summary>
public static class CredentialMessages
{
    public static string Word(CredentialKind kind) => kind == CredentialKind.Passphrase ? "passphrase" : "PIN";

    public static string Incorrect(CredentialKind kind) => $"Incorrect {Word(kind)}.";

    public static string BiometricFailed(CredentialKind kind) => $"Biometric failed. Enter your {Word(kind)}.";

    /// <summary>Only a failed authentication is the user's mistake and counts toward the attempt limit.</summary>
    public static bool CountsAsAttempt(CryptoFailure? failure) => failure is null or CryptoFailure.WrongCredential;

    /// <summary>The calm note shown when the automatic upgrade could not run on this device. The wallet is unaffected.</summary>
    public static string MigrationDeferred(CryptoFailure why) => why == CryptoFailure.Unsupported
        ? "This browser can't use the stronger protection yet. Your wallet works as before, and we'll try again when you unlock somewhere that supports it."
        : "We couldn't strengthen this wallet's protection on this device right now because it ran low on memory. Your wallet works as before, and we'll try again the next time you unlock.";

    public static string ForFailure(CredentialKind kind, CryptoFailure failure) => failure switch
    {
        CryptoFailure.WrongCredential => Incorrect(kind),
        CryptoFailure.UnsupportedVersion =>
            "This wallet was saved by a newer version of the app. Update the app, then try again.",
        CryptoFailure.Unsupported =>
            "This browser can't run the protection this wallet needs. Update your browser or open the wallet in another one, then try again. This did not count as a wrong attempt.",
        CryptoFailure.ResourceExhausted =>
            $"This device ran out of memory while checking your {Word(kind)}. Close other tabs or apps and try again. This did not count as a wrong attempt.",
        _ => "The saved wallet data could not be read. Nothing was changed.",
    };
}

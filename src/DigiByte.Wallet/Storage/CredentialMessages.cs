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

    public static string ForFailure(CredentialKind kind, CryptoFailure failure) => failure switch
    {
        CryptoFailure.WrongCredential => Incorrect(kind),
        CryptoFailure.UnsupportedVersion =>
            "This wallet was saved by a newer version of the app. Update the app, then try again.",
        CryptoFailure.ResourceExhausted =>
            $"This device ran out of memory while checking your {Word(kind)}. Close other tabs or apps and try again. This did not count as a wrong attempt.",
        _ => "The saved wallet data could not be read. Nothing was changed.",
    };
}

using DigiByte.Wallet.Storage;

namespace DigiByte.Web.Services;

/// <summary>
/// The localised wording for credential checks: the right word for the wallet's credential, and a clear, separate
/// message for a problem that is not the user's mistake. Mirrors <see cref="CredentialMessages"/> (the English reference
/// the tests compare against) but reads each text from the locale files, so Spanish users see Spanish (FR-022).
/// </summary>
public static class CredentialText
{
    public static string Word(LocalizationService l, CredentialKind kind) =>
        l.T(kind == CredentialKind.Passphrase ? "credential.word_passphrase" : "credential.word_pin");

    public static string Incorrect(LocalizationService l, CredentialKind kind) =>
        l.T(kind == CredentialKind.Passphrase ? "credential.incorrect_passphrase" : "credential.incorrect_pin");

    public static string BiometricFailed(LocalizationService l, CredentialKind kind) =>
        l.T("credential.biometric_failed", Word(l, kind));

    public static string ForFailure(LocalizationService l, CredentialKind kind, CryptoFailure failure) => failure switch
    {
        CryptoFailure.WrongCredential => Incorrect(l, kind),
        CryptoFailure.UnsupportedVersion => l.T("credential.update_required"),
        CryptoFailure.Unsupported => l.T("credential.unsupported_browser"),
        CryptoFailure.ResourceExhausted => l.T("credential.out_of_memory", Word(l, kind)),
        _ => l.T("credential.unreadable"),
    };

    public static string MigrationDeferred(LocalizationService l, CryptoFailure why) =>
        l.T(why == CryptoFailure.Unsupported ? "credential.deferred_unsupported" : "credential.deferred_memory");
}

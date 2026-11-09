using System.Text;

namespace DigiByte.Wallet.Storage;

/// <summary>Why a passphrase was not accepted. Each reason has its own locale key so the user is told how to fix it.</summary>
public enum PassphraseRejection
{
    None,
    TooShort,
    TooLong,
    CommonPassword,
    Repeated,
    Sequential,
}

public sealed record PassphraseCheck(bool Accepted, PassphraseRejection Reason);

/// <summary>A deny list of very common passwords, matched on lower-cased, normalised text.</summary>
public interface ICommonPasswordList
{
    bool Contains(string lowercased);
}

/// <summary>
/// Rules for the two credential types (research R6): a six-digit PIN, or a passphrase of 12 to 256 characters
/// after Unicode NFKC normalisation that is not on the common list and not trivially repeated or sequential.
/// This is a deny list, not a strength meter.
/// </summary>
public static class CredentialPolicy
{
    public const int PassphraseMinLength = 12;
    public const int PassphraseMaxLength = 256;
    public const int PinLength = 6;

    /// <summary>NFKC so the same visible text gives the same key on every device (FR-006). Spaces are kept as typed.</summary>
    public static string Normalise(string text) => text.Normalize(NormalizationForm.FormKC);

    public static bool IsValidPin(string? pin) =>
        pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

    public static PassphraseCheck Check(string passphrase, ICommonPasswordList? commonList)
    {
        var text = Normalise(passphrase);
        var length = text.EnumerateRunes().Count();

        if (length < PassphraseMinLength) return Reject(PassphraseRejection.TooShort);
        if (length > PassphraseMaxLength) return Reject(PassphraseRejection.TooLong);
        if (IsRepeated(text)) return Reject(PassphraseRejection.Repeated);
        if (IsSequential(text)) return Reject(PassphraseRejection.Sequential);

        if (commonList is not null)
        {
            var lowered = text.ToLowerInvariant();
            if (commonList.Contains(lowered) || commonList.Contains(lowered.Trim()))
                return Reject(PassphraseRejection.CommonPassword);
        }
        return new PassphraseCheck(true, PassphraseRejection.None);
    }

    /// <summary>Locale key explaining a rejection, e.g. <c>passphrase.reject.too_short</c>.</summary>
    public static string ReasonKey(PassphraseRejection reason) => reason switch
    {
        PassphraseRejection.TooShort => "passphrase.reject.too_short",
        PassphraseRejection.TooLong => "passphrase.reject.too_long",
        PassphraseRejection.CommonPassword => "passphrase.reject.common",
        PassphraseRejection.Repeated => "passphrase.reject.repeated",
        PassphraseRejection.Sequential => "passphrase.reject.sequential",
        _ => "",
    };

    private static PassphraseCheck Reject(PassphraseRejection reason) => new(false, reason);

    // A block repeated two or more times: "aaaaaaaaaaaa", "abababababab", "passwordpassword".
    private static bool IsRepeated(string s)
    {
        var n = s.Length;
        for (var block = 1; block <= n / 2; block++)
        {
            if (n % block != 0) continue;
            var repeated = true;
            for (var i = block; i < n; i++)
            {
                if (s[i] != s[i - block]) { repeated = false; break; }
            }
            if (repeated) return true;
        }
        return false;
    }

    // Every step is +1 (or every step is -1): "abcdefghijkl", "zyxwvutsrqpo", "123456789012" (9 wraps to 0).
    private static bool IsSequential(string s)
    {
        if (s.Length < 2) return false;
        var up = true;
        var down = true;
        for (var i = 1; i < s.Length; i++)
        {
            var diff = s[i] - s[i - 1];
            var wrapUp = s[i - 1] == '9' && s[i] == '0';
            var wrapDown = s[i - 1] == '0' && s[i] == '9';
            if (diff != 1 && !wrapUp) up = false;
            if (diff != -1 && !wrapDown) down = false;
            if (!up && !down) return false;
        }
        return up || down;
    }
}

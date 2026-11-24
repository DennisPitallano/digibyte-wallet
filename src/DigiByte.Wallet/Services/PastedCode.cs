namespace DigiByte.Wallet.Services;

/// <summary>
/// Text that reaches the scan panel by paste, typing, a picture or the screen (specs/030-desktop-paste-scan).
/// Pasted text often arrives with what a chat app or an email added around it: spaces, line breaks, quotes.
/// <see cref="Clean"/> takes those off and nothing else, so the text that is routed is the code itself.
/// </summary>
public static class PastedCode
{
    /// <summary>
    /// The longest text accepted. A QR code holds at most 2,953 bytes, so this leaves room for any code the
    /// scanner can read, including a base64 multisig proposal, while refusing pastes of whole documents.
    /// </summary>
    public const int MaxLength = 8192;

    // Opening quote → its closing quote. Straight quotes close themselves; typographic ones have a partner.
    private static readonly Dictionary<char, char> QuotePairs = new()
    {
        ['"'] = '"',
        ['\''] = '\'',
        ['`'] = '`',
        ['“'] = '”',   // “ ”
        ['‘'] = '’',   // ‘ ’
    };

    /// <summary>
    /// Removes surrounding white space (line breaks included) and one pair of matching surrounding quotes, then
    /// white space again. Returns null when nothing is left or the result is longer than <see cref="MaxLength"/>.
    /// </summary>
    public static string? Clean(string? raw)
    {
        var text = Strip(raw);
        return text.Length == 0 || text.Length > MaxLength ? null : text;
    }

    /// <summary>True when there is text, but more than <see cref="MaxLength"/> characters of it once cleaned.</summary>
    public static bool IsTooLong(string? raw) => Strip(raw).Length > MaxLength;

    // Recovery phrases have these word counts (BIP39). A phrase still counts with a few typos in it.
    private static readonly HashSet<int> PhraseLengths = [12, 15, 18, 21, 24];
    private const double PhraseWordShare = 0.8;

    private static readonly HashSet<string> EnglishWords =
        new(NBitcoin.Wordlist.English.GetWords(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the text looks like a recovery phrase or a private key: 12, 15, 18, 21 or 24 words with at least
    /// 80% of them in the BIP39 English list; a WIF private key on any DigiByte network; or exactly 64 hex
    /// characters (a raw private key). It never decides whether a secret is valid, only that it should not stay
    /// on screen where it was not expected (FR-027).
    /// </summary>
    public static bool LooksSecret(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return false;

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (PhraseLengths.Contains(words.Length))
        {
            var known = words.Count(EnglishWords.Contains);
            if (known >= words.Length * PhraseWordShare) return true;
        }

        if (words.Length != 1) return false;
        if (trimmed.Length == 64 && trimmed.All(char.IsAsciiHexDigit)) return true;
        // A WIF key is 51 or 52 characters; anything else need not be parsed at all.
        return trimmed.Length is 51 or 52 && DigiByte.Crypto.KeyGeneration.PrivateKeyImporter.IsValidWif(trimmed);
    }

    private static string Strip(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length >= 2
            && QuotePairs.TryGetValue(text[0], out var closing)
            && text[^1] == closing)
        {
            text = text[1..^1].Trim();
        }
        return text;
    }
}

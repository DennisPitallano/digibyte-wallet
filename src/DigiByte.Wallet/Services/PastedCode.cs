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

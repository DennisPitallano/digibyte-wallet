namespace DigiByte.Wallet.Services;

/// <summary>Readable display of co-signer public keys, so they can be compared by eye with what a co-signer sent.</summary>
public static class MultisigKeyFormat
{
    private const int CompressedKeyHexLength = 66;
    private const int BlockSize = 8;

    /// <summary>Lower-cases the key and removes whitespace. Returns the input untouched if it isn't hex.</summary>
    public static string Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return hex ?? "";
        var compact = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        return compact.All(Uri.IsHexDigit) ? compact : hex;
    }

    /// <summary>
    /// Splits a 66-character compressed public key into blocks of 8 separated by spaces (the last block is shorter).
    /// Anything that is not a compressed-key-length hex string is returned unchanged.
    /// </summary>
    public static string Group(string? hex)
    {
        var normalized = Normalize(hex);
        if (normalized.Length != CompressedKeyHexLength || !normalized.All(Uri.IsHexDigit))
            return hex ?? "";

        var blocks = new List<string>();
        for (var i = 0; i < normalized.Length; i += BlockSize)
            blocks.Add(normalized.Substring(i, Math.Min(BlockSize, normalized.Length - i)));
        return string.Join(' ', blocks);
    }
}

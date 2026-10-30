using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DigiByte.P2P.Shared.Sponsors;

/// <summary>Image formats a sponsor picture may have, detected from its bytes (never from a file name).</summary>
public enum SponsorImageType { None, Png, Jpeg, WebP }

/// <summary>
/// The one set of limits and checks for sponsor entries, shared by the wallet (form and list parser) and the API
/// (submission endpoint) so the two cannot drift. See specs/006-sponsors-page/data-model.md.
/// </summary>
public static class SponsorRules
{
    public const int NameMaxLength = 60;
    public const int LinkMaxLength = 200;
    public const int ImageMaxBytes = 200 * 1024;
    public const int TxIdLength = 64;
    public const int MaxListEntries = 1000;

    private static readonly Regex ListImageName =
        new(@"^[a-z0-9][a-z0-9._-]{0,63}\.(png|jpe?g|webp)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Trims the name and checks it is 1–60 characters with no control characters. Markup is allowed here; it is rendered as plain text.</summary>
    public static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = "";
        if (name is null) return false;
        var trimmed = name.Trim();
        if (trimmed.Length < 1 || trimmed.Length > NameMaxLength) return false;
        foreach (var c in trimmed)
            if (char.IsControl(c)) return false;
        normalized = trimmed;
        return true;
    }

    /// <summary>Absolute https URL, at most 200 characters, no embedded credentials, no whitespace.</summary>
    public static bool IsValidLink(string? link)
    {
        if (string.IsNullOrEmpty(link) || link.Length > LinkMaxLength) return false;
        foreach (var c in link)
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Host.Length > 0;
    }

    /// <summary>A DGB transaction ID: exactly 64 hexadecimal characters.</summary>
    public static bool IsValidTxId(string? txid)
    {
        if (txid is null || txid.Length != TxIdLength) return false;
        foreach (var c in txid)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>Branch suffix for a submission: the first 16 hex characters of the transaction ID, lower-cased.</summary>
    public static string BranchSuffix(string txid) => txid[..16].ToLowerInvariant();

    /// <summary>A picture file name as it may appear in the list: lower-case, no folders, png/jpg/jpeg/webp only (no SVG).</summary>
    public static bool IsValidListImageName(string? name) => name is not null && ListImageName.IsMatch(name);

    public static bool IsImageSizeOk(long bytes) => bytes > 0 && bytes <= ImageMaxBytes;

    /// <summary>Detects PNG, JPEG or WebP from the signature bytes; anything else (SVG, GIF, RIFF/WAVE, ...) is <see cref="SponsorImageType.None"/>.</summary>
    public static SponsorImageType DetectImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return SponsorImageType.Png;
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return SponsorImageType.Jpeg;
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' &&
            b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P')
            return SponsorImageType.WebP;
        return SponsorImageType.None;
    }

    public static string ImageExtension(SponsorImageType type) => type switch
    {
        SponsorImageType.Png => "png",
        SponsorImageType.Jpeg => "jpg",
        SponsorImageType.WebP => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Entry id: first 12 hex characters of SHA-256 over the lower-cased name, link and image bytes.</summary>
    public static string ComputeEntryId(string name, string? link, ReadOnlySpan<byte> image)
    {
        var head = Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant() + "\n" + (link ?? "") + "\n");
        var all = new byte[head.Length + image.Length];
        head.CopyTo(all, 0);
        image.CopyTo(all.AsSpan(head.Length));
        return Convert.ToHexString(SHA256.HashData(all))[..12].ToLowerInvariant();
    }
}

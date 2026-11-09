namespace DigiByte.Wallet.Storage.Envelope;

public enum EnvelopeStatus
{
    /// <summary>A valid <c>dgb2</c> envelope.</summary>
    Ok,
    /// <summary>A value with no <c>dgbN$</c> prefix: the legacy PBKDF2 format.</summary>
    Legacy,
    /// <summary>Structurally invalid, truncated or below the parameter floor.</summary>
    Unreadable,
    /// <summary>A <c>dgbN$</c> prefix with N greater than 2: written by a newer build.</summary>
    UnsupportedVersion,
}

/// <summary>A parsed <c>dgb2</c> envelope. <see cref="Header"/> is the authenticated-data bytes (offsets 0 to 8).</summary>
public sealed record Envelope(
    KdfParameters Kdf,
    CredentialKind Kind,
    byte[] Salt,
    byte[] Nonce,
    byte[] CiphertextAndTag,
    byte[] Header);

public sealed record EnvelopeParseResult(EnvelopeStatus Status, Envelope? Envelope = null);

/// <summary>Parses and formats the <c>dgb2$</c> envelope. See contracts/envelope-format.md.</summary>
public static class EnvelopeCodec
{
    public const string Prefix = "dgb2$";
    private const byte Version = 0x02;
    private const byte KdfArgon2id = 0x01;
    private const int HeaderLength = 9;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int MinimumLength = HeaderLength + SaltLength + NonceLength + TagLength;

    public static bool IsLegacy(string stored) => !LooksVersioned(stored);

    public static EnvelopeParseResult Parse(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return new(EnvelopeStatus.Unreadable);
        if (!LooksVersioned(stored)) return new(EnvelopeStatus.Legacy);

        var dollar = stored.IndexOf('$');
        if (!int.TryParse(stored.AsSpan(3, dollar - 3), out var version)) return new(EnvelopeStatus.Unreadable);
        if (version > Version) return new(EnvelopeStatus.UnsupportedVersion);
        if (version != Version) return new(EnvelopeStatus.Unreadable);

        byte[] raw;
        try { raw = Base64UrlDecode(stored[(dollar + 1)..]); }
        catch (FormatException) { return new(EnvelopeStatus.Unreadable); }

        if (raw.Length < MinimumLength) return new(EnvelopeStatus.Unreadable);
        if (raw[0] != Version || raw[1] != KdfArgon2id) return new(EnvelopeStatus.Unreadable);

        var memory = (raw[2] << 24) | (raw[3] << 16) | (raw[4] << 8) | raw[5];
        var kdf = new KdfParameters(memory, raw[6], raw[7]);
        if (!KdfProfile.IsAtOrAboveFloor(kdf)) return new(EnvelopeStatus.Unreadable);

        if (raw[8] is not (1 or 2)) return new(EnvelopeStatus.Unreadable);
        var kind = (CredentialKind)raw[8];

        var envelope = new Envelope(
            kdf,
            kind,
            raw[HeaderLength..(HeaderLength + SaltLength)],
            raw[(HeaderLength + SaltLength)..(HeaderLength + SaltLength + NonceLength)],
            raw[(HeaderLength + SaltLength + NonceLength)..],
            raw[..HeaderLength]);
        return new(EnvelopeStatus.Ok, envelope);
    }

    /// <summary>Builds the header bytes used as AES-GCM additional authenticated data.</summary>
    public static byte[] BuildHeader(KdfParameters kdf, CredentialKind kind)
    {
        EnsureWritable(kdf, kind);
        return
        [
            Version, KdfArgon2id,
            (byte)(kdf.MemoryKiB >> 24), (byte)(kdf.MemoryKiB >> 16), (byte)(kdf.MemoryKiB >> 8), (byte)kdf.MemoryKiB,
            (byte)kdf.Passes, (byte)kdf.Lanes, (byte)kind,
        ];
    }

    public static string Format(KdfParameters kdf, CredentialKind kind, byte[] salt, byte[] nonce, byte[] ciphertextAndTag)
    {
        if (salt.Length != SaltLength) throw new ArgumentException("Salt must be 16 bytes.", nameof(salt));
        if (nonce.Length != NonceLength) throw new ArgumentException("Nonce must be 12 bytes.", nameof(nonce));
        if (ciphertextAndTag.Length < TagLength) throw new ArgumentException("Ciphertext must include the tag.", nameof(ciphertextAndTag));

        var header = BuildHeader(kdf, kind);
        var raw = new byte[HeaderLength + SaltLength + NonceLength + ciphertextAndTag.Length];
        header.CopyTo(raw, 0);
        salt.CopyTo(raw, HeaderLength);
        nonce.CopyTo(raw, HeaderLength + SaltLength);
        ciphertextAndTag.CopyTo(raw, HeaderLength + SaltLength + NonceLength);
        return Prefix + Base64UrlEncode(raw);
    }

    private static void EnsureWritable(KdfParameters kdf, CredentialKind kind)
    {
        if (!KdfProfile.IsAtOrAboveFloor(kdf))
            throw new ArgumentException("KDF parameters are below the minimum floor.", nameof(kdf));
        if (kind is not (CredentialKind.Pin or CredentialKind.Passphrase))
            throw new ArgumentException("Unknown credential kind.", nameof(kind));
    }

    // "dgb" + digits + "$". Legacy standard Base64 can never contain '$'.
    private static bool LooksVersioned(string s)
    {
        if (s.Length < 5 || !s.StartsWith("dgb", StringComparison.Ordinal)) return false;
        var i = 3;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        return i > 3 && i < s.Length && s[i] == '$';
    }

    private static string Base64UrlEncode(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        return Convert.FromBase64String(t);
    }
}

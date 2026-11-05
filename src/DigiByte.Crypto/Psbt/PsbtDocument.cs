using NBitcoin;

namespace DigiByte.Crypto.Psbt;

public sealed record PsbtLoadResult(PsbtDocument? Document, PsbtRefusal? Refusal);

/// <summary>
/// A decoded, untrusted PSBT. Loading only checks that the input is a well-formed PSBT; whether it is safe to sign is the
/// analyzer's job. Text must be base64 (research R11): hex is not offered, so it is refused rather than guessed at.
/// </summary>
public sealed class PsbtDocument
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly byte[] Magic = [0x70, 0x73, 0x62, 0x74, 0xFF];

    public PSBT Psbt { get; }
    public PsbtSourceFormat SourceFormat { get; }

    private PsbtDocument(PSBT psbt, PsbtSourceFormat sourceFormat)
    {
        Psbt = psbt;
        SourceFormat = sourceFormat;
    }

    public static PsbtLoadResult FromText(string? text, Network network)
    {
        if (text is not null && text.Length > MaxBytes)
            return Refuse(PsbtRefusalReason.TooLarge, "This is larger than 1 MB, which is far bigger than any PSBT this wallet signs.");

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Refuse(PsbtRefusalReason.NotAPsbt, "Paste a PSBT in base64 text, or upload a .psbt file.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            return Refuse(PsbtRefusalReason.NotAPsbt, "This isn't a PSBT. Paste it in base64 text, or upload a .psbt file.");
        }

        return Load(bytes, network, PsbtSourceFormat.Text);
    }

    public static PsbtLoadResult FromBytes(byte[] bytes, Network network)
    {
        if (bytes.Length > MaxBytes)
            return Refuse(PsbtRefusalReason.TooLarge, "This file is larger than 1 MB, which is far bigger than any PSBT this wallet signs.");

        return Load(bytes, network, PsbtSourceFormat.Binary);
    }

    private static PsbtLoadResult Load(byte[] bytes, Network network, PsbtSourceFormat format)
    {
        if (bytes.Length < Magic.Length || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return Refuse(PsbtRefusalReason.NotAPsbt, "This isn't a PSBT. Paste it in base64 text, or upload a .psbt file.");

        PSBT psbt;
        try
        {
            psbt = PSBT.Load(bytes, network);
        }
        catch (Exception)
        {
            // The magic matched, so this claims to be a PSBT but can't be read: damaged, truncated, or deliberately malformed.
            return Refuse(PsbtRefusalReason.InvalidStructure, "This PSBT is damaged or malformed and can't be read.");
        }

        var tx = psbt.GetGlobalTransaction();
        if (tx.Inputs.Count == 0)
            return Refuse(PsbtRefusalReason.InvalidStructure, "This PSBT has no inputs.");
        if (tx.Outputs.Count == 0)
            return Refuse(PsbtRefusalReason.InvalidStructure, "This PSBT has no outputs.");
        if (tx.Inputs.Select(i => i.PrevOut).Distinct().Count() != tx.Inputs.Count)
            return Refuse(PsbtRefusalReason.InvalidStructure, "This PSBT spends the same coin twice.");

        return new PsbtLoadResult(new PsbtDocument(psbt, format), null);
    }

    private static PsbtLoadResult Refuse(PsbtRefusalReason reason, string message) =>
        new(null, new PsbtRefusal(reason, message));
}

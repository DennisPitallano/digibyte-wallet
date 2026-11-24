namespace DigiByte.Wallet.Services;

/// <summary>
/// Whether a payment proposal is small enough to share as one QR code (specs/029-multisig-multiple-recipients, FR-015).
/// A QR code at error-correction level M holds at most 2,331 bytes; proposals are kept about 5% under that so a code is never
/// drawn at its limit. Larger proposals are shared by Copy, Save as file or a signing room instead.
/// </summary>
public static class QrCapacity
{
    /// <summary>The longest proposal (base64 PSBT text) shown as a QR code.</summary>
    public const int MaxProposalChars = 2200;

    public static bool FitsProposal(string? psbtBase64) =>
        !string.IsNullOrEmpty(psbtBase64) && psbtBase64.Length <= MaxProposalChars;
}

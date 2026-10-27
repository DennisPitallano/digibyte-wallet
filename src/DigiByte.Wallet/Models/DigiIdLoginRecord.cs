namespace DigiByte.Wallet.Models;

/// <summary>
/// One successful Digi-ID sign-in, as kept in the on-device login history.
/// Fields added after the first release are optional so older stored entries still load.
/// </summary>
public record DigiIdLoginRecord
{
    public required string Domain { get; init; }
    public required string Address { get; init; }
    public required DateTime Timestamp { get; init; }

    public string? CallbackUrl { get; init; }
    public string? Uri { get; init; }
    public string? Nonce { get; init; }
    public string? Signature { get; init; }
    public bool? IsUnsecure { get; init; }
}

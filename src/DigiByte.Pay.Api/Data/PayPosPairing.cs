namespace DigiByte.Pay.Api.Data;

/// <summary>
/// An open POS pairing code: made by the merchant in the dashboard, redeemed once by a
/// tablet at /v1/pay/pos/claim (see <c>Endpoints.PosEndpoints</c>). Kept in the database so
/// a code survives an API restart and can be claimed on any API instance.
///
/// Neither the code nor any key secret is stored: <see cref="CodeHash"/> is the SHA-256 of
/// the normalised code, and the tablet's key secret is only generated at claim time. The
/// key <see cref="KeyPrefix"/> is fixed here because the pairing response already shows it
/// (the dashboard watches /devices for it to learn when the code was used).
/// </summary>
public class PayPosPairing
{
    public required string Id { get; init; }
    /// <summary>SHA-256 hex of the normalised code (upper case, no spaces/dashes). Unique.</summary>
    public required string CodeHash { get; init; }
    public required string MerchantId { get; init; }
    public required string StoreId { get; init; }
    public required string StoreName { get; init; }
    public required string MerchantName { get; init; }
    public required string DeviceLabel { get; init; }
    /// <summary>"dgp_xxxxxxxxxx" — the prefix the claimed key will get.</summary>
    public required string KeyPrefix { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

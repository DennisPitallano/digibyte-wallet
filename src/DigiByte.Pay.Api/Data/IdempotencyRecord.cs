namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Stripe-style idempotency record. When a client sends an
/// <c>Idempotency-Key</c> header on session creation, we map
/// (merchant + key) → the original sessionId so a retried request
/// returns the same session instead of minting a new one.
///
/// Records are scoped to a merchant so two different merchants can
/// reuse the same key without collision; the unique constraint is on
/// (MerchantId, Key). TTL is 24 hours — older records are pruned by a
/// background sweep so the table stays small.
/// </summary>
public class IdempotencyRecord
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public required string Key { get; init; }
    public required string SessionId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

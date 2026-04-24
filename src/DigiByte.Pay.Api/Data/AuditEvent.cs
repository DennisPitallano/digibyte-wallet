namespace DigiByte.Pay.Api.Data;

/// <summary>
/// Append-only audit trail of sensitive merchant actions.
/// Written by <see cref="Services.AuditLogger"/> after the mutating call
/// succeeds. Never updated or deleted; merchants read it from the dashboard
/// to answer "who did what, when, from where" for compliance.
/// </summary>
public class AuditEvent
{
    public required string Id { get; init; }
    public required string MerchantId { get; init; }

    /// <summary>"ApiKey" (dgp_…), "Session" (dps_…, dashboard user), or "System" (background jobs).</summary>
    public required string ActorType { get; init; }

    /// <summary>Token prefix (never the secret). Null for System actors.</summary>
    public string? ActorId { get; init; }

    /// <summary>Client IP (honours ForwardedHeaders).</summary>
    public string? ActorIp { get; init; }

    /// <summary>Machine-readable action name, e.g. "session.refund", "store.delete", "key.create".</summary>
    public required string Action { get; init; }

    /// <summary>"Session" | "Store" | "ApiKey" | "Merchant".</summary>
    public required string TargetType { get; init; }

    /// <summary>Id of the affected entity (session id, store id, key id, merchant id).</summary>
    public required string TargetId { get; init; }

    /// <summary>Human-friendly one-liner shown in the audit UI.</summary>
    public string? Summary { get; set; }

    /// <summary>Optional JSON blob for extra context (txid, note, field diffs). Capped at 2 KB.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

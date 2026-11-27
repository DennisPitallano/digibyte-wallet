namespace DigiByte.Pay.Api.Data;

/// <summary>
/// One row per distinct DigiDollar account key across DigiPay (specs/035 data-model). Every store with the same key draws
/// receive indexes from this one sequence, so no DD address is ever given out twice.
/// </summary>
public class DdKeySequence
{
    /// <summary>The key's id: SHA-256 hex of <c>network:key</c> (64 chars).</summary>
    public required string Id { get; init; }

    /// <summary>"mainnet" or "testnet".</summary>
    public required string Network { get; init; }

    /// <summary>The next unused receive index. Only ever increases, and starts at 0.</summary>
    public int NextIndex { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

namespace DigiByte.P2P.Shared.Models;

public class UserReputation
{
    public required Guid UserId { get; set; }
    public required string Username { get; set; }
    public int TotalTrades { get; set; }
    public int CompletedTrades { get; set; }
    public decimal CompletionRate => TotalTrades == 0 ? 0 : (decimal)CompletedTrades / TotalTrades;
    public double AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public TimeSpan AverageReleaseTime { get; set; }
    public DateTime MemberSince { get; set; }
    public List<Badge> Badges { get; set; } = [];
}

public class TradeReview
{
    public Guid Id { get; set; }
    public required Guid TradeId { get; set; }
    public required Guid ReviewerId { get; set; }
    public required Guid RevieweeId { get; set; }
    public required int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum Badge
{
    Verified,
    Trusted,           // 10+ completed trades
    Experienced,       // 50+ completed trades
    PowerTrader,       // 100+ completed trades
    FastReleaser,      // Average release < 5 minutes
    PerfectRecord      // 100% completion rate with 20+ trades
}

public class Dispute
{
    public Guid Id { get; set; }
    public required Guid TradeId { get; set; }
    public required Guid InitiatorId { get; set; }
    public required string Reason { get; set; }
    public List<string> EvidenceUrls { get; set; } = [];
    public string? Resolution { get; set; }
    public Guid? ResolvedById { get; set; }
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public enum DisputeStatus
{
    Open,
    UnderReview,
    ResolvedForBuyer,
    ResolvedForSeller,
    Closed
}

namespace DigiByte.SpvGateway;

public enum GatewayNetwork
{
    Mainnet,
    Testnet,
}

/// <summary>
/// Deployment settings for the gateway, read from the <c>SpvGateway</c> configuration section. Anything missing or out of range falls
/// back to the default for that field, so a typo cannot silently turn a limit off.
/// </summary>
public sealed class SpvGatewayOptions
{
    public GatewayNetwork Network { get; init; } = GatewayNetwork.Mainnet;

    /// <summary>Folder for the SQLite files (headers, block index, peer book).</summary>
    public string DataDirectory { get; init; } = "data";

    /// <summary>How many days back the gateway answers scans directly (about 5,760 blocks a day at 15 seconds).</summary>
    public int ScanWindowDays { get; init; } = 90;

    public int MaxAddressesPerRequest { get; init; } = 100;

    public int MaxHeadersPerRequest { get; init; } = 2000;

    /// <summary>The most blocks one scan request may cover.</summary>
    public int ScanRangeCapBlocks { get; init; } = 50_000;

    /// <summary>How long one scan may fetch blocks before it answers with what it has (complete: false).</summary>
    public int ScanBudgetSeconds { get; init; } = 20;

    /// <summary>How many scans run at once. More wait their turn, so a heavy restore cannot starve the others (SC-009).</summary>
    public int MaxConcurrentScans { get; init; } = 4;

    /// <summary>How long a scan waits for a free slot before the gateway says it is busy.</summary>
    public int ScanQueueWaitSeconds { get; init; } = 10;

    /// <summary>Requests per client per minute before the gateway answers <c>rate_limited</c>.</summary>
    public int RateLimitPerMinute { get; init; } = 120;

    /// <summary>Connect to peers and download headers. Turned off by tests and by an operator who only wants to serve what is stored.</summary>
    public bool PeersEnabled { get; init; } = true;

    /// <summary>The number of peer connections the gateway aims for.</summary>
    public int PeerTarget { get; init; } = 8;

    /// <summary>Below this many connected peers the gateway reports itself as not healthy.</summary>
    public int MinHealthyPeers { get; init; } = 4;

    /// <summary>No more than this many peers from the same /16 address range.</summary>
    public int MaxPeersPerRange { get; init; } = 2;

    /// <summary>
    /// Take the client address from the first entry of X-Forwarded-For. Turn this on only behind a proxy that sets the header,
    /// because otherwise a client could choose its own address and dodge the rate limit.
    /// </summary>
    public bool TrustForwardedFor { get; init; }

    public static SpvGatewayOptions FromConfiguration(Func<string, string?> get)
    {
        var peerTarget = ReadInt(get, "SpvGateway:PeerTarget", 1, 64) ?? 8;
        var minHealthy = ReadInt(get, "SpvGateway:MinHealthyPeers", 1, 64) ?? 4;

        return new SpvGatewayOptions
        {
            Network = string.Equals(get("SpvGateway:Network")?.Trim(), "testnet", StringComparison.OrdinalIgnoreCase)
                ? GatewayNetwork.Testnet
                : GatewayNetwork.Mainnet,
            DataDirectory = string.IsNullOrWhiteSpace(get("SpvGateway:DataDirectory")) ? "data" : get("SpvGateway:DataDirectory")!.Trim(),
            ScanWindowDays = ReadInt(get, "SpvGateway:ScanWindowDays", 1, 3650) ?? 90,
            MaxAddressesPerRequest = ReadInt(get, "SpvGateway:MaxAddressesPerRequest", 1, 1000) ?? 100,
            MaxHeadersPerRequest = ReadInt(get, "SpvGateway:MaxHeadersPerRequest", 1, 2000) ?? 2000,
            ScanRangeCapBlocks = ReadInt(get, "SpvGateway:ScanRangeCapBlocks", 1000, 1_000_000) ?? 50_000,
            MaxConcurrentScans = ReadInt(get, "SpvGateway:MaxConcurrentScans", 1, 64) ?? 4,
            ScanQueueWaitSeconds = ReadInt(get, "SpvGateway:ScanQueueWaitSeconds", 1, 120) ?? 10,
            ScanBudgetSeconds = ReadInt(get, "SpvGateway:ScanBudgetSeconds", 1, 300) ?? 20,
            RateLimitPerMinute = ReadInt(get, "SpvGateway:RateLimitPerMinute", 1, 100_000) ?? 120,
            PeersEnabled = !string.Equals(get("SpvGateway:PeersEnabled")?.Trim(), "false", StringComparison.OrdinalIgnoreCase),
            PeerTarget = peerTarget,
            MinHealthyPeers = Math.Min(minHealthy, peerTarget),
            MaxPeersPerRange = ReadInt(get, "SpvGateway:MaxPeersPerRange", 1, 16) ?? 2,
            TrustForwardedFor = string.Equals(get("SpvGateway:TrustForwardedFor")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static int? ReadInt(Func<string, string?> get, string key, int min, int max)
    {
        var raw = get(key);
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw.Trim(), out var value))
            return null;
        return value < min || value > max ? null : value;
    }
}

namespace DigiByte.Wallet.Services;

/// <summary>
/// Deployment settings for the SPV gateway source (specs/011-p2p-spv-gateway), read from the <c>SpvGateway</c> configuration section.
/// Anything missing or out of range falls back to the default for that field. Off by default.
/// </summary>
public sealed class P2pGatewayOptions
{
    public const int DefaultCheckIntervalSeconds = 30;
    public const int DefaultStablePeriodSeconds = 120;
    public const int DefaultHeightTolerance = 5;
    public const int DefaultHealthTimeoutSeconds = 3;

    /// <summary>Off means today's behaviour and no health calls.</summary>
    public bool Enabled { get; init; }

    /// <summary>Absolute https origin of the gateway (http only for a local test gateway) without a trailing slash, or null.</summary>
    public string? ApiUrl { get; init; }

    public bool HasApiUrl => ApiUrl is not null;

    /// <summary>The position asked for. Use <see cref="Position"/> for the one that takes effect.</summary>
    public GatewayPosition RequestedPosition { get; init; } = GatewayPosition.Backup;

    /// <summary>The position that takes effect: a request for <c>First</c> is ignored until the wallet checks proof of work itself.</summary>
    public GatewayPosition Position => EffectivePosition(GatewayCapabilities.WalletPowVerified);

    public GatewayPosition EffectivePosition(bool walletPowVerified) =>
        GatewayCapabilities.EffectivePosition(RequestedPosition, walletPowVerified);

    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(DefaultCheckIntervalSeconds);

    /// <summary>How long the gateway must stay ready before it is used; at least two check intervals.</summary>
    public TimeSpan StablePeriod { get; init; } = TimeSpan.FromSeconds(DefaultStablePeriodSeconds);

    /// <summary>How many blocks the gateway may differ from a public explorer and still count as current (either direction).</summary>
    public int HeightTolerance { get; init; } = DefaultHeightTolerance;

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(DefaultHealthTimeoutSeconds);

    /// <summary>
    /// Ask the gateway to prove explorer-sourced transactions even while it is only a backup. Off by default because proof requests
    /// reveal transaction ids to the gateway.
    /// </summary>
    public bool BackgroundProving { get; init; }

    public static P2pGatewayOptions FromConfiguration(Func<string, string?> get)
    {
        var checkSeconds = NodeSourceOptions.ReadInt(get, "SpvGateway:CheckIntervalSeconds", 10, 300) ?? DefaultCheckIntervalSeconds;
        var minStable = checkSeconds * 2;
        var stableSeconds = NodeSourceOptions.ReadInt(get, "SpvGateway:StablePeriodSeconds", minStable, int.MaxValue)
                            ?? Math.Max(DefaultStablePeriodSeconds, minStable);

        return new P2pGatewayOptions
        {
            Enabled = IsTrue(get("SpvGateway:Enabled")),
            ApiUrl = NodeSourceOptions.NormalizeUrl(get("SpvGateway:ApiUrl")),
            RequestedPosition = string.Equals(get("SpvGateway:Position")?.Trim(), "first", StringComparison.OrdinalIgnoreCase)
                ? GatewayPosition.First
                : GatewayPosition.Backup,
            CheckInterval = TimeSpan.FromSeconds(checkSeconds),
            StablePeriod = TimeSpan.FromSeconds(stableSeconds),
            HeightTolerance = NodeSourceOptions.ReadInt(get, "SpvGateway:HeightTolerance", 1, 50) ?? DefaultHeightTolerance,
            HealthTimeout = TimeSpan.FromSeconds(NodeSourceOptions.ReadInt(get, "SpvGateway:HealthTimeoutSeconds", 1, 10) ?? DefaultHealthTimeoutSeconds),
            BackgroundProving = IsTrue(get("SpvGateway:BackgroundProving")),
        };
    }

    /// <summary>
    /// The same settings in the shape the existing readiness monitor takes. The gateway reports its health in the node's vocabulary
    /// (<c>starting</c>, <c>syncing</c>, <c>ok</c>, <c>unhealthy</c>), so one monitor implementation serves both sources.
    /// </summary>
    public NodeSourceOptions ToMonitorOptions() => new()
    {
        Enabled = Enabled,
        ApiUrl = ApiUrl,
        CheckInterval = CheckInterval,
        StablePeriod = StablePeriod,
        HeightTolerance = HeightTolerance,
        HealthTimeout = HealthTimeout,
    };

    private static bool IsTrue(string? raw) => string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

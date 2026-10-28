namespace DigiByte.Wallet.Services;

/// <summary>
/// Deployment settings for using our own node as a blockchain source (specs/005-wallet-own-node-source, data-model.md).
/// Read from the <c>OwnNode</c> configuration section; anything missing or out of range falls back to the default for that field.
/// </summary>
public sealed class NodeSourceOptions
{
    public const int DefaultCheckIntervalSeconds = 30;
    public const int DefaultStablePeriodSeconds = 120;
    public const int DefaultHeightTolerance = 5;
    public const int DefaultHealthTimeoutSeconds = 3;

    /// <summary>Off means today's behaviour and no health calls.</summary>
    public bool Enabled { get; init; }

    /// <summary>Absolute http(s) origin of the node API without a trailing slash, or null when missing or invalid.</summary>
    public string? ApiUrl { get; init; }

    public bool HasApiUrl => ApiUrl is not null;

    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(DefaultCheckIntervalSeconds);

    /// <summary>How long the node must stay ready before it is used; at least two check intervals.</summary>
    public TimeSpan StablePeriod { get; init; } = TimeSpan.FromSeconds(DefaultStablePeriodSeconds);

    /// <summary>How many blocks the node may differ from a public explorer and still count as current (either direction).</summary>
    public int HeightTolerance { get; init; } = DefaultHeightTolerance;

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(DefaultHealthTimeoutSeconds);

    /// <summary>The longest the explorer height lookup of one check may take. Not a deployment setting.</summary>
    public TimeSpan ExplorerBudget { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Shows the developer diagnostics page when true.</summary>
    public bool DevView { get; init; }

    public static NodeSourceOptions FromConfiguration(Func<string, string?> get)
    {
        var checkSeconds = ReadInt(get, "OwnNode:CheckIntervalSeconds", 10, 300) ?? DefaultCheckIntervalSeconds;
        var minStable = checkSeconds * 2;
        var stableSeconds = ReadInt(get, "OwnNode:StablePeriodSeconds", minStable, int.MaxValue)
                            ?? Math.Max(DefaultStablePeriodSeconds, minStable);

        return new NodeSourceOptions
        {
            Enabled = string.Equals(get("OwnNode:Enabled")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
            ApiUrl = NormalizeUrl(get("OwnNode:ApiUrl")),
            CheckInterval = TimeSpan.FromSeconds(checkSeconds),
            StablePeriod = TimeSpan.FromSeconds(stableSeconds),
            HeightTolerance = ReadInt(get, "OwnNode:HeightTolerance", 1, 50) ?? DefaultHeightTolerance,
            HealthTimeout = TimeSpan.FromSeconds(ReadInt(get, "OwnNode:HealthTimeoutSeconds", 1, 10) ?? DefaultHealthTimeoutSeconds),
            DevView = string.Equals(get("OwnNode:DevView")?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static int? ReadInt(Func<string, string?> get, string key, int min, int max)
    {
        var raw = get(key);
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw.Trim(), out var value))
            return null;
        return value < min || value > max ? null : value;
    }

    private static string? NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return raw.Trim().TrimEnd('/');
    }
}

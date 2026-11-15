using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Reads <c>{ApiUrl}/v1/health</c> from the SPV gateway and presents it as a node health result, so the existing readiness monitor can
/// judge it: one GET, a short timeout, no retries, and no exception ever reaches the caller. The gateway's status words
/// (<c>starting</c>, <c>syncing</c>, <c>ok</c>, <c>unhealthy</c>) are the node's, its header height stands in for the node's block height,
/// and it has no transaction index or version to check, so those count as satisfied.
/// </summary>
public sealed class GatewayHealthHttpSource : INodeHealthSource
{
    private readonly HttpClient _http;
    private readonly P2pGatewayOptions _options;

    public GatewayHealthHttpSource(HttpClient http, P2pGatewayOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ApiUrl is not { } baseUrl)
            return NodeHealthResult.Unreachable;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.HealthTimeout);

            using var response = await _http.GetAsync($"{baseUrl}/v1/health", timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return Parse((int)response.StatusCode, body);
        }
        catch (Exception)
        {
            // Includes timeouts, refused connections and cancellation: all mean "no usable answer".
            return NodeHealthResult.Unreachable;
        }
    }

    /// <summary>Turns an HTTP answer into a health result. Never throws.</summary>
    public static NodeHealthResult Parse(int statusCode, string? body)
    {
        if (statusCode < 200 || statusCode > 299)
            return NodeHealthResult.Unreachable;
        if (string.IsNullOrWhiteSpace(body))
            return NodeHealthResult.Unreadable;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return NodeHealthResult.Unreadable;

            var height = Int(root, "headerHeight");
            return new NodeHealthResult(
                Reachable: true,
                Malformed: false,
                Status: Text(root, "status")?.Trim().ToLowerInvariant(),
                Blocks: height,
                Headers: height,
                Lag: Int(root, "lagBlocks"),
                Peers: Int(root, "peerCount"),
                Txindex: true,
                VersionOk: true,
                NodeVersion: Text(root, "buildVersion"),
                RecommendedVersion: null);
        }
        catch (JsonException)
        {
            return NodeHealthResult.Unreadable;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}

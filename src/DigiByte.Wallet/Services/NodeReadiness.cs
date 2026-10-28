using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Reads the node's <c>/api/health</c> report and applies the ready predicate
/// (specs/005-wallet-own-node-source/contracts/node-readiness.md): reachable, <c>status == ok</c>, transaction index ready,
/// version acceptable, and the node's height within the tolerance of a public explorer (either direction).
/// Pure: no network, no clock.
/// </summary>
public static class NodeReadiness
{
    /// <summary>Turns an HTTP answer into a health result. Never throws; anything unreadable is reported as such.</summary>
    public static NodeHealthResult ParseHealth(int statusCode, string? body)
    {
        // The node API answers 503 when it cannot reach the node; any other failure status is treated the same way.
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

            return new NodeHealthResult(
                Reachable: true,
                Malformed: false,
                Status: Text(root, "status")?.Trim().ToLowerInvariant(),
                Blocks: Int(root, "blocks"),
                Headers: Int(root, "headers"),
                Lag: Int(root, "lag"),
                Peers: Int(root, "peers"),
                Txindex: Bool(root, "txindex") ?? false,
                VersionOk: Bool(root, "versionOk"),
                NodeVersion: Text(root, "nodeVersion"),
                RecommendedVersion: Text(root, "recommendedVersion"));
        }
        catch (JsonException)
        {
            return NodeHealthResult.Unreadable;
        }
    }

    /// <summary>Judges one health result and an explorer height. A missing explorer height makes an otherwise good report inconclusive.</summary>
    public static NodeReadinessSnapshot Evaluate(NodeHealthResult? health, int? explorerHeight, int tolerance, DateTime now)
    {
        NodeReadinessSnapshot Snap(bool ready, bool inconclusive, NodeNotReadyReason reason, int? delta = null) =>
            new(now, health, explorerHeight, delta, ready, inconclusive, reason);

        if (health is null || !health.Reachable)
            return Snap(false, false, NodeNotReadyReason.Unreachable);
        if (health.Malformed)
            return Snap(false, false, NodeNotReadyReason.Malformed);

        switch (health.Status)
        {
            case "starting": return Snap(false, false, NodeNotReadyReason.Starting);
            case "syncing": return Snap(false, false, NodeNotReadyReason.Syncing);
            case "unhealthy": return Snap(false, false, NodeNotReadyReason.Unhealthy);
            case "ok": break;
            default: return Snap(false, false, NodeNotReadyReason.Malformed);
        }

        if (!health.Txindex)
            return Snap(false, false, NodeNotReadyReason.Txindex);
        if (health.VersionOk == false)
            return Snap(false, false, NodeNotReadyReason.Version);
        if (health.Blocks is not { } blocks)
            return Snap(false, false, NodeNotReadyReason.Malformed);

        if (explorerHeight is not { } explorer)
            return Snap(false, true, NodeNotReadyReason.None);

        var delta = blocks - explorer;
        return Math.Abs(delta) > tolerance
            ? Snap(false, false, NodeNotReadyReason.HeightDrift, delta)
            : Snap(true, false, NodeNotReadyReason.None, delta);
    }

    /// <summary>The monitor state a not-ready reason leads to.</summary>
    public static NodeSourceState StateFor(NodeNotReadyReason reason) => reason switch
    {
        NodeNotReadyReason.Disabled => NodeSourceState.Disabled,
        NodeNotReadyReason.Unreachable or NodeNotReadyReason.Malformed => NodeSourceState.Unavailable,
        _ => NodeSourceState.NotReady,
    };

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static bool? Bool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}

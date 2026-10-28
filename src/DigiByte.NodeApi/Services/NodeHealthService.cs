using System.Text.Json;
using System.Text.RegularExpressions;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Services;

/// <summary>Calls one RPC method on the node and returns the raw result. Lets the health check be tested without a node.</summary>
public delegate Task<JsonElement> NodeRpcCall(string method);

/// <summary>
/// Health model for <c>GET /api/health</c> (specs/004-railway-digibyte-node/contracts/nodeapi-health.md).
/// Contains no credentials, addresses or file paths.
/// </summary>
public sealed record NodeHealth(
    string Status,
    string? NodeVersion,
    int? Blocks,
    int? Headers,
    int? Lag,
    int? Peers,
    bool Txindex,
    string? RecommendedVersion,
    bool VersionOk)
{
    public const string Starting = "starting";
    public const string Ok = "ok";
    public const string Syncing = "syncing";
    public const string Unhealthy = "unhealthy";

    /// <summary>200 for starting, syncing and ok (so a platform healthcheck does not restart a syncing service); 503 only when the node cannot be reached.</summary>
    public int HttpStatus => Status == Unhealthy ? 503 : 200;
}

/// <summary>
/// Builds the health model from a few cheap RPC calls (no walk over historical blocks).
/// <list type="bullet">
/// <item><c>starting</c>: the node answers but is still loading its block index (RPC error -28).</item>
/// <item><c>syncing</c>: initial block download, more than <see cref="MaxLagForOk"/> blocks behind the best header, or the transaction index is still building.</item>
/// <item><c>ok</c>: current and the transaction index is ready.</item>
/// <item><c>unhealthy</c>: the node cannot be reached or rejects the credentials.</item>
/// </list>
/// </summary>
public sealed partial class NodeHealthService
{
    /// <summary>Blocks behind the best header that still count as current (matches the within-3-blocks target).</summary>
    public const int MaxLagForOk = 3;

    private const int RpcWarmingUp = -28;

    private readonly NodeRpcCall _rpc;
    private readonly string? _recommendedVersion;

    public NodeHealthService(NodeRpcCall rpc, string? recommendedVersion)
    {
        _rpc = rpc;
        _recommendedVersion = string.IsNullOrWhiteSpace(recommendedVersion) ? null : recommendedVersion.Trim();
    }

    public async Task<NodeHealth> GetAsync()
    {
        JsonElement chain, network;
        try
        {
            chain = await _rpc("getblockchaininfo");
            network = await _rpc("getnetworkinfo");
        }
        catch (RpcException ex) when (ex.Code == RpcWarmingUp)
        {
            return Empty(NodeHealth.Starting);
        }
        catch (Exception)
        {
            // Unreachable node, rejected credentials or any other failure. Details stay in the server log, not the response.
            return Empty(NodeHealth.Unhealthy);
        }

        var blocks = Int(chain, "blocks");
        var headers = Int(chain, "headers");
        var lag = blocks is { } b && headers is { } h ? Math.Max(0, h - b) : (int?)null;
        var initialDownload = chain.TryGetProperty("initialblockdownload", out var ibd) && ibd.ValueKind == JsonValueKind.True;
        var peers = Int(network, "connections");
        var version = ParseVersion(network.TryGetProperty("subversion", out var sub) ? sub.GetString() : null);
        var txindex = await TxindexReadyAsync();

        var current = !initialDownload && lag is { } l && l <= MaxLagForOk;
        var status = current && txindex ? NodeHealth.Ok : NodeHealth.Syncing;

        return new NodeHealth(
            status, version, blocks, headers, lag, peers, txindex,
            _recommendedVersion, VersionIsAtLeastRecommended(version));
    }

    private NodeHealth Empty(string status) =>
        new(status, null, null, null, null, null, false, _recommendedVersion, true);

    private async Task<bool> TxindexReadyAsync()
    {
        try
        {
            var info = await _rpc("getindexinfo");
            return info.TryGetProperty("txindex", out var tx) &&
                   tx.TryGetProperty("synced", out var synced) &&
                   synced.ValueKind == JsonValueKind.True;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool VersionIsAtLeastRecommended(string? running)
    {
        if (_recommendedVersion is null)
            return true;
        if (!Version.TryParse(running, out var have) || !Version.TryParse(_recommendedVersion, out var want))
            return false;
        return have >= want;
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : null;

    /// <summary>Reads "9.26.7" out of a subversion such as "/DigiByte:9.26.7/"; falls back to the trimmed text.</summary>
    private static string? ParseVersion(string? subversion)
    {
        if (string.IsNullOrWhiteSpace(subversion))
            return null;
        var match = VersionPattern().Match(subversion);
        return match.Success ? match.Groups[1].Value : subversion.Trim('/');
    }

    [GeneratedRegex(@":(\d+\.\d+\.\d+)")]
    private static partial Regex VersionPattern();
}

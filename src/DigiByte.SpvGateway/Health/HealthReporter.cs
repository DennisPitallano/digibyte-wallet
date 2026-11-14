using System.Reflection;
using DigiByte.SpvGateway.Sync;

namespace DigiByte.SpvGateway.Health;

/// <summary>What the peer layer knows right now. Holds no wallet data.</summary>
/// <param name="PeerCount">Connected peers.</param>
/// <param name="FullHistoryPeerCount">Connected peers that keep the whole chain.</param>
/// <param name="FilterPeerCount">Connected peers that advertise compact filters.</param>
/// <param name="BestPeerHeight">The greatest chain height any connected peer reports, or null when unknown.</param>
/// <param name="EverConnected">True once at least one peer has connected since the gateway started.</param>
public sealed record PeerSnapshot(int PeerCount, int FullHistoryPeerCount, int FilterPeerCount, int? BestPeerHeight, bool EverConnected);

/// <summary>Source of the current <see cref="PeerSnapshot"/>. The peer layer provides the real one.</summary>
public interface IPeerStatusSource
{
    PeerSnapshot Snapshot();
}

/// <summary>Used until the peer layer exists: no peers.</summary>
public sealed class NoPeersStatusSource : IPeerStatusSource
{
    public PeerSnapshot Snapshot() => new(0, 0, 0, null, false);
}

/// <summary>The health report of the contract (GET /v1/health). Never contains wallet or address data.</summary>
public sealed record HealthReport(
    string Status,
    string Network,
    int HeaderHeight,
    int LagBlocks,
    int PeerCount,
    int FullHistoryPeerCount,
    int FilterPeerCount,
    string MatchingMode,
    int? IndexedFromHeight,
    string BuildVersion);

public static class HealthReporter
{
    /// <summary>More than this many blocks behind the best peer means the gateway is still catching up.</summary>
    public const int SyncingLagBlocks = 5;

    public static HealthReport Build(SpvGatewayOptions options, HeaderStore store, PeerSnapshot peers)
    {
        var tip = store.BestTip()?.Height ?? 0;
        var lag = peers.BestPeerHeight is { } best ? Math.Max(0, best - tip) : 0;

        string status;
        if (!peers.EverConnected)
            status = "starting";
        else if (peers.PeerCount < options.MinHealthyPeers)
            status = "unhealthy";
        else if (lag > SyncingLagBlocks)
            status = "syncing";
        else
            status = "ok";

        return new HealthReport(
            status,
            options.Network == GatewayNetwork.Testnet ? "testnet" : "mainnet",
            tip,
            lag,
            peers.PeerCount,
            peers.FullHistoryPeerCount,
            peers.FilterPeerCount,
            "blocks",
            null, // the block index does not exist yet; this becomes the first indexed height when it does
            Version);
    }

    public static string Version { get; } =
        typeof(HealthReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}

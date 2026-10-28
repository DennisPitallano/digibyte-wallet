using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T004: the interpretation table in specs/005-wallet-own-node-source/contracts/node-readiness.md.
/// </summary>
public class NodeReadinessTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static string Body(
        string status = "ok", int? blocks = 1000, bool txindex = true, string? versionOk = "true",
        int headers = 1000, int lag = 0, int peers = 8, string extra = "") =>
        "{" +
        $"\"status\":\"{status}\"," +
        (blocks is null ? "" : $"\"blocks\":{blocks},") +
        $"\"headers\":{headers},\"lag\":{lag},\"peers\":{peers},\"txindex\":{(txindex ? "true" : "false")}," +
        (versionOk is null ? "" : $"\"versionOk\":{versionOk},") +
        "\"nodeVersion\":\"9.26.7\",\"recommendedVersion\":\"9.26.7\"" + extra + "}";

    private static NodeReadinessSnapshot Judge(string body, int? explorerHeight = 1000, int tolerance = 5, int status = 200) =>
        NodeReadiness.Evaluate(NodeReadiness.ParseHealth(status, body), explorerHeight, tolerance, Now);

    // ---------- ParseHealth ----------

    [Fact]
    public void ParseHealth_ValidBody_ReadsTheFields()
    {
        var h = NodeReadiness.ParseHealth(200, Body(blocks: 24465504, headers: 24465505, lag: 1, peers: 18));

        Assert.True(h.Reachable);
        Assert.False(h.Malformed);
        Assert.Equal("ok", h.Status);
        Assert.Equal(24465504, h.Blocks);
        Assert.Equal(24465505, h.Headers);
        Assert.Equal(1, h.Lag);
        Assert.Equal(18, h.Peers);
        Assert.True(h.Txindex);
        Assert.True(h.VersionOk);
        Assert.Equal("9.26.7", h.NodeVersion);
        Assert.Equal("9.26.7", h.RecommendedVersion);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(0)]
    public void ParseHealth_FailureStatus_IsUnreachable(int status)
    {
        var h = NodeReadiness.ParseHealth(status, Body());

        Assert.False(h.Reachable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("<html>gateway</html>")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"status\":")]
    public void ParseHealth_UnreadableBody_IsMalformedNotAThrow(string? body)
    {
        var h = NodeReadiness.ParseHealth(200, body);

        Assert.True(h.Reachable);
        Assert.True(h.Malformed);
    }

    [Fact]
    public void ParseHealth_UnknownFieldsAreIgnored()
    {
        var h = NodeReadiness.ParseHealth(200, Body(extra: ",\"somethingNew\":{\"a\":1},\"another\":[1,2]"));

        Assert.False(h.Malformed);
        Assert.Equal("ok", h.Status);
    }

    [Fact]
    public void ParseHealth_StatusIsCaseInsensitive()
    {
        Assert.Equal("ok", NodeReadiness.ParseHealth(200, Body(status: "OK")).Status);
    }

    // ---------- Evaluate ----------

    [Fact]
    public void Ready_WhenOkIndexedVersionOkAndHeightWithinTolerance()
    {
        var s = Judge(Body(blocks: 1000), explorerHeight: 1003);

        Assert.True(s.Ready);
        Assert.False(s.Inconclusive);
        Assert.Equal(NodeNotReadyReason.None, s.Reason);
        Assert.Equal(-3, s.HeightDelta);
    }

    [Theory]
    [InlineData(1005, true)]   // exactly at tolerance, node behind
    [InlineData(995, true)]    // exactly at tolerance, node ahead
    [InlineData(1006, false)]  // just beyond, node behind
    [InlineData(994, false)]   // just beyond, node ahead
    public void HeightTolerance_IsSymmetricAndInclusive(int explorerHeight, bool ready)
    {
        var s = Judge(Body(blocks: 1000), explorerHeight, tolerance: 5);

        Assert.Equal(ready, s.Ready);
        if (!ready)
            Assert.Equal(NodeNotReadyReason.HeightDrift, s.Reason);
    }

    [Theory]
    [InlineData("starting", NodeNotReadyReason.Starting)]
    [InlineData("syncing", NodeNotReadyReason.Syncing)]
    [InlineData("unhealthy", NodeNotReadyReason.Unhealthy)]
    public void StatusOtherThanOk_IsNotReadyWithThatReason(string status, NodeNotReadyReason reason)
    {
        var s = Judge(Body(status: status));

        Assert.False(s.Ready);
        Assert.False(s.Inconclusive);
        Assert.Equal(reason, s.Reason);
    }

    [Theory]
    [InlineData("degraded")]
    [InlineData("")]
    [InlineData("connected")]
    public void UnknownStatus_IsMalformed(string status)
    {
        var s = Judge(Body(status: status));

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Malformed, s.Reason);
    }

    [Fact]
    public void MissingStatusField_IsMalformed()
    {
        var s = Judge("{\"blocks\":1000,\"txindex\":true}");

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Malformed, s.Reason);
    }

    [Fact]
    public void Ok_ButTxindexFalse_IsNotReadyTxindex()
    {
        var s = Judge(Body(txindex: false));

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Txindex, s.Reason);
    }

    [Fact]
    public void Ok_ButVersionOkFalse_IsNotReadyVersion()
    {
        var s = Judge(Body(versionOk: "false"));

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Version, s.Reason);
    }

    [Fact]
    public void AbsentVersionOk_IsTreatedAsTrue()
    {
        var s = Judge(Body(versionOk: null));

        Assert.True(s.Ready);
    }

    [Fact]
    public void Ok_WithoutBlocks_IsMalformed()
    {
        var s = Judge(Body(blocks: null));

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Malformed, s.Reason);
    }

    [Fact]
    public void NoExplorerHeight_IsInconclusive_NeitherReadyNorWrong()
    {
        var s = Judge(Body(), explorerHeight: null);

        Assert.False(s.Ready);
        Assert.True(s.Inconclusive);
        Assert.Equal(NodeNotReadyReason.None, s.Reason);
        Assert.Null(s.HeightDelta);
    }

    [Fact]
    public void NoExplorerHeight_DoesNotHideARealProblem()
    {
        // A node that says it is syncing is not ready whatever the explorer did.
        var s = Judge(Body(status: "syncing"), explorerHeight: null);

        Assert.False(s.Inconclusive);
        Assert.Equal(NodeNotReadyReason.Syncing, s.Reason);
    }

    [Fact]
    public void Unreachable_IsNotReady()
    {
        var s = NodeReadiness.Evaluate(NodeHealthResult.Unreachable, 1000, 5, Now);

        Assert.False(s.Ready);
        Assert.Equal(NodeNotReadyReason.Unreachable, s.Reason);
    }

    [Fact]
    public void NullHealth_IsUnreachable()
    {
        var s = NodeReadiness.Evaluate(null, 1000, 5, Now);

        Assert.Equal(NodeNotReadyReason.Unreachable, s.Reason);
    }

    [Fact]
    public void Unreadable_IsMalformed()
    {
        var s = NodeReadiness.Evaluate(NodeHealthResult.Unreadable, 1000, 5, Now);

        Assert.Equal(NodeNotReadyReason.Malformed, s.Reason);
    }

    [Fact]
    public void Snapshot_CarriesTheInputs()
    {
        var s = Judge(Body(blocks: 1000), explorerHeight: 1002);

        Assert.Equal(Now, s.CheckedAt);
        Assert.Equal(1002, s.ExplorerHeight);
        Assert.NotNull(s.Health);
    }

    [Theory]
    [InlineData(NodeNotReadyReason.Unreachable, NodeSourceState.Unavailable)]
    [InlineData(NodeNotReadyReason.Malformed, NodeSourceState.Unavailable)]
    [InlineData(NodeNotReadyReason.Starting, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.Syncing, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.Unhealthy, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.Txindex, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.Version, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.HeightDrift, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.ReadFailures, NodeSourceState.NotReady)]
    [InlineData(NodeNotReadyReason.Disabled, NodeSourceState.Disabled)]
    public void StateFor_MapsReasonsToStates(NodeNotReadyReason reason, NodeSourceState state)
    {
        Assert.Equal(state, NodeReadiness.StateFor(reason));
    }
}

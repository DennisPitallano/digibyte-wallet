using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// T008: the monitor's state machine (specs/005-wallet-own-node-source/data-model.md, "Monitor States and Transitions").
/// Fake clock, health source and explorer height source; nothing here touches the network.
/// </summary>
public class NodeSourceMonitorTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private sealed class Rig
    {
        public FakeClock Clock { get; } = new();
        public FakeHealthSource Health { get; } = new();
        public FakeExplorerHeightSource Explorer { get; } = new();
        public NodeSourceMonitor Monitor { get; }
        public List<(NodeSourceState State, NodeNotReadyReason Reason)> Changes { get; } = [];

        public Rig(bool enabled = true)
        {
            var options = new NodeSourceOptions
            {
                Enabled = enabled,
                ApiUrl = "https://node.example.org",
                CheckInterval = Interval,
                StablePeriod = TimeSpan.FromSeconds(120),
                HeightTolerance = 5,
                HealthTimeout = TimeSpan.FromMilliseconds(300),
                ExplorerBudget = TimeSpan.FromMilliseconds(300),
            };
            Monitor = new NodeSourceMonitor(options, Health, Explorer, Clock);
            Monitor.Changed += () => Changes.Add((Monitor.State, Monitor.Reason));
        }

        /// <summary>Runs one check, then lets one interval pass.</summary>
        public async Task Tick()
        {
            await Monitor.CheckAsync();
            Clock.Advance(Interval);
        }
    }

    // ---------- initial and disabled ----------

    [Fact]
    public async Task Disabled_NeverCallsAnySource_AndStaysDisabled()
    {
        var rig = new Rig(enabled: false);

        await rig.Monitor.CheckAsync();
        await rig.Monitor.CheckAsync();

        Assert.Equal(NodeSourceState.Disabled, rig.Monitor.State);
        Assert.Equal(0, rig.Health.Calls);
        Assert.Equal(0, rig.Explorer.Calls);
        Assert.False(rig.Monitor.IsActive);
    }

    [Fact]
    public void Enabled_StartsUnavailableUntilTheFirstCheck()
    {
        var rig = new Rig();

        Assert.Equal(NodeSourceState.Unavailable, rig.Monitor.State);
        Assert.False(rig.Monitor.IsActive);
        Assert.False(rig.Monitor.NodeWasActive);
        Assert.Null(rig.Monitor.LastSnapshot);
    }

    // ---------- promotion ----------

    [Fact]
    public async Task ReadyCheck_StartsTheStabilityWindow()
    {
        var rig = new Rig();

        await rig.Tick();

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
        Assert.False(rig.Monitor.IsActive);
    }

    [Fact]
    public async Task Ready_ForTheFullStablePeriod_BecomesActive()
    {
        var rig = new Rig();

        // checks at t = 0, 30, 60, 90 are still stabilizing; the check at t = 120 promotes
        for (var i = 0; i < 4; i++)
        {
            await rig.Tick();
            Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
        }
        await rig.Tick();

        Assert.Equal(NodeSourceState.Active, rig.Monitor.State);
        Assert.True(rig.Monitor.IsActive);
        Assert.True(rig.Monitor.NodeWasActive);
        Assert.Equal(NodeNotReadyReason.None, rig.Monitor.Reason);
    }

    [Fact]
    public async Task OneSecondShortOfTheStablePeriod_IsNotEnough()
    {
        var rig = new Rig();
        await rig.Monitor.CheckAsync();           // t = 0 -> stabilizing
        rig.Clock.Advance(TimeSpan.FromSeconds(119));

        await rig.Monitor.CheckAsync();

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
    }

    // ---------- demotion ----------

    [Theory]
    [InlineData("syncing", NodeSourceState.NotReady, NodeNotReadyReason.Syncing)]
    [InlineData("starting", NodeSourceState.NotReady, NodeNotReadyReason.Starting)]
    [InlineData("unhealthy", NodeSourceState.NotReady, NodeNotReadyReason.Unhealthy)]
    public async Task NotReadyCheck_FromStabilizing_LeavesImmediately(string status, NodeSourceState state, NodeNotReadyReason reason)
    {
        var rig = new Rig();
        await rig.Tick();
        rig.Health.Returns(FakeHealthSource.WithStatus(status));

        await rig.Tick();

        Assert.Equal(state, rig.Monitor.State);
        Assert.Equal(reason, rig.Monitor.Reason);
    }

    [Fact]
    public async Task NotReadyCheck_FromActive_LeavesOnTheNextCheck()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++) await rig.Tick();
        Assert.True(rig.Monitor.IsActive);
        rig.Health.Returns(FakeHealthSource.WithStatus("syncing"));

        await rig.Tick();

        Assert.False(rig.Monitor.IsActive);
        Assert.Equal(NodeSourceState.NotReady, rig.Monitor.State);
        Assert.True(rig.Monitor.NodeWasActive);   // stays true for the banner decision
    }

    [Fact]
    public async Task UnreachableNode_IsUnavailable()
    {
        var rig = new Rig();
        rig.Health.Returns(NodeHealthResult.Unreachable);

        await rig.Tick();

        Assert.Equal(NodeSourceState.Unavailable, rig.Monitor.State);
        Assert.Equal(NodeNotReadyReason.Unreachable, rig.Monitor.Reason);
    }

    [Fact]
    public async Task HeightDrift_FromActive_Demotes()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++) await rig.Tick();
        rig.Explorer.Returns(1100);   // explorer 100 blocks ahead of the node

        await rig.Tick();

        Assert.False(rig.Monitor.IsActive);
        Assert.Equal(NodeNotReadyReason.HeightDrift, rig.Monitor.Reason);
    }

    [Fact]
    public async Task AfterDemotion_ThePromotionNeedsTheFullStablePeriodAgain()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++) await rig.Tick();
        rig.Health.Returns(FakeHealthSource.WithStatus("syncing"));
        await rig.Tick();
        rig.Health.Returns(FakeHealthSource.Ok());

        await rig.Tick();

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
        Assert.False(rig.Monitor.IsActive);
    }

    // ---------- flapping ----------

    [Fact]
    public async Task FlappingEveryOtherCheck_NeverReachesActive()
    {
        var rig = new Rig();

        for (var i = 0; i < 40; i++)
        {
            rig.Health.Returns(i % 2 == 0 ? FakeHealthSource.Ok() : FakeHealthSource.WithStatus("syncing"));
            await rig.Tick();
            Assert.False(rig.Monitor.IsActive, $"became active at check {i}");
        }
        Assert.False(rig.Monitor.NodeWasActive);
    }

    // ---------- inconclusive checks (no explorer height) ----------

    [Fact]
    public async Task NoExplorerHeight_WhileActive_StaysActive()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++) await rig.Tick();
        rig.Explorer.Returns(null);

        for (var i = 0; i < 5; i++) await rig.Tick();

        Assert.True(rig.Monitor.IsActive);
    }

    [Fact]
    public async Task NoExplorerHeight_WhileStabilizing_DoesNotAdvance()
    {
        var rig = new Rig();
        await rig.Tick();
        rig.Explorer.Returns(null);

        for (var i = 0; i < 10; i++) await rig.Tick();

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
        Assert.False(rig.Monitor.NodeWasActive);
    }

    [Fact]
    public async Task NoExplorerHeight_FromTheStart_NeverActivates()
    {
        var rig = new Rig();
        rig.Explorer.Returns(null);

        for (var i = 0; i < 10; i++) await rig.Tick();

        Assert.False(rig.Monitor.IsActive);
        Assert.NotEqual(NodeSourceState.Stabilizing, rig.Monitor.State);
    }

    [Fact]
    public async Task NoExplorerHeight_AndNodeSyncing_StillReportsSyncing()
    {
        var rig = new Rig();
        rig.Explorer.Returns(null);
        rig.Health.Returns(FakeHealthSource.WithStatus("syncing"));

        await rig.Tick();

        Assert.Equal(NodeNotReadyReason.Syncing, rig.Monitor.Reason);
    }

    // ---------- Changed event ----------

    [Fact]
    public async Task Changed_FiresOnEachStateChange_AndOnlyThen()
    {
        var rig = new Rig();

        for (var i = 0; i < 8; i++) await rig.Tick();   // Stabilizing at 0, Active at 120, then steady
        rig.Health.Returns(FakeHealthSource.WithStatus("syncing"));
        await rig.Tick();
        await rig.Tick();                                // same state again: no new event

        Assert.Equal(
            [
                (NodeSourceState.Stabilizing, NodeNotReadyReason.None),
                (NodeSourceState.Active, NodeNotReadyReason.None),
                (NodeSourceState.NotReady, NodeNotReadyReason.Syncing),
            ],
            rig.Changes);
    }

    [Fact]
    public async Task Changed_FiresWhenOnlyTheReasonChanges()
    {
        var rig = new Rig();
        rig.Health.Returns(FakeHealthSource.WithStatus("starting"));
        await rig.Tick();
        rig.Health.Returns(FakeHealthSource.WithStatus("syncing"));

        await rig.Tick();

        Assert.Equal(2, rig.Changes.Count);
        Assert.Equal(NodeNotReadyReason.Syncing, rig.Changes[^1].Reason);
    }

    // ---------- read failures reported by the fallback service ----------

    private static async Task<Rig> ActiveRig()
    {
        var rig = new Rig();
        for (var i = 0; i < 5; i++) await rig.Tick();
        Assert.True(rig.Monitor.IsActive);
        rig.Changes.Clear();
        return rig;
    }

    [Fact]
    public async Task ThreeConsecutiveReadFailures_DemoteImmediately_WithoutACheck()
    {
        var rig = await ActiveRig();

        rig.Monitor.ReportNodeReadFailure();
        rig.Monitor.ReportNodeReadFailure();
        Assert.True(rig.Monitor.IsActive);
        rig.Monitor.ReportNodeReadFailure();

        Assert.False(rig.Monitor.IsActive);
        Assert.Equal(NodeSourceState.NotReady, rig.Monitor.State);
        Assert.Equal(NodeNotReadyReason.ReadFailures, rig.Monitor.Reason);
        Assert.Single(rig.Changes);
        Assert.True(rig.Monitor.NodeWasActive);
    }

    [Fact]
    public async Task ASuccessfulRead_ResetsTheFailureCount()
    {
        var rig = await ActiveRig();

        rig.Monitor.ReportNodeReadFailure();
        rig.Monitor.ReportNodeReadFailure();
        rig.Monitor.ReportNodeReadSuccess();
        rig.Monitor.ReportNodeReadFailure();
        rig.Monitor.ReportNodeReadFailure();

        Assert.True(rig.Monitor.IsActive);
    }

    [Fact]
    public async Task ReadFailures_WhenNotActive_AreIgnored()
    {
        var rig = new Rig();
        await rig.Tick();   // stabilizing

        for (var i = 0; i < 10; i++) rig.Monitor.ReportNodeReadFailure();

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
    }

    [Fact]
    public async Task AfterReadFailureDemotion_TheNextReadyCheckStartsAFreshWindow()
    {
        var rig = await ActiveRig();
        for (var i = 0; i < 3; i++) rig.Monitor.ReportNodeReadFailure();

        await rig.Tick();   // health still says ok

        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
        Assert.False(rig.Monitor.IsActive);
    }

    [Fact]
    public void ReadReports_WhenDisabled_DoNothing()
    {
        var rig = new Rig(enabled: false);

        for (var i = 0; i < 5; i++) rig.Monitor.ReportNodeReadFailure();

        Assert.Equal(NodeSourceState.Disabled, rig.Monitor.State);
        Assert.Empty(rig.Changes);
    }

    // ---------- robustness ----------

    [Fact]
    public async Task CheckNeverThrows_WhateverTheSourcesDo()
    {
        var rig = new Rig();
        rig.Health.Respond = _ => throw new InvalidOperationException("health boom");
        rig.Explorer.Respond = _ => throw new InvalidOperationException("explorer boom");

        await rig.Monitor.CheckAsync();

        Assert.Equal(NodeSourceState.Unavailable, rig.Monitor.State);
    }

    [Fact]
    public async Task ExplorerThrowing_IsAnInconclusiveCheck_NotANodeFailure()
    {
        var rig = new Rig();
        rig.Explorer.Respond = _ => throw new InvalidOperationException("explorer boom");

        await rig.Monitor.CheckAsync();

        // The node answered fine; only the cross-check is missing, so the check is inconclusive and the node is not blamed.
        Assert.True(rig.Monitor.LastSnapshot!.Inconclusive);
        Assert.NotEqual(NodeNotReadyReason.Unreachable, rig.Monitor.LastSnapshot.Reason);
        Assert.NotNull(rig.Monitor.LastSnapshot.Health);
        Assert.True(rig.Monitor.LastSnapshot.Health!.Reachable);
    }

    [Fact]
    public async Task HealthAndExplorerLookups_RunInParallel()
    {
        var rig = new Rig();
        rig.Health.Respond = async _ => { await Task.Delay(200); return FakeHealthSource.Ok(); };
        rig.Explorer.Respond = async _ => { await Task.Delay(200); return 1000; };

        var started = DateTime.UtcNow;
        await rig.Monitor.CheckAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(380), $"took {elapsed}");
        Assert.Equal(NodeSourceState.Stabilizing, rig.Monitor.State);
    }

    [Fact]
    public async Task AWholeCheck_NeverTakesLongerThanTheHealthTimeoutPlusTheExplorerBudget()
    {
        var rig = new Rig();   // health timeout 300 ms, explorer budget 300 ms
        rig.Health.Respond = async ct => { await Task.Delay(Timeout.Infinite, ct); return FakeHealthSource.Ok(); };
        rig.Explorer.Respond = async ct => { await Task.Delay(Timeout.Infinite, ct); return 1000; };

        var started = DateTime.UtcNow;
        await rig.Monitor.CheckAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"took {elapsed}");
        Assert.Equal(NodeSourceState.Unavailable, rig.Monitor.State);
    }

    [Fact]
    public async Task LastSnapshot_IsUpdatedByEveryCheck()
    {
        var rig = new Rig();

        await rig.Tick();
        var first = rig.Monitor.LastSnapshot;
        await rig.Tick();

        Assert.NotNull(first);
        Assert.True(rig.Monitor.LastSnapshot!.CheckedAt > first!.CheckedAt);
    }
}

using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>T024: the fallback service says when every source failed, without changing the error it throws.</summary>
public class FallbackAllSourcesFailedTests
{
    private static FallbackBlockchainService Sut(ScriptedExplorer explorer, FakeNodeApi node, FakeNodeState? state = null) =>
        new(node.Client(strict: state is not null), [explorer], new HttpClient(), isDevelopment: false,
            nodeState: state, failureSink: state);

    [Fact]
    public async Task IsFalseAtStart()
    {
        var sut = Sut(new ScriptedExplorer("e"), new FakeNodeApi(), new FakeNodeState());

        Assert.False(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task IsSet_WhenAReadExhaustsEverySource()
    {
        var explorer = new ScriptedExplorer("e") { Fails = true };
        var sut = Sut(explorer, new FakeNodeApi { Fails = true }, new FakeNodeState { State = NodeSourceState.NotReady });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetBlockHeightAsync());

        Assert.True(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task ThrownErrorTypeAndMessage_AreUnchanged()
    {
        var explorer = new ScriptedExplorer("e") { Fails = true };
        var sut = Sut(explorer, new FakeNodeApi { Fails = true }, new FakeNodeState());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetBalanceAsync("DAbc"));

        Assert.Contains("All blockchain backends are unavailable", ex.Message);
    }

    // A failed explorer is put on a two minute cooldown, so recovery is shown through the active node, which is the address fallback.
    [Fact]
    public async Task IsCleared_ByTheNextSuccessfulRead()
    {
        var explorer = new ScriptedExplorer("e") { Fails = true };
        var node = new FakeNodeApi { Fails = true };
        var sut = Sut(explorer, node, new FakeNodeState { State = NodeSourceState.Active });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetUtxosAsync("DAbc"));
        Assert.True(sut.AllSourcesFailed);

        node.Fails = false;
        await sut.GetUtxosAsync("DAbc");

        Assert.False(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task Event_FiresOnEachChange_AndOnlyThen()
    {
        var explorer = new ScriptedExplorer("e") { Fails = true };
        var node = new FakeNodeApi { Fails = true };
        var sut = Sut(explorer, node, new FakeNodeState { State = NodeSourceState.Active });
        var events = 0;
        sut.AllSourcesFailedChanged += () => events++;

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetUtxosAsync("DAbc"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetUtxosAsync("DAbc"));   // still failed: no new event
        Assert.Equal(1, events);

        node.Fails = false;
        await sut.GetUtxosAsync("DAbc");
        await sut.GetUtxosAsync("DAbc");                                                        // still fine: no new event
        Assert.Equal(2, events);
    }

    [Fact]
    public async Task ACachedRead_DoesNotClearTheFlag()
    {
        var explorer = new ScriptedExplorer("e");
        var sut = Sut(explorer, new FakeNodeApi(), new FakeNodeState());
        await sut.GetBalanceAsync("DAbc");        // cached for 30 s
        explorer.Fails = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetUtxosAsync("DAbc"));
        Assert.True(sut.AllSourcesFailed);

        await sut.GetBalanceAsync("DAbc");        // served from the cache, no source was tried

        Assert.True(sut.AllSourcesFailed);
    }

    [Fact]
    public async Task WorksWithTheFeatureOffToo_WithoutChangingItsBehaviour()
    {
        var explorer = new ScriptedExplorer("e") { Fails = true };
        var node = new FakeNodeApi { Fails = true };
        var sut = new FallbackBlockchainService(node.Client(strict: false), [explorer], new HttpClient(), isDevelopment: false);

        // Today's lenient node answers with a neutral value, so a read succeeds (as it always did)
        Assert.Equal(0, await sut.GetBalanceAsync("DAbc"));

        Assert.False(sut.AllSourcesFailed);
    }
}

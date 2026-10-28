using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>T010: the explorer height lookup used for the node cross-check.</summary>
public class ExplorerHeightSourceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task ReturnsTheFirstExplorersAnswer_AndStops()
    {
        var first = new HeightOnlyExplorer(() => Task.FromResult(1000));
        var second = new HeightOnlyExplorer(() => Task.FromResult(2000));
        var sut = new ExplorerHeightSource([first, second], Budget);

        var height = await sut.GetHeightAsync();

        Assert.Equal(1000, height);
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task SkipsAnExplorerThatThrows()
    {
        var broken = new HeightOnlyExplorer(() => throw new HttpRequestException("down"));
        var working = new HeightOnlyExplorer(() => Task.FromResult(1234));
        var sut = new ExplorerHeightSource([broken, working], Budget);

        Assert.Equal(1234, await sut.GetHeightAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ZeroOrNegativeHeight_CountsAsNoAnswer(int bogus)
    {
        var bad = new HeightOnlyExplorer(() => Task.FromResult(bogus));
        var good = new HeightOnlyExplorer(() => Task.FromResult(777));
        var sut = new ExplorerHeightSource([bad, good], Budget);

        Assert.Equal(777, await sut.GetHeightAsync());
    }

    [Fact]
    public async Task ReturnsNull_WhenEveryExplorerFails()
    {
        var a = new HeightOnlyExplorer(() => throw new Exception("a"));
        var b = new HeightOnlyExplorer(() => Task.FromResult(0));
        var sut = new ExplorerHeightSource([a, b], Budget);

        Assert.Null(await sut.GetHeightAsync());
    }

    [Fact]
    public async Task ReturnsNull_WhenThereAreNoExplorers()
    {
        var sut = new ExplorerHeightSource([], Budget);

        Assert.Null(await sut.GetHeightAsync());
    }

    [Fact]
    public async Task AHangingLookup_IsAbandonedAfterTheBudget_AndReturnsNull()
    {
        var hanging = new HeightOnlyExplorer(async () => { await Task.Delay(Timeout.Infinite); return 1; });
        var sut = new ExplorerHeightSource([hanging], Budget);

        var started = DateTime.UtcNow;
        var height = await sut.GetHeightAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.Null(height);
        Assert.True(elapsed < TimeSpan.FromSeconds(2), $"took {elapsed}");
    }

    [Fact]
    public async Task TheBudgetCoversTheWholeCall_NotEachExplorer()
    {
        var one = new HeightOnlyExplorer(async () => { await Task.Delay(Timeout.Infinite); return 1; });
        var two = new HeightOnlyExplorer(async () => { await Task.Delay(Timeout.Infinite); return 2; });
        var sut = new ExplorerHeightSource([one, two], Budget);

        var started = DateTime.UtcNow;
        await sut.GetHeightAsync();
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"took {elapsed}");
    }

    [Fact]
    public async Task CallerCancellation_ReturnsNull_NotAnException()
    {
        var hanging = new HeightOnlyExplorer(async () => { await Task.Delay(Timeout.Infinite); return 1; });
        var sut = new ExplorerHeightSource([hanging], TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.Null(await sut.GetHeightAsync(cts.Token));
    }
}

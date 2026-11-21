using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The developer switches count only while the developer view is on (spec 024, FR-013a, T010a).</summary>
public class HistorySyncOptionsTests
{
    [Theory]
    [InlineData("false", "false")]
    [InlineData("true", "true")]
    [InlineData(null, null)]
    public void DevViewOff_AlwaysUsesTheDefaults(string? changeOnly, string? tiered)
    {
        var options = HistorySyncOptions.FromStorage(devView: false, changeOnly, tiered);

        Assert.True(options.ChangeOnly);
        Assert.True(options.Tiered);
        Assert.True(options.TieringActive);
    }

    [Fact]
    public void DevViewOn_ASavedFalseTurnsThatSwitchOff()
    {
        var options = HistorySyncOptions.FromStorage(devView: true, changeOnlyStored: "true", tieredStored: "false");

        Assert.True(options.ChangeOnly);
        Assert.False(options.Tiered);
        Assert.False(options.TieringActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("not a bool")]
    [InlineData("")]
    public void DevViewOn_MissingOrUnreadable_LeavesItOn(string? stored)
    {
        var options = HistorySyncOptions.FromStorage(devView: true, stored, stored);

        Assert.True(options.ChangeOnly);
        Assert.True(options.Tiered);
    }

    [Fact]
    public void TieringNeedsChangeOnly()
    {
        var options = HistorySyncOptions.FromStorage(devView: true, changeOnlyStored: "false", tieredStored: "true");

        Assert.False(options.ChangeOnly);
        Assert.True(options.Tiered);
        Assert.False(options.TieringActive);
    }
}

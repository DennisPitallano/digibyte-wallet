using DigiByte.NodeApi;

namespace DigiByte.NodeApi.Tests;

/// <summary>T033 (FR-015): the health response carries the commit the build came from, "unknown" when the platform does not provide it.</summary>
public class BuildInfoTests
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("e6c9031abc", "e6c9031abc")]
    [InlineData("  e6c9031abc\n", "e6c9031abc")]
    public void Resolve_ReturnsTheTrimmedValueOrUnknown(string? value, string expected) =>
        Assert.Equal(expected, BuildInfo.Resolve(value));

    [Fact]
    public void Commit_ReadsTheRailwayVariable()
    {
        var previous = Environment.GetEnvironmentVariable(BuildInfo.Variable);
        try
        {
            Environment.SetEnvironmentVariable(BuildInfo.Variable, "abc123def");
            Assert.Equal("abc123def", BuildInfo.Commit);
            Environment.SetEnvironmentVariable(BuildInfo.Variable, null);
            Assert.Equal("unknown", BuildInfo.Commit);
        }
        finally { Environment.SetEnvironmentVariable(BuildInfo.Variable, previous); }
    }
}

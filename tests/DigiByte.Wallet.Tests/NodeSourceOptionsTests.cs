using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>T002: defaults, clamping and URL validation for the node-source deployment settings.</summary>
public class NodeSourceOptionsTests
{
    private static NodeSourceOptions From(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return NodeSourceOptions.FromConfiguration(key => map.TryGetValue(key, out var v) ? v : null);
    }

    [Fact]
    public void Defaults_WhenNothingIsConfigured()
    {
        var o = From();

        Assert.False(o.Enabled);
        Assert.Null(o.ApiUrl);
        Assert.Equal(TimeSpan.FromSeconds(30), o.CheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(120), o.StablePeriod);
        Assert.Equal(5, o.HeightTolerance);
        Assert.Equal(TimeSpan.FromSeconds(3), o.HealthTimeout);
        Assert.False(o.DevView);
    }

    [Fact]
    public void ValidValues_AreUsed()
    {
        var o = From(
            ("OwnNode:Enabled", "true"),
            ("OwnNode:ApiUrl", "https://node.example.org/"),
            ("OwnNode:CheckIntervalSeconds", "20"),
            ("OwnNode:StablePeriodSeconds", "90"),
            ("OwnNode:HeightTolerance", "8"),
            ("OwnNode:HealthTimeoutSeconds", "4"),
            ("OwnNode:DevView", "true"));

        Assert.True(o.Enabled);
        Assert.Equal("https://node.example.org", o.ApiUrl);
        Assert.Equal(TimeSpan.FromSeconds(20), o.CheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(90), o.StablePeriod);
        Assert.Equal(8, o.HeightTolerance);
        Assert.Equal(TimeSpan.FromSeconds(4), o.HealthTimeout);
        Assert.True(o.DevView);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("301")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    public void CheckInterval_OutOfRange_FallsBackToDefault(string value)
    {
        Assert.Equal(TimeSpan.FromSeconds(30), From(("OwnNode:CheckIntervalSeconds", value)).CheckInterval);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("300")]
    public void CheckInterval_Bounds_AreAccepted(string value)
    {
        Assert.Equal(TimeSpan.FromSeconds(int.Parse(value)), From(("OwnNode:CheckIntervalSeconds", value)).CheckInterval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("-1")]
    [InlineData("x")]
    public void HeightTolerance_OutOfRange_FallsBackToDefault(string value)
    {
        Assert.Equal(5, From(("OwnNode:HeightTolerance", value)).HeightTolerance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("x")]
    public void HealthTimeout_OutOfRange_FallsBackToDefault(string value)
    {
        Assert.Equal(TimeSpan.FromSeconds(3), From(("OwnNode:HealthTimeoutSeconds", value)).HealthTimeout);
    }

    [Fact]
    public void StablePeriod_ShorterThanTwoCheckIntervals_FallsBackToTheDefaultOrTwoIntervals()
    {
        // 50 s is below two 30 s intervals (60 s), so the default (120 s) applies.
        Assert.Equal(TimeSpan.FromSeconds(120), From(("OwnNode:StablePeriodSeconds", "50")).StablePeriod);

        // With a 200 s interval the default 120 s is itself too short, so two intervals (400 s) apply.
        var o = From(("OwnNode:CheckIntervalSeconds", "200"));
        Assert.Equal(TimeSpan.FromSeconds(400), o.StablePeriod);
    }

    [Fact]
    public void StablePeriod_ExactlyTwoIntervals_IsAccepted()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), From(("OwnNode:StablePeriodSeconds", "60")).StablePeriod);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("ftp://node.example.org")]
    [InlineData("http://node.example.org")]
    [InlineData("http://203.0.113.5:5260")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    public void ApiUrl_EmptyOrNotAbsoluteHttp_IsNullAndNeverThrows(string value)
    {
        var o = From(("OwnNode:ApiUrl", value));

        Assert.Null(o.ApiUrl);
        Assert.False(o.HasApiUrl);
    }

    [Theory]
    [InlineData("http://localhost:5260", "http://localhost:5260")]
    [InlineData("http://127.0.0.1:5260", "http://127.0.0.1:5260")]
    [InlineData("https://node.example.org/", "https://node.example.org")]
    public void ApiUrl_AbsoluteHttp_IsAcceptedWithoutTrailingSlash(string value, string expected)
    {
        var o = From(("OwnNode:ApiUrl", value));

        Assert.Equal(expected, o.ApiUrl);
        Assert.True(o.HasApiUrl);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void Enabled_OnlyAcceptsTrue(string value, bool expected)
    {
        Assert.Equal(expected, From(("OwnNode:Enabled", value)).Enabled);
    }
}

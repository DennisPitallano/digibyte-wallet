using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Deployment settings for the SPV gateway source (specs/011-p2p-spv-gateway, T039 and T042).</summary>
public class P2pGatewayOptionsTests
{
    private static P2pGatewayOptions From(Dictionary<string, string> values) =>
        P2pGatewayOptions.FromConfiguration(key => values.TryGetValue(key, out var v) ? v : null);

    [Fact]
    public void Everything_is_off_or_conservative_by_default()
    {
        var o = From([]);

        Assert.False(o.Enabled);
        Assert.Null(o.ApiUrl);
        Assert.False(o.HasApiUrl);
        Assert.Equal(GatewayPosition.Backup, o.RequestedPosition);
        Assert.Equal(GatewayPosition.Backup, o.Position);
        Assert.False(o.BackgroundProving);
        Assert.Equal(TimeSpan.FromSeconds(30), o.CheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(120), o.StablePeriod);
        Assert.Equal(5, o.HeightTolerance);
        Assert.Equal(TimeSpan.FromSeconds(3), o.HealthTimeout);
    }

    [Fact]
    public void Valid_values_are_used()
    {
        var o = From(new()
        {
            ["SpvGateway:Enabled"] = "true",
            ["SpvGateway:ApiUrl"] = "https://gateway.example.org/",
            ["SpvGateway:Position"] = "first",
            ["SpvGateway:CheckIntervalSeconds"] = "20",
            ["SpvGateway:StablePeriodSeconds"] = "300",
            ["SpvGateway:HeightTolerance"] = "3",
            ["SpvGateway:HealthTimeoutSeconds"] = "5",
            ["SpvGateway:BackgroundProving"] = "true",
        });

        Assert.True(o.Enabled);
        Assert.Equal("https://gateway.example.org", o.ApiUrl);
        Assert.Equal(GatewayPosition.First, o.RequestedPosition);
        Assert.Equal(TimeSpan.FromSeconds(20), o.CheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(300), o.StablePeriod);
        Assert.Equal(3, o.HeightTolerance);
        Assert.Equal(TimeSpan.FromSeconds(5), o.HealthTimeout);
        Assert.True(o.BackgroundProving);
    }

    [Theory]
    [InlineData("SpvGateway:CheckIntervalSeconds", "1")]
    [InlineData("SpvGateway:CheckIntervalSeconds", "9999")]
    [InlineData("SpvGateway:HeightTolerance", "0")]
    [InlineData("SpvGateway:HeightTolerance", "500")]
    [InlineData("SpvGateway:HealthTimeoutSeconds", "0")]
    [InlineData("SpvGateway:HealthTimeoutSeconds", "abc")]
    public void Out_of_range_or_unreadable_values_fall_back_to_the_default(string key, string value)
    {
        var o = From(new() { [key] = value });

        Assert.Equal(TimeSpan.FromSeconds(30), o.CheckInterval);
        Assert.Equal(5, o.HeightTolerance);
        Assert.Equal(TimeSpan.FromSeconds(3), o.HealthTimeout);
    }

    [Fact]
    public void The_stable_period_is_never_shorter_than_two_check_intervals()
    {
        var o = From(new() { ["SpvGateway:CheckIntervalSeconds"] = "100", ["SpvGateway:StablePeriodSeconds"] = "50" });

        Assert.True(o.StablePeriod >= TimeSpan.FromSeconds(200));
    }

    [Theory]
    [InlineData("https://gateway.example.org")]
    [InlineData("http://localhost:5290")]
    [InlineData("http://127.0.0.1:5290")]
    public void Secure_or_local_origins_are_accepted(string url)
    {
        Assert.NotNull(From(new() { ["SpvGateway:ApiUrl"] = url }).ApiUrl);
    }

    [Theory]
    [InlineData("http://gateway.example.org")]
    [InlineData("ftp://gateway.example.org")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Plain_http_to_a_remote_host_and_other_schemes_are_refused(string url)
    {
        Assert.Null(From(new() { ["SpvGateway:ApiUrl"] = url }).ApiUrl);
    }

    [Fact]
    public void A_request_for_first_is_ignored_until_the_wallet_checks_proof_of_work_itself()
    {
        var o = From(new() { ["SpvGateway:Position"] = "first" });

        Assert.Equal(GatewayPosition.First, o.RequestedPosition);
        Assert.Equal(GatewayPosition.Backup, o.EffectivePosition(walletPowVerified: false));
        Assert.Equal(GatewayPosition.First, o.EffectivePosition(walletPowVerified: true));
        Assert.False(GatewayCapabilities.WalletPowVerified);
        Assert.Equal(GatewayPosition.Backup, o.Position);
    }

    [Fact]
    public void An_unknown_position_means_backup()
    {
        Assert.Equal(GatewayPosition.Backup, From(new() { ["SpvGateway:Position"] = "primary" }).RequestedPosition);
    }

    [Fact]
    public void The_monitor_options_carry_the_same_settings()
    {
        var o = From(new() { ["SpvGateway:Enabled"] = "true", ["SpvGateway:ApiUrl"] = "https://gateway.example.org", ["SpvGateway:HeightTolerance"] = "4" });

        var monitor = o.ToMonitorOptions();

        Assert.True(monitor.Enabled);
        Assert.Equal("https://gateway.example.org", monitor.ApiUrl);
        Assert.Equal(4, monitor.HeightTolerance);
        Assert.Equal(o.CheckInterval, monitor.CheckInterval);
        Assert.Equal(o.StablePeriod, monitor.StablePeriod);
        Assert.Equal(o.HealthTimeout, monitor.HealthTimeout);
    }
}

using DigiByte.SpvGateway;

namespace DigiByte.SpvGateway.Tests;

public class SpvGatewayOptionsTests
{
    private static SpvGatewayOptions From(Dictionary<string, string> values) =>
        SpvGatewayOptions.FromConfiguration(key => values.TryGetValue(key, out var v) ? v : null);

    [Fact]
    public void Defaults_match_the_spec()
    {
        var o = From([]);

        Assert.Equal(GatewayNetwork.Mainnet, o.Network);
        Assert.Equal(90, o.ScanWindowDays);
        Assert.Equal(100, o.MaxAddressesPerRequest);
        Assert.Equal(2000, o.MaxHeadersPerRequest);
        Assert.Equal(50_000, o.ScanRangeCapBlocks);
        Assert.Equal(8, o.PeerTarget);
        Assert.Equal(4, o.MinHealthyPeers);
        Assert.Equal(2, o.MaxPeersPerRange);
        Assert.Equal("data", o.DataDirectory);
    }

    [Fact]
    public void Valid_values_are_used()
    {
        var o = From(new()
        {
            ["SpvGateway:Network"] = "testnet",
            ["SpvGateway:ScanWindowDays"] = "30",
            ["SpvGateway:MaxAddressesPerRequest"] = "50",
            ["SpvGateway:ScanRangeCapBlocks"] = "10000",
            ["SpvGateway:RateLimitPerMinute"] = "30",
            ["SpvGateway:DataDirectory"] = "/data",
        });

        Assert.Equal(GatewayNetwork.Testnet, o.Network);
        Assert.Equal(30, o.ScanWindowDays);
        Assert.Equal(50, o.MaxAddressesPerRequest);
        Assert.Equal(10_000, o.ScanRangeCapBlocks);
        Assert.Equal(30, o.RateLimitPerMinute);
        Assert.Equal("/data", o.DataDirectory);
    }

    [Theory]
    [InlineData("SpvGateway:ScanWindowDays", "0")]
    [InlineData("SpvGateway:ScanWindowDays", "100000")]
    [InlineData("SpvGateway:ScanWindowDays", "abc")]
    [InlineData("SpvGateway:MaxAddressesPerRequest", "0")]
    [InlineData("SpvGateway:MaxAddressesPerRequest", "5000")]
    [InlineData("SpvGateway:MaxHeadersPerRequest", "2001")]
    [InlineData("SpvGateway:ScanRangeCapBlocks", "5")]
    public void Out_of_range_or_unreadable_values_fall_back_to_the_default(string key, string value)
    {
        var o = From(new() { [key] = value });

        Assert.Equal(90, o.ScanWindowDays);
        Assert.Equal(100, o.MaxAddressesPerRequest);
        Assert.Equal(2000, o.MaxHeadersPerRequest);
        Assert.Equal(50_000, o.ScanRangeCapBlocks);
    }

    [Fact]
    public void An_unknown_network_name_falls_back_to_mainnet()
    {
        Assert.Equal(GatewayNetwork.Mainnet, From(new() { ["SpvGateway:Network"] = "regtest" }).Network);
        Assert.Equal(GatewayNetwork.Mainnet, From(new() { ["SpvGateway:Network"] = "" }).Network);
    }

    [Fact]
    public void Healthy_peer_minimum_never_exceeds_the_peer_target()
    {
        var o = From(new() { ["SpvGateway:PeerTarget"] = "3", ["SpvGateway:MinHealthyPeers"] = "9" });

        Assert.True(o.MinHealthyPeers <= o.PeerTarget);
    }
}

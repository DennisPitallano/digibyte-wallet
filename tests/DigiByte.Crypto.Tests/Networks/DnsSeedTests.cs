using DigiByte.Crypto.Networks;

namespace DigiByte.Crypto.Tests.Networks;

/// <summary>
/// The SPV gateway finds peers through the network definition's DNS seeds (specs/011-p2p-spv-gateway, T007).
/// The seeds are the ones DigiByte Core lists in its chain parameters; a missing or duplicated list leaves the gateway with no peers.
/// </summary>
public class DnsSeedTests
{
    [Fact]
    public void Mainnet_lists_the_seeds_from_DigiByte_Core()
    {
        var hosts = DigiByteNetwork.Mainnet.DNSSeeds.Select(s => s.Host).ToArray();

        Assert.Contains("seed.digibyte.io", hosts);
        Assert.Contains("seed.diginode.tools", hosts);
        Assert.Contains("seed.digibyte.link", hosts);
        Assert.Contains("seed.aroundtheblock.app", hosts);
        Assert.Contains("seed.tuyul.cc", hosts);
    }

    [Fact]
    public void Mainnet_does_not_list_the_old_seed_that_does_not_resolve()
    {
        var hosts = DigiByteNetwork.Mainnet.DNSSeeds.Select(s => s.Host).ToArray();

        Assert.DoesNotContain("seed1.digibyte.io", hosts);
    }

    [Fact]
    public void Testnet_lists_the_seeds_from_DigiByte_Core()
    {
        var hosts = DigiByteNetwork.Testnet.DNSSeeds.Select(s => s.Host).ToArray();

        Assert.Contains("testnetseed.digibyte.io", hosts);
        Assert.Contains("testnetseed.digibyte.link", hosts);
        Assert.Contains("testnetseed.digibyte.services", hosts);
    }

    [Theory]
    [InlineData("Mainnet")]
    [InlineData("Testnet")]
    public void Seed_lists_are_not_empty_and_have_no_duplicates(string network)
    {
        var net = network == "Mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet;
        var hosts = net.DNSSeeds.Select(s => s.Host).ToArray();

        Assert.NotEmpty(hosts);
        Assert.Equal(hosts.Length, hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

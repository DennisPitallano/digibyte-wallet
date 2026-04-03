using NBitcoin;

namespace DigiByte.Crypto.Networks;

public class DigiByteNetworkSet : INetworkSet
{
    private static readonly Lazy<DigiByteNetworkSet> LazyInstance = new(() => new DigiByteNetworkSet());
    public static DigiByteNetworkSet Instance => LazyInstance.Value;

    private readonly Lazy<Network> _mainnet;
    private readonly Lazy<Network> _testnet;

    public string CryptoCode => "DGB";

    public Network Mainnet => _mainnet.Value;
    public Network Testnet => _testnet.Value;
    public Network Regtest => Testnet;

    private DigiByteNetworkSet()
    {
        _mainnet = new Lazy<Network>(RegisterMainnet);
        _testnet = new Lazy<Network>(RegisterTestnet);
    }

    public Network GetNetwork(ChainName chainName)
    {
        if (chainName == ChainName.Testnet) return Testnet;
        return Mainnet;
    }

    private Network RegisterMainnet()
    {
        var genesisHex = BuildGenesisHex();

        var consensus = new Consensus
        {
            SubsidyHalvingInterval = 1050000,
            MajorityEnforceBlockUpgrade = 750,
            MajorityRejectBlockOutdated = 950,
            MajorityWindow = 1000,
            PowTargetTimespan = TimeSpan.FromSeconds(14 * 24 * 60 * 60),
            PowTargetSpacing = TimeSpan.FromSeconds(15),
            PowAllowMinDifficultyBlocks = false,
            PowNoRetargeting = false,
            SupportSegwit = true,
        };

        var builder = new NetworkBuilder()
            .SetConsensus(consensus)
            .SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, [0x1E])
            .SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, [0x3F])
            .SetBase58Bytes(Base58Type.SECRET_KEY, [0x80])
            .SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, [0x04, 0x88, 0xB2, 0x1E])
            .SetBase58Bytes(Base58Type.EXT_SECRET_KEY, [0x04, 0x88, 0xAD, 0xE4])
            .SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, "dgb")
            .SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, "dgb")
            .SetMagic(0xDAB6C3FA)
            .SetPort(12024)
            .SetRPCPort(14022)
            .SetName("dgb-mainnet")
            .SetChainName(ChainName.Mainnet)
            .SetNetworkSet(this)
            .AddDNSSeeds(new[]
            {
                new DNSSeedData("seed1.digibyte.io", "seed1.digibyte.io"),
            })
            .SetGenesis(genesisHex);

        return builder.BuildAndRegister();
    }

    private Network RegisterTestnet()
    {
        var genesisHex = BuildGenesisHex();

        var consensus = new Consensus
        {
            SubsidyHalvingInterval = 1050000,
            MajorityEnforceBlockUpgrade = 51,
            MajorityRejectBlockOutdated = 75,
            MajorityWindow = 100,
            PowTargetTimespan = TimeSpan.FromSeconds(14 * 24 * 60 * 60),
            PowTargetSpacing = TimeSpan.FromSeconds(15),
            PowAllowMinDifficultyBlocks = true,
            PowNoRetargeting = false,
            SupportSegwit = true,
        };

        var builder = new NetworkBuilder()
            .SetConsensus(consensus)
            .SetBase58Bytes(Base58Type.PUBKEY_ADDRESS, [0x7E])
            .SetBase58Bytes(Base58Type.SCRIPT_ADDRESS, [0x8C])
            .SetBase58Bytes(Base58Type.SECRET_KEY, [0xFE])
            .SetBase58Bytes(Base58Type.EXT_PUBLIC_KEY, [0x04, 0x35, 0x87, 0xCF])
            .SetBase58Bytes(Base58Type.EXT_SECRET_KEY, [0x04, 0x35, 0x83, 0x94])
            .SetBech32(Bech32Type.WITNESS_PUBKEY_ADDRESS, "dgbt")
            .SetBech32(Bech32Type.WITNESS_SCRIPT_ADDRESS, "dgbt")
            .SetMagic(0xFDC8BDDD)
            .SetPort(12025)
            .SetRPCPort(14023)
            .SetName("dgb-testnet")
            .SetChainName(ChainName.Testnet)
            .SetNetworkSet(this)
            .AddDNSSeeds(Array.Empty<DNSSeedData>())
            .SetGenesis(genesisHex);

        return builder.BuildAndRegister();
    }

    private static string BuildGenesisHex()
    {
        return
            "01000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "696ad20e2dd4365c7459b4a4a5af743d5e92c6da3229e01f0d75700127286658" +
            "acba5e53ffff001e5a320a00" +
            "01" +
            "01000000010000000000000000000000000000000000000000000000000000000000000000ffffffff" +
            "4804ffff001e01044555534120546f64617920302f4a616e2f323031342c20546172676574205374" +
            "6f72657320446174612042726561636820416666656374732034304d20436172647300ffffffff01" +
            "00f2052a0100000043410459190b7e2054b7a1f1de832dbcc13b4bcc0b153a5e30d4048acb0b31e3" +
            "c12d10a5fb93cf71a7bfba2048ad5e7e15ccfa92a4b3e75e7b223395b6a4b07e21ac00000000";
    }
}

/// <summary>
/// Convenience accessors for DigiByte networks.
/// </summary>
public static class DigiByteNetwork
{
    public static Network Mainnet => DigiByteNetworkSet.Instance.Mainnet;
    public static Network Testnet => DigiByteNetworkSet.Instance.Testnet;
}

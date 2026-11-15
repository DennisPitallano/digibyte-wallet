using System.Text.Json;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.SpvGateway.Tests.Support;

/// <summary>Real mainnet blocks (a few dozen just after the built-in checkpoint), as served by peers.</summary>
internal static class RealBlocks
{
    private static readonly Lazy<IReadOnlyList<(int Height, uint256 Hash, byte[] Raw)>> Lazy = new(Load);

    public static IReadOnlyList<(int Height, uint256 Hash, byte[] Raw)> All => Lazy.Value;

    public static Block Parse(byte[] raw)
    {
        var network = DigiByteNetworkSet.Instance.Mainnet;
        return Block.Load(raw, network);
    }

    private static IReadOnlyList<(int, uint256, byte[])> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "blocks-mainnet.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("blocks").EnumerateArray()
            .Select(e => (e.GetProperty("height").GetInt32(), uint256.Parse(e.GetProperty("hash").GetString()!), Convert.FromHexString(e.GetProperty("hex").GetString()!)))
            .ToList();
    }
}

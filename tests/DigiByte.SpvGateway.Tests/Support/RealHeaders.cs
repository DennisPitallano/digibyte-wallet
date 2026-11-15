using System.Text.Json;
using DigiByte.Crypto.Headers;

namespace DigiByte.SpvGateway.Tests.Support;

/// <summary>Real mainnet headers (heights 24,563,017 and up) and helpers to build forks from them.</summary>
internal static class RealHeaders
{
    private static readonly Lazy<IReadOnlyList<(int Height, byte[] Raw)>> Lazy = new(Load);

    public static IReadOnlyList<(int Height, byte[] Raw)> Run => Lazy.Value;

    public static BlockHeader80 At(int height) => BlockHeader80.Parse(Run.First(r => r.Height == height).Raw);

    private static IReadOnlyList<(int, byte[])> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .Select(e => (e.GetProperty("height").GetInt32(), Convert.FromHexString(e.GetProperty("header").GetString()!)))
            .ToList();
    }

    /// <summary>
    /// A fork that leaves the real chain after <paramref name="forkAfterHeight"/>: the next <paramref name="length"/> real headers with
    /// their nonce changed and their previous hashes relinked. Time, algorithm and difficulty match the real branch, so every header is
    /// valid except for its proof of work, which only a sync without a hasher ignores.
    /// </summary>
    public static List<BlockHeader80> Fork(int forkAfterHeight, int length)
    {
        var result = new List<BlockHeader80>();
        var previous = At(forkAfterHeight).Hash;
        for (var i = 1; i <= length; i++)
        {
            var raw = (byte[])Run.First(r => r.Height == forkAfterHeight + i).Raw.Clone();
            previous.ToBytes().CopyTo(raw, 4);
            raw[76] ^= 0xA5;
            var header = BlockHeader80.Parse(raw);
            result.Add(header);
            previous = header.Hash;
        }
        return result;
    }
}

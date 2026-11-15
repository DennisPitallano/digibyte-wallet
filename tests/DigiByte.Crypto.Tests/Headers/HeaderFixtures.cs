using System.Text.Json;
using DigiByte.Crypto.Headers;

namespace DigiByte.Crypto.Tests.Headers;

/// <summary>
/// Real mainnet data for the header tests (specs/011-p2p-spv-gateway, task T008): 400 consecutive headers
/// (heights 24,563,017 to 24,563,416) and merkle vectors, from the digiexplorer.info Esplora API.
/// Each header was rebuilt from the block fields and checked against the block id when the fixture was made.
/// </summary>
internal static class HeaderFixtures
{
    public sealed record Row(int Height, byte[] Raw, string Id, string Algo, long MedianTime);

    public sealed record MerkleVector(int Height, string BlockId, string MerkleRoot, string TxId, int TxIndex, string[] Branch, string RawTx, int TxCount);

    private static readonly Lazy<IReadOnlyList<Row>> LazyRun = new(LoadRun);
    private static readonly Lazy<IReadOnlyList<MerkleVector>> LazyMerkle = new(LoadMerkle);

    public static IReadOnlyList<Row> Run => LazyRun.Value;
    public static IReadOnlyList<MerkleVector> Merkle => LazyMerkle.Value;

    public static Row At(int height) => Run.First(r => r.Height == height);

    public static BlockHeader80 Header(int height) => BlockHeader80.Parse(At(height).Raw);

    private static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Headers", "Fixtures", name);

    private static IReadOnlyList<Row> LoadRun()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path("headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .Select(e => new Row(
                e.GetProperty("height").GetInt32(),
                Convert.FromHexString(e.GetProperty("header").GetString()!),
                e.GetProperty("id").GetString()!,
                e.GetProperty("algo").GetString()!,
                e.GetProperty("mediantime").GetInt64()))
            .ToList();
    }

    private static IReadOnlyList<MerkleVector> LoadMerkle()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path("merkle-mainnet.json")));
        return doc.RootElement.GetProperty("vectors").EnumerateArray()
            .Select(e => new MerkleVector(
                e.GetProperty("height").GetInt32(),
                e.GetProperty("blockId").GetString()!,
                e.GetProperty("merkleRoot").GetString()!,
                e.GetProperty("txid").GetString()!,
                e.GetProperty("txIndex").GetInt32(),
                e.GetProperty("branch").EnumerateArray().Select(b => b.GetString()!).ToArray(),
                e.GetProperty("rawTx").GetString()!,
                e.GetProperty("txCount").GetInt32()))
            .ToList();
    }
}

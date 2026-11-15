using System.Text.Json;
using DigiByte.Crypto.Headers.Hashers;

namespace DigiByte.Crypto.Tests.Headers;

/// <summary>
/// Qubit chains five hash functions: Luffa-512, CubeHash-512, SHAvite-3-512, SIMD-512 and ECHO-512 (specs/011-p2p-spv-gateway, T024).
/// Each port is checked on its own against values produced by DigiByte Core's own C++ code, built and run for this purpose
/// (see <c>qubit-reference.json</c>), on inputs of many lengths so every padding case is hit.
/// </summary>
public class QubitComponentTests
{
    public sealed record Vector(string Input, string Luffa, string CubeHash, string Shavite, string Simd, string Echo, string[] Chain);

    private static readonly Lazy<IReadOnlyList<Vector>> Lazy = new(Load);

    public static IReadOnlyList<Vector> Vectors => Lazy.Value;

    private static IReadOnlyList<Vector> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Headers", "Fixtures", "qubit-reference.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("vectors").EnumerateArray().Select(e => new Vector(
            e.GetProperty("input").GetString()!,
            e.GetProperty("luffa").GetString()!,
            e.GetProperty("cubehash").GetString()!,
            e.GetProperty("shavite").GetString()!,
            e.GetProperty("simd").GetString()!,
            e.GetProperty("echo").GetString()!,
            Enumerable.Range(0, 5).Select(i => e.GetProperty($"chain{i}").GetString()!).ToArray())).ToList();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void The_reference_vectors_cover_many_input_lengths_including_real_headers()
    {
        Assert.True(Vectors.Count >= 20);
        var lengths = Vectors.Select(v => v.Input.Length / 2).ToHashSet();
        Assert.Contains(0, lengths);
        Assert.Contains(64, lengths);
        Assert.Contains(80, lengths);
    }

    [Fact]
    public void Luffa512_matches_the_reference()
    {
        foreach (var v in Vectors)
            Assert.Equal(v.Luffa, Hex(Luffa512.Hash(Convert.FromHexString(v.Input))));
    }

    [Fact]
    public void CubeHash512_matches_the_reference()
    {
        foreach (var v in Vectors)
            Assert.Equal(v.CubeHash, Hex(CubeHash512.Hash(Convert.FromHexString(v.Input))));
    }

    [Fact]
    public void Shavite512_matches_the_reference()
    {
        foreach (var v in Vectors)
            Assert.Equal(v.Shavite, Hex(Shavite512.Hash(Convert.FromHexString(v.Input))));
    }

    [Fact]
    public void Echo512_matches_the_reference()
    {
        foreach (var v in Vectors)
            Assert.Equal(v.Echo, Hex(Echo512.Hash(Convert.FromHexString(v.Input))));
    }

    [Fact]
    public void Simd512_matches_the_reference()
    {
        foreach (var v in Vectors)
            Assert.Equal(v.Simd, Hex(Simd512.Hash(Convert.FromHexString(v.Input))));
    }

    [Fact]
    public void The_whole_qubit_chain_matches_the_reference_at_every_stage()
    {
        foreach (var v in Vectors)
        {
            var chain = QubitHasher.Chain(Convert.FromHexString(v.Input));
            for (var i = 0; i < 5; i++)
                Assert.Equal(v.Chain[i], Hex(chain[i]));
        }
    }
}

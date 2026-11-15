using DigiByte.Crypto.Headers;

namespace DigiByte.Crypto.Tests.Headers;

public class BlockHeader80Tests
{
    [Fact]
    public void Every_fixture_header_parses_and_hashes_to_its_block_id()
    {
        foreach (var row in HeaderFixtures.Run)
        {
            var header = BlockHeader80.Parse(row.Raw);

            Assert.Equal(row.Id, header.Hash.ToString());
            Assert.Equal(row.Raw, header.ToBytes());
        }
    }

    [Fact]
    public void Algorithm_is_read_from_the_version_bits_for_every_active_algorithm()
    {
        var expected = new Dictionary<string, PowAlgorithm>
        {
            ["sha256d"] = PowAlgorithm.Sha256d,
            ["scrypt"] = PowAlgorithm.Scrypt,
            ["skein"] = PowAlgorithm.Skein,
            ["qubit"] = PowAlgorithm.Qubit,
            ["odo"] = PowAlgorithm.Odo,
        };

        foreach (var row in HeaderFixtures.Run)
            Assert.Equal(expected[row.Algo], BlockHeader80.Parse(row.Raw).Algorithm);

        Assert.Equal(5, HeaderFixtures.Run.Select(r => r.Algo).Distinct().Count());
    }

    [Theory]
    [InlineData(0 << 8, PowAlgorithm.Scrypt)]
    [InlineData(2 << 8, PowAlgorithm.Sha256d)]
    [InlineData(4 << 8, PowAlgorithm.Groestl)]
    [InlineData(6 << 8, PowAlgorithm.Skein)]
    [InlineData(8 << 8, PowAlgorithm.Qubit)]
    [InlineData(14 << 8, PowAlgorithm.Odo)]
    public void Version_bit_constants_map_to_the_documented_algorithm(int algoBits, PowAlgorithm expected)
    {
        var version = 0x20000000 | algoBits | 2;

        Assert.True(PowAlgorithms.TryFromVersion(version, out var algo));
        Assert.Equal(expected, algo);
    }

    [Theory]
    [InlineData(1 << 8)]
    [InlineData(3 << 8)]
    [InlineData(5 << 8)]
    [InlineData(7 << 8)]
    [InlineData(9 << 8)]
    [InlineData(15 << 8)]
    public void An_unknown_algorithm_value_is_rejected(int algoBits)
    {
        Assert.False(PowAlgorithms.TryFromVersion(0x20000000 | algoBits, out var algo));
        Assert.Equal(PowAlgorithm.Unknown, algo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(79)]
    [InlineData(81)]
    [InlineData(160)]
    public void Input_that_is_not_exactly_80_bytes_is_rejected(int length)
    {
        Assert.Throws<FormatException>(() => BlockHeader80.Parse(new byte[length]));
    }

    [Fact]
    public void Changing_any_byte_changes_the_hash()
    {
        var raw = (byte[])HeaderFixtures.Run[0].Raw.Clone();
        var original = BlockHeader80.Parse(raw).Hash;
        raw[79] ^= 0x01;

        Assert.NotEqual(original, BlockHeader80.Parse(raw).Hash);
    }
}

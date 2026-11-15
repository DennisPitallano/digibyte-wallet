using DigiByte.Crypto.Headers;
using DigiByte.Crypto.Headers.Hashers;

namespace DigiByte.Crypto.Tests.Headers;

/// <summary>
/// Each algorithm's hash of a real header must be at or below the target its nBits claims. The explorer exposes no PoW
/// hash, so this is the oracle: a wrong hash function fails on essentially every real block
/// (specs/011-p2p-spv-gateway, tasks T015, T022 to T025). Skein, Qubit and Odocrypt are added when their ports exist.
/// </summary>
public class PowHasherTests
{
    private static IEnumerable<HeaderFixtures.Row> Rows(string algo) => HeaderFixtures.Run.Where(r => r.Algo == algo);

    public static IEnumerable<object[]> ImplementedAlgorithms() =>
    [
        ["sha256d", new Sha256dHasher()],
        ["scrypt", new ScryptHasher()],
        ["skein", new SkeinHasher()],
        ["qubit", new QubitHasher()],
        ["odo", new OdocryptHasher()],
    ];

    [Theory]
    [MemberData(nameof(ImplementedAlgorithms))]
    public void Every_real_block_of_the_algorithm_meets_its_own_target(string algo, IPowHasher hasher)
    {
        var rows = Rows(algo).ToList();
        Assert.True(rows.Count >= 50, "the fixture should hold plenty of blocks for each algorithm");

        foreach (var row in rows)
        {
            var header = BlockHeader80.Parse(row.Raw);
            Assert.True(hasher.Supports(header.Algorithm));
            var hash = hasher.Hash(header);

            Assert.True(CompactTarget.MeetsTarget(hash, header.Bits, DigiByteConsensus.Mainnet.PowLimit),
                $"{algo} block {row.Height}");
        }
    }

    [Theory]
    [MemberData(nameof(ImplementedAlgorithms))]
    public void A_header_with_a_changed_nonce_fails_its_target(string algo, IPowHasher hasher)
    {
        var failures = 0;
        foreach (var row in Rows(algo).Take(30))
        {
            var raw = (byte[])row.Raw.Clone();
            raw[76] ^= 0xFF; // nonce
            var header = BlockHeader80.Parse(raw);
            if (!CompactTarget.MeetsTarget(hasher.Hash(header), header.Bits, DigiByteConsensus.Mainnet.PowLimit))
                failures++;
        }

        Assert.True(failures >= 29, $"changing the nonce should break the work in nearly every case, failed {failures} of 30");
    }

    [Fact]
    public void Sha256d_proof_of_work_hash_is_the_block_hash()
    {
        var header = BlockHeader80.Parse(Rows("sha256d").First().Raw);

        Assert.Equal(header.Hash, new Sha256dHasher().Hash(header));
    }

    [Fact]
    public void Skein_512_matches_the_published_known_answer_for_the_one_byte_message_0xFF()
    {
        // From the Skein reference known-answer tests (Skein-512-512, message FF).
        var digest = SkeinHasher.Skein512Digest([0xFF]);

        Assert.Equal("71B7BCE6FE6452227B9CED6014249E5BF9A9754C3AD618CCC4E0AAE16B316CC8CA698D864307ED3E80B6EF1570812AC5272DC409B5A012DF2A579102F340617A",
            Convert.ToHexString(digest));
    }

    [Fact]
    public void Odocrypt_uses_a_different_shape_for_a_different_ten_day_key()
    {
        var row = Rows("odo").First();
        var header = BlockHeader80.Parse(row.Raw);
        var mainnet = new OdocryptHasher().Hash(header);
        var oneDayKey = new OdocryptHasher(24 * 60 * 60).Hash(header);

        Assert.NotEqual(mainnet, oneDayKey);
    }

    [Fact]
    public void A_hasher_refuses_an_algorithm_it_does_not_implement()
    {
        var odo = BlockHeader80.Parse(Rows("odo").First().Raw);

        Assert.False(new Sha256dHasher().Supports(odo.Algorithm));
        Assert.Throws<NotSupportedException>(() => new Sha256dHasher().Hash(odo));
    }

    [Fact]
    public void The_combined_hasher_reports_which_algorithms_it_can_check()
    {
        var all = PowHashers.Available;
        var active = new[] { PowAlgorithm.Sha256d, PowAlgorithm.Scrypt, PowAlgorithm.Skein, PowAlgorithm.Qubit, PowAlgorithm.Odo };

        Assert.True(all.Supports(PowAlgorithm.Sha256d));
        Assert.True(all.Supports(PowAlgorithm.Scrypt));
        // Every active algorithm now has a hasher, so proof of work can be checked for all of them.
        Assert.True(active.All(all.Supports));
        Assert.True(PowHashers.AllActiveAlgorithmsSupported);
        Assert.False(all.Supports(PowAlgorithm.Groestl));
        Assert.False(all.Supports(PowAlgorithm.Unknown));
    }
}

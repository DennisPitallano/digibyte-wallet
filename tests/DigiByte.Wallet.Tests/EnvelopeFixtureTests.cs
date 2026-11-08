using System.Text.Json;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Tests;

/// <summary>Reads the same known-answer file as the JS suite (T004); the C# side checks structure only.</summary>
public class EnvelopeFixtureTests
{
    private static JsonElement Fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "envelope-fixtures.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void The_legacy_fixture_is_recognised_as_legacy()
        => Assert.Equal(EnvelopeStatus.Legacy, EnvelopeCodec.Parse(Fixtures().GetProperty("legacy").GetString()).Status);

    [Theory]
    [InlineData("standard", 65536, 3)]
    [InlineData("low-memory", 32768, 3)]
    [InlineData("floor", 19456, 2)]
    public void Each_profile_fixture_parses_with_its_cost_parameters(string profile, int memoryKiB, int passes)
    {
        var parsed = EnvelopeCodec.Parse(Fixtures().GetProperty("valid").GetProperty(profile).GetString());

        Assert.Equal(EnvelopeStatus.Ok, parsed.Status);
        Assert.Equal(new KdfParameters(memoryKiB, passes, 1), parsed.Envelope!.Kdf);
    }

    [Theory]
    [InlineData("truncated", EnvelopeStatus.Unreadable)]
    [InlineData("unknownVersion", EnvelopeStatus.UnsupportedVersion)]
    [InlineData("belowFloorMemory", EnvelopeStatus.Unreadable)]
    [InlineData("belowFloorPasses", EnvelopeStatus.Unreadable)]
    [InlineData("headerBitFlip", EnvelopeStatus.Ok)] // structurally valid; refused at authentication
    [InlineData("ciphertextBitFlip", EnvelopeStatus.Ok)]
    public void Tampered_fixtures_parse_to_the_expected_status(string name, EnvelopeStatus expected)
        => Assert.Equal(expected, EnvelopeCodec.Parse(Fixtures().GetProperty("tampered").GetProperty(name).GetString()).Status);
}

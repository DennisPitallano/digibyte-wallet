using System.Text.Json;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

public class WalletInfoProtectionFieldsTests
{
    [Fact]
    public void Wallet_json_written_before_this_feature_still_loads_with_safe_defaults()
    {
        const string old = """{"Id":"w1","Name":"Old","CreatedAt":"2026-01-01T00:00:00Z","WalletType":"hd"}""";

        var info = JsonSerializer.Deserialize<WalletInfo>(old)!;

        Assert.Equal("pin", info.CredentialType);
        Assert.Equal("standard", info.ProtectionLevel);
        Assert.False(info.PassphraseNudgeDismissed);
    }

    [Fact]
    public void New_fields_round_trip()
    {
        var info = new WalletInfo
        {
            Id = "w2",
            Name = "New",
            CreatedAt = DateTime.UtcNow,
            CredentialType = "passphrase",
            ProtectionLevel = "legacy",
            PassphraseNudgeDismissed = true,
        };

        var back = JsonSerializer.Deserialize<WalletInfo>(JsonSerializer.Serialize(info))!;

        Assert.Equal("passphrase", back.CredentialType);
        Assert.Equal("legacy", back.ProtectionLevel);
        Assert.True(back.PassphraseNudgeDismissed);
    }

    [Fact]
    public void Older_builds_ignore_the_new_fields()
    {
        // Simulates an older build that has no such properties: unknown JSON members must not break loading.
        const string withExtra = """{"Id":"w3","Name":"X","CreatedAt":"2026-01-01T00:00:00Z","CredentialType":"passphrase","Future":1}""";

        var info = JsonSerializer.Deserialize<WalletInfo>(withExtra)!;

        Assert.Equal("passphrase", info.CredentialType);
    }
}

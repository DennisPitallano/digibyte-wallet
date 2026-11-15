using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Staged delivery: no promotion above the explorers and no "verified" label before the wallet checks proof of work itself (FR-013, T042).</summary>
public class GatewayCapabilitiesTests
{
    [Fact]
    public void The_wallet_does_not_check_proof_of_work_yet()
    {
        Assert.False(GatewayCapabilities.WalletPowVerified);
    }

    [Theory]
    [InlineData(GatewayPosition.Backup, false, GatewayPosition.Backup)]
    [InlineData(GatewayPosition.First, false, GatewayPosition.Backup)]
    [InlineData(GatewayPosition.Backup, true, GatewayPosition.Backup)]
    [InlineData(GatewayPosition.First, true, GatewayPosition.First)]
    public void A_request_for_first_only_takes_effect_once_proof_of_work_is_verified(GatewayPosition requested, bool verified, GatewayPosition expected)
    {
        Assert.Equal(expected, GatewayCapabilities.EffectivePosition(requested, verified));
    }
}

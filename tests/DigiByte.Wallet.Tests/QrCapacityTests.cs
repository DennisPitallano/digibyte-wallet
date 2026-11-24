using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Whether a proposal is small enough to show as one QR code (specs/029-multisig-multiple-recipients, FR-015).</summary>
public class QrCapacityTests
{
    [Fact]
    public void A_proposal_up_to_the_limit_fits()
    {
        Assert.Equal(2200, QrCapacity.MaxProposalChars);
        Assert.True(QrCapacity.FitsProposal(new string('A', 2200)));
        Assert.True(QrCapacity.FitsProposal("cHNidP8B"));
    }

    [Fact]
    public void A_proposal_over_the_limit_does_not_fit()
    {
        Assert.False(QrCapacity.FitsProposal(new string('A', 2201)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_to_show_does_not_fit(string? psbt)
    {
        Assert.False(QrCapacity.FitsProposal(psbt));
    }
}

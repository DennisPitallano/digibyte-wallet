using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class DonationInfoTests
{
    [Fact]
    public void Bip21Uri_HasTheAddressAndTheMinimumAmount()
    {
        Assert.Equal($"digibyte:{DonationInfo.CurrentAddress}?amount=1", DonationInfo.Bip21Uri());
        Assert.Equal($"digibyte:{DonationInfo.CurrentAddress}?amount=5", DonationInfo.Bip21Uri(5));
    }

    [Fact]
    public void CurrentAddress_IsAMainnetSegwitAddress()
    {
        Assert.StartsWith("dgb1q", DonationInfo.CurrentAddress);
    }

    [Fact]
    public void CurrentAddress_IsOneOfTheApisAcceptedDonationAddresses()
    {
        // The API's donation check reads Sponsors:DonationAddresses; the QR must point at an address it accepts.
        var path = FindUp("src/DigiByte.Api/appsettings.json");
        var text = File.ReadAllText(path);
        Assert.Contains($"\"{DonationInfo.CurrentAddress}\"", text);
    }

    private static string FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}

using Microsoft.Extensions.DependencyInjection;
using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// The checkout and tip page offer an installed wallet app and the DigiByte Wallet in the browser.
public class WalletButtonsTests : BunitContext
{
    private const string Bip21 = "digibyte:dgb1qexample?amount=5&label=Shop";

    [Fact]
    public void Both_ways_to_pay_point_at_the_same_request()
    {
        var cut = Render<WalletButtons>(p => p.Add(x => x.Uri, Bip21));
        Assert.NotNull(cut.Find($"a.btn-primary[href='{Bip21}']"));
        var web = cut.Find("a.ui-wallet-web");
        // The request goes after the # of the wallet's pay page, so the wallet's server never sees it.
        Assert.Equal("https://dgbwallet.app/pay#" + Bip21, web.GetAttribute("href"));
        Assert.Equal("_blank", web.GetAttribute("target"));
        Assert.Contains("noopener", web.GetAttribute("rel"));
    }

    [Fact]
    public void The_wallet_address_comes_from_configuration()
    {
        Services.AddSingleton(new WalletAppUrl("https://wallet.test/"));
        var cut = Render<WalletButtons>(p => p.Add(x => x.Uri, Bip21));
        Assert.Equal("https://wallet.test/pay#" + Bip21, cut.Find("a.ui-wallet-web").GetAttribute("href"));
    }
}

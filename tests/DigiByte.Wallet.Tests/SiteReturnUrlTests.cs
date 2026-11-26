using DigiByte.Crypto.DigiId;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The "return to the site" address a site passes with a payment link (/pay?return=…#digibyte:…) or a Digi-ID
/// sign-in (/identity?uri=…&amp;return=…). Anything that fails a rule is ignored: no button, no error.
/// </summary>
public class SiteReturnUrlTests
{
    // ─── Payments: absolute https, http only on localhost ───

    [Theory]
    [InlineData("https://pay.dgbwallet.app/checkout/abc?theme=dark", "pay.dgbwallet.app")]
    [InlineData("https://pay.dgbwallet.app/", "pay.dgbwallet.app")]
    [InlineData("HTTPS://Pay.DGBWallet.App/x", "pay.dgbwallet.app")]
    [InlineData("https://shop.example:8443/x", "shop.example:8443")]
    [InlineData("https://pay.dgbwallet.app:443/x", "pay.dgbwallet.app")]
    [InlineData("http://localhost:5252/checkout/abc", "localhost:5252")]
    [InlineData("http://127.0.0.1:5252/x", "127.0.0.1:5252")]
    [InlineData("http://[::1]:5252/x", "[::1]:5252")]
    [InlineData("https://localhost/x", "localhost")]
    public void A_payment_accepts_https_and_local_http_and_shows_the_host(string value, string host)
    {
        var result = SiteReturnUrl.ForPayment(value);
        Assert.NotNull(result);
        Assert.Equal(host, result!.Host);
        Assert.StartsWith(value.StartsWith("http:") ? "http://" : "https://", result.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://pay.dgbwallet.app/x")]          // plain http off localhost
    [InlineData("http://localhost.evil.com/x")]
    [InlineData("http://192.168.1.5/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("ftp://pay.dgbwallet.app/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/checkout/abc")]                        // relative
    [InlineData("//evil.com/x")]                         // scheme-relative
    [InlineData("pay.dgbwallet.app/checkout")]
    [InlineData("https:pay.dgbwallet.app")]
    [InlineData("https:/pay.dgbwallet.app")]
    [InlineData("https://dgbwallet.app@evil.com/")]      // userinfo
    [InlineData("https://user:pass@pay.dgbwallet.app/")]
    [InlineData("https://evil.com\\@pay.dgbwallet.app/")] // backslash
    [InlineData("https://pay.dgbwallet.app/a b")]
    [InlineData("https://pay.dgbwallet.app/\nx")]
    [InlineData(" https://pay.dgbwallet.app/")]
    [InlineData("https://pay.dgbwallet.app./x")]         // trailing dot
    [InlineData("https://")]
    public void A_payment_ignores_anything_else(string? value)
    {
        Assert.Null(SiteReturnUrl.ForPayment(value));
    }

    [Fact]
    public void An_overlong_address_is_ignored()
    {
        Assert.Null(SiteReturnUrl.ForPayment("https://pay.dgbwallet.app/" + new string('a', 2100)));
    }

    [Fact]
    public void A_look_alike_host_shows_in_its_ascii_form()
    {
        // Cyrillic "а" in place of the Latin one: the button shows the xn-- form, not something that reads as dgbwallet.
        var result = SiteReturnUrl.ForPayment("https://pay.dgbwаllet.app/x");
        Assert.NotNull(result);
        Assert.StartsWith("pay.xn--", result!.Host);
    }

    // ─── Digi-ID: the same site as the request ───

    private static string Callback(string digiId) => DigiIdService.ParseUri(digiId)!.CallbackUrl;

    private const string Production = "digiid://pay.dgbwallet.app/v1/pay/auth/callback?x=n1";
    private const string ProductionApi = "digiid://api.pay.dgbwallet.app/v1/pay/auth/callback?x=n1";
    private const string Local = "digiid://localhost:5008/v1/pay/auth/callback?x=n1&u=1";

    [Theory]
    [InlineData(Production, "https://pay.dgbwallet.app/merchant/signin", "pay.dgbwallet.app")]
    [InlineData(ProductionApi, "https://pay.dgbwallet.app/merchant/signin", "pay.dgbwallet.app")]
    [InlineData(Production, "https://api.pay.dgbwallet.app/x", "api.pay.dgbwallet.app")]
    [InlineData(Production, "https://dgbwallet.app/", "dgbwallet.app")]
    [InlineData("digiid://dgbwallet.app/cb?x=n1", "https://pay.dgbwallet.app/", "pay.dgbwallet.app")]
    [InlineData(Production, "https://PAY.dgbwallet.app/x", "pay.dgbwallet.app")]
    [InlineData("digiid://pay.dgbwallet.app:8443/cb?x=n1", "https://pay.dgbwallet.app:8443/x", "pay.dgbwallet.app:8443")]
    [InlineData("digiid://pay.dgbwallet.app:8443/cb?x=n1", "https://pay.dgbwallet.app/x", "pay.dgbwallet.app")] // default port
    [InlineData(Local, "http://localhost:5252/merchant/signin", "localhost:5252")]  // local DigiPay: web :5252, API :5008
    [InlineData(Local, "http://localhost:5008/x", "localhost:5008")]
    [InlineData(Local, "https://localhost:7253/x", "localhost:7253")]
    [InlineData("digiid://127.0.0.1:5008/cb?x=n1&u=1", "http://127.0.0.1:5252/x", "127.0.0.1:5252")]
    [InlineData("digiid://10.0.0.5/cb?x=n1", "https://10.0.0.5/x", "10.0.0.5")]
    public void Digi_id_accepts_the_same_site(string digiId, string value, string host)
    {
        var result = SiteReturnUrl.ForDigiId(value, Callback(digiId));
        Assert.NotNull(result);
        Assert.Equal(host, result!.Host);
    }

    [Theory]
    [InlineData(Production, "https://dgbwallet.app.evil.com/")]
    [InlineData(Production, "https://evil-dgbwallet.app/")]
    [InlineData(Production, "https://dgbwallet.app@evil.com/")]
    [InlineData(Production, "https://pay.dgbwallet.app@evil.com/")]
    [InlineData(Production, "https://user@pay.dgbwallet.app/")]
    [InlineData(Production, "https://evil.com/")]
    [InlineData(Production, "https://dgbwallet.application/")]
    [InlineData(Production, "https://pay.dgbwallet.app.")]
    [InlineData(Production, "https://pay.dgbwallet.app:8443/x")]     // another port
    [InlineData(Production, "http://pay.dgbwallet.app/x")]           // http off localhost
    [InlineData(Production, "http://localhost:5252/x")]              // localhost is not the request's site
    [InlineData(Local, "https://pay.dgbwallet.app/x")]
    [InlineData(Local, "http://127.0.0.1:5252/x")]                   // loopback names must match exactly
    [InlineData(Local, "http://[::1]:5252/x")]
    [InlineData(Local, "http://localhost.dgbwallet.app/x")]
    [InlineData("digiid://10.0.0.5/cb?x=n1", "https://10.0.0.6/x")]   // IP addresses must match exactly
    [InlineData("digiid://1.0.0.5/cb?x=n1", "https://2.0.0.5/x")]     // never "last two labels" for an IP
    [InlineData("digiid://pay.dgbwallet.app:8443/cb?x=n1", "https://pay.dgbwallet.app:9443/x")]
    [InlineData(Production, "javascript:alert(1)")]
    [InlineData(Production, "/merchant/signin")]
    [InlineData(Production, null)]
    public void Digi_id_ignores_another_site(string digiId, string? value)
    {
        Assert.Null(SiteReturnUrl.ForDigiId(value, Callback(digiId)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://pay.dgbwallet.app/")]
    public void Digi_id_without_a_usable_callback_gives_nothing(string? callback)
    {
        Assert.Null(SiteReturnUrl.ForDigiId("https://pay.dgbwallet.app/", callback));
    }

    [Fact]
    public void The_address_followed_is_the_checked_one()
    {
        var result = SiteReturnUrl.ForDigiId("https://pay.dgbwallet.app/merchant/signin?return=%2Fdashboard", Callback(Production));
        Assert.Equal("https://pay.dgbwallet.app/merchant/signin?return=%2Fdashboard", result!.Url);
    }
}

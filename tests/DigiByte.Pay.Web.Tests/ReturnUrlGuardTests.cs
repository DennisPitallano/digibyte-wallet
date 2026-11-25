using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// contracts/dashboard-routes.md: sign-in return guard.
public class ReturnUrlGuardTests
{
    [Theory]
    [InlineData("/merchant")]
    [InlineData("/merchant/webhook")]
    [InlineData("/merchant/sessions?status=paid")]
    [InlineData("/merchant/sessions/ses_abc")]
    [InlineData("/merchant/api-keys?store=sto_1")]
    public void Local_dashboard_paths_are_kept(string value) =>
        Assert.Equal(value, ReturnUrlGuard.Sanitize(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:5252/merchant")]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/merchant")]
    [InlineData("/\\evil.example")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/merchant/..%5C..%5Cevil")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/docs")]
    [InlineData("/merchantx")]
    [InlineData("/merchant/signin")]
    [InlineData("/merchant/signin?return=/merchant")]
    [InlineData("merchant/webhook")]
    public void Anything_else_goes_to_the_dashboard_home(string? value) =>
        Assert.Equal("/merchant", ReturnUrlGuard.Sanitize(value));
}

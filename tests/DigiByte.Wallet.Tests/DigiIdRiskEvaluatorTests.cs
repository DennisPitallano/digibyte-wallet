using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

public class DigiIdRiskEvaluatorTests
{
    private static readonly string[] Known = ["example.com", "shop.digibyte.io"];

    [Fact]
    public void KnownSecureSite_HasNoRisk() =>
        Assert.Equal(DigiIdRisk.None, DigiIdRiskEvaluator.Evaluate("example.com", isUnsecure: false, Known));

    [Fact]
    public void UnknownSecureSite_IsNewSite() =>
        Assert.Equal(DigiIdRisk.NewSite, DigiIdRiskEvaluator.Evaluate("other.org", isUnsecure: false, Known));

    [Fact]
    public void InsecureSite_IsFlagged_EvenWhenKnown() =>
        Assert.Equal(DigiIdRisk.InsecureSite, DigiIdRiskEvaluator.Evaluate("example.com", isUnsecure: true, Known));

    [Fact]
    public void InsecureBeatsNew_WhenBothApply() =>
        Assert.Equal(DigiIdRisk.InsecureSite, DigiIdRiskEvaluator.Evaluate("other.org", isUnsecure: true, Known));

    [Theory]
    [InlineData("EXAMPLE.com")]
    [InlineData("  example.com  ")]
    public void DomainMatch_IgnoresCaseAndWhitespace(string domain) =>
        Assert.Equal(DigiIdRisk.None, DigiIdRiskEvaluator.Evaluate(domain, false, Known));

    [Fact]
    public void LookalikeDomain_IsNotTreatedAsKnown() =>
        Assert.Equal(DigiIdRisk.NewSite, DigiIdRiskEvaluator.Evaluate("examp1e.com", false, Known));

    [Fact]
    public void Subdomain_IsNotTreatedAsKnown() =>
        Assert.Equal(DigiIdRisk.NewSite, DigiIdRiskEvaluator.Evaluate("login.example.com", false, Known));

    [Fact]
    public void NoHistory_MeansNewSite() =>
        Assert.Equal(DigiIdRisk.NewSite, DigiIdRiskEvaluator.Evaluate("example.com", false, []));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDomain_IsTreatedAsNewSite(string domain) =>
        Assert.Equal(DigiIdRisk.NewSite, DigiIdRiskEvaluator.Evaluate(domain, false, Known));
}

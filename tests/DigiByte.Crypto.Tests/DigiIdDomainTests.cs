using DigiByte.Crypto.DigiId;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// DigiIdDomain.Normalize turns what a user types (a bare host or a pasted URL) into the exact domain string a Digi-ID login
/// signs with. If the two ever differ, the recovery check derives a different address and silently finds nothing.
/// </summary>
public class DigiIdDomainTests
{
    private static string? LoginDomain(string hostAsTyped) =>
        DigiIdService.ParseUri($"digiid://{hostAsTyped}/callback?x=abc123")?.Domain;

    // ─── Accepted forms ───

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Example.COM", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    [InlineData("example.com:8443", "example.com")]
    [InlineData("example.com/some/path?x=1#frag", "example.com")]
    [InlineData("https://Example.com:8443/path?x=1", "example.com")]
    [InlineData("HTTPS://Example.com", "example.com")]
    [InlineData("http://example.com/", "example.com")]
    [InlineData("digiid://example.com/cb?x=abc", "example.com")]
    [InlineData("web+digiid://example.com/cb?x=abc", "example.com")]
    [InlineData("sub.example.co.uk", "sub.example.co.uk")]
    [InlineData("localhost", "localhost")]
    public void Normalize_AcceptedForms_GiveTheLoginDomain(string input, string expected)
    {
        Assert.Equal(expected, DigiIdDomain.Normalize(input));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("Example.COM")]
    [InlineData("example.com:8443")]
    [InlineData("sub.example.co.uk")]
    [InlineData("localhost")]
    public void Normalize_EqualsWhatALoginToTheSameHostSigns(string hostAsTyped)
    {
        var loginDomain = LoginDomain(hostAsTyped);

        Assert.NotNull(loginDomain);
        Assert.Equal(loginDomain, DigiIdDomain.Normalize(hostAsTyped));
        Assert.Equal(loginDomain, DigiIdDomain.Normalize($"https://{hostAsTyped}/anything?x=1"));
    }

    [Fact]
    public void Normalize_DifferentSpellingsOfOneSite_GiveOneSiteIndex()
    {
        var a = DigiIdService.DeriveSiteIndex(DigiIdDomain.Normalize("Example.com")!);
        var b = DigiIdService.DeriveSiteIndex(DigiIdDomain.Normalize("https://EXAMPLE.com:443/x")!);

        Assert.Equal(a, b);
    }

    // ─── Rejected input ───

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a domain")]
    [InlineData("exa mple.com")]
    [InlineData("example.com extra")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("https://")]
    [InlineData("digiid://")]
    [InlineData("://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("bob@example.com")]
    [InlineData("https://user:pass@example.com")]
    public void Normalize_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(DigiIdDomain.Normalize(input));
    }

    [Fact]
    public void Normalize_OverLongHost_ReturnsNull()
    {
        var tooLong = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".com"; // over 253 characters

        Assert.True(tooLong.Length > 253);
        Assert.Null(DigiIdDomain.Normalize(tooLong));
    }

    [Fact]
    public void Normalize_NeverThrows_OnOddInput()
    {
        foreach (var input in new[] { "\0", "\n", "https://[", "http://%zz", "digiid://?x=1", new string('x', 5000), "https://exa\tmple.com" })
        {
            var ex = Record.Exception(() => DigiIdDomain.Normalize(input));
            Assert.Null(ex);
        }
    }
}

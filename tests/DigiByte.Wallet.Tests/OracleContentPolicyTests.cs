namespace DigiByte.Wallet.Tests;

/// <summary>
/// The page's content policy is not widened for the oracle (specs/014-digibyte-io-oracle FR-016): the browser never contacts digibyte.io, only the
/// project's own data service, which is already allowed.
/// </summary>
public class OracleContentPolicyTests
{
    private static string IndexHtml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "DigiByte.Web", "wwwroot", "index.html")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "DigiByte.Web", "wwwroot", "index.html"));
    }

    [Fact]
    public void The_content_policy_does_not_mention_digibyte_io()
    {
        Assert.DoesNotContain("digibyte.io", IndexHtml(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_content_policy_still_lists_the_data_service_origin()
    {
        Assert.Contains("https://digibyte-api-production.up.railway.app", IndexHtml());
    }
}

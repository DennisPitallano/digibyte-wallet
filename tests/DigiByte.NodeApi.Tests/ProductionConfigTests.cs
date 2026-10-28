using DigiByte.NodeApi.RpcClient;
using DigiByte.NodeApi.Services;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T006: in Production the node RPC credentials must be set and must not be the shipped
/// placeholder; Development keeps its convenient defaults.
/// </summary>
public class ProductionConfigTests
{
    private static NodeConfig Config(string user, string password) =>
        new() { RpcUser = user, RpcPassword = password, IsTestnet = false };

    [Theory]
    [InlineData("", "a-long-random-secret")]
    [InlineData("  ", "a-long-random-secret")]
    [InlineData("rpcuser", "")]
    [InlineData("rpcuser", "   ")]
    [InlineData("rpcuser", "changeme")]
    [InlineData("rpcuser", "ChangeMe")]
    public void Production_RejectsMissingOrDefaultCredentials(string user, string password)
    {
        var error = NodeConfigValidator.Validate(Config(user, password), isProduction: true);

        Assert.NotNull(error);
    }

    [Fact]
    public void Production_AcceptsRealCredentials()
    {
        Assert.Null(NodeConfigValidator.Validate(Config("dgbrpc", "a-long-random-secret"), isProduction: true));
    }

    [Theory]
    [InlineData("dgbrpc", "changeme")]
    [InlineData("", "")]
    public void Development_AllowsDefaults(string user, string password)
    {
        Assert.Null(NodeConfigValidator.Validate(Config(user, password), isProduction: false));
    }

    [Fact]
    public void ErrorMessage_NeverContainsTheSecret()
    {
        const string secret = "changeme";
        var error = NodeConfigValidator.Validate(Config("rpcuser", secret), isProduction: true);

        Assert.NotNull(error);
        Assert.DoesNotContain(secret, error!, StringComparison.OrdinalIgnoreCase);
    }
}

using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Services;

/// <summary>
/// Startup check for the node connection settings. In Production the RPC credentials must be set and
/// must not be the shipped placeholder, so a forgotten variable stops the service instead of
/// silently talking to a node with a well-known password.
/// </summary>
public static class NodeConfigValidator
{
    private const string Placeholder = "changeme";

    /// <summary>Returns an error message when the configuration must not be used, otherwise null. The message never contains secret values.</summary>
    public static string? Validate(NodeConfig config, bool isProduction)
    {
        if (!isProduction)
            return null;

        if (string.IsNullOrWhiteSpace(config.RpcUser))
            return "The node RPC user is not configured (set DIGIBYTE_RPC_USER).";

        if (string.IsNullOrWhiteSpace(config.RpcPassword))
            return "The node RPC password is not configured (set DIGIBYTE_RPC_PASSWORD).";

        if (string.Equals(config.RpcPassword.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase))
            return "The node RPC password is still the shipped placeholder; set DIGIBYTE_RPC_PASSWORD to a real secret.";

        return null;
    }
}

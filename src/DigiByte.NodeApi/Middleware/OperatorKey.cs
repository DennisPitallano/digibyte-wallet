using System.Security.Cryptography;
using System.Text;

namespace DigiByte.NodeApi.Middleware;

/// <summary>
/// The operator key check (<c>X-NodeApi-Key</c> against <c>DigiDollarConfig.WriteKey</c>) shared by every operator-only
/// route: the wallet-scoped DigiDollar routes and the usage-count summary. Fails closed: with no key configured the answer is
/// "not configured" (503) unless the host is Development.
/// </summary>
public static class OperatorKey
{
    public const string HeaderName = "X-NodeApi-Key";

    public enum Result
    {
        Allowed,
        Unauthorized,
        NotConfigured,
    }

    public static Result Check(string? configuredKey, string? providedKey, bool isDevelopment)
    {
        if (string.IsNullOrEmpty(configuredKey))
            return isDevelopment ? Result.Allowed : Result.NotConfigured;

        return FixedTimeEquals(providedKey ?? "", configuredKey) ? Result.Allowed : Result.Unauthorized;
    }

    /// <summary>Constant-time comparison to prevent timing attacks.</summary>
    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

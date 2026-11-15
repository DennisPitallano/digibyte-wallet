using System.Security.Cryptography;
using System.Text;

namespace DigiByte.SpvGateway.Health;

/// <summary>
/// The only way the gateway may mention an address or a transaction in a log (spec FR-021): a short hash prefix that lets an operator
/// tell two lines are about the same thing without learning what it is. A full address, a transaction id or a transaction body is never
/// written to a log, a file or a response header.
/// </summary>
public static class LogRedaction
{
    /// <summary>Eight hex characters of the SHA-256 of the text, or "none" for an empty one.</summary>
    public static string Short(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "none";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
    }
}

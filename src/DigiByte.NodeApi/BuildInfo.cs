namespace DigiByte.NodeApi;

/// <summary>
/// The commit the running build came from, for the public /api/health `commit` field (specs/008-security-hardening, FR-015).
/// NodeApi does not reference DigiByte.Server.Shared (that library is for the two public APIs only), so this is its own copy.
/// </summary>
public static class BuildInfo
{
    public const string Variable = "RAILWAY_GIT_COMMIT_SHA";

    public static string Commit => Resolve(Environment.GetEnvironmentVariable(Variable));

    public static string Resolve(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}

namespace DigiByte.Server.Shared;

/// <summary>The commit the running build came from, for the public /api/health `commit` field (specs/008-security-hardening, FR-015).</summary>
public static class BuildInfo
{
    public const string Variable = "RAILWAY_GIT_COMMIT_SHA";

    public static string Commit => Resolve(Environment.GetEnvironmentVariable(Variable));

    /// <summary>"unknown" when the variable is absent or blank; otherwise the trimmed value.</summary>
    public static string Resolve(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}

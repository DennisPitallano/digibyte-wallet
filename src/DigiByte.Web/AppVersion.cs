using System.Reflection;

namespace DigiByte.Web;

/// <summary>
/// App versioning: 0.x.x-alpha → 0.x.x-beta → 0.x.x-rc.1 → 1.0.0
/// </summary>
public static class AppVersion
{
    private static readonly Assembly _assembly = typeof(AppVersion).Assembly;

    /// <summary>Full version including pre-release tag, e.g. "0.2.0-alpha.1"</summary>
    public static string Version =>
        _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? _assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>Numeric-only version, e.g. "0.2.0"</summary>
    public static string ShortVersion =>
        _assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Pre-release channel: "alpha", "beta", "rc", or "stable"</summary>
    public static string Channel
    {
        get
        {
            var v = Version;
            if (v.Contains("-alpha")) return "alpha";
            if (v.Contains("-beta")) return "beta";
            if (v.Contains("-rc")) return "rc";
            return "stable";
        }
    }

    /// <summary>True if this is a pre-release build</summary>
    public static bool IsPreRelease => Channel != "stable";
}

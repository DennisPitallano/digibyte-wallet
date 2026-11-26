namespace DigiByte.Pay.Web;

/// <summary>
/// Identifies this build of the web app. It changes with every build, so a page can tell that the server it
/// was loaded from has been replaced (the POS reloads itself between sales when that happens).
/// </summary>
public static class BuildInfo
{
    public static readonly string Id = typeof(BuildInfo).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
}

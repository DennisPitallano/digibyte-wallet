using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>What an "Install app" control should do right now. The table is in specs/019-settings-install-app/data-model.md.</summary>
public enum InstallMode
{
    /// <summary>Already installed, or this browser cannot install: show nothing.</summary>
    Hidden,
    /// <summary>The browser has an install prompt ready (Android, desktop Chrome or Edge).</summary>
    Native,
    /// <summary>iPhone or iPad: show the Add to Home Screen steps.</summary>
    IosSteps,
    /// <summary>A browser that installs from its menu: show how.</summary>
    MenuSteps,
}

/// <summary>The snapshot <c>pwaInstall.getState</c> returns.</summary>
public sealed record InstallState(bool Standalone, bool Ios, bool Android, bool CanPrompt, bool NonSafariIos, bool PromptSeen);

/// <summary>
/// One rule for "is the wallet installed, and how could it be", shared by the Install app entry in Settings (spec 019) and
/// the first-payment celebration (spec 025, FR-027).
/// </summary>
public sealed class InstallAppState(IJSRuntime js)
{
    public static InstallMode ComputeInstallMode(InstallState s) =>
        s.Standalone ? InstallMode.Hidden
        : s.CanPrompt ? InstallMode.Native
        : s.Ios ? InstallMode.IosSteps
        : (s.Android || s.PromptSeen) ? InstallMode.MenuSteps
        : InstallMode.Hidden;

    /// <summary>The browser's current install state, or null when interop is unavailable.</summary>
    public async Task<InstallState?> GetStateAsync()
    {
        try { return await js.InvokeAsync<InstallState>("pwaInstall.getState"); }
        catch { return null; }
    }

    /// <summary>What an install control should do now. <see cref="InstallMode.Hidden"/> when the state cannot be read.</summary>
    public async Task<InstallMode> GetModeAsync() =>
        await GetStateAsync() is { } state ? ComputeInstallMode(state) : InstallMode.Hidden;

    /// <summary>Shows the browser's own install prompt. True when the user accepted it.</summary>
    public async Task<bool> PromptAsync()
    {
        try { return await js.InvokeAsync<bool>("pwaInstall.prompt"); }
        catch { return false; }
    }
}

using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

public class ThemeService
{
    private readonly IJSRuntime _js;
    private bool _isDark;
    private string _mode = "dark";

    /// <summary>True when the page is currently drawn dark. In "system" mode this is the value at the last read.</summary>
    public bool IsDark => _isDark;

    /// <summary>"light", "dark" or "system" (follow the device).</summary>
    public string Mode => _mode;

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Reads the saved choice. Call before showing theme controls; safe to call more than once.</summary>
    public async Task InitializeAsync()
    {
        _mode = await _js.InvokeAsync<string>("themeManager.getMode");
        _isDark = await _js.InvokeAsync<bool>("themeManager.isDark");
    }

    public async Task SetModeAsync(string mode)
    {
        _mode = mode is "light" or "system" ? mode : "dark";
        _isDark = await _js.InvokeAsync<bool>("themeManager.setMode", _mode);
        OnThemeChanged?.Invoke();
    }

    public Task ToggleAsync() => SetModeAsync(_isDark ? "light" : "dark");

    public Task SetDarkAsync(bool isDark) => SetModeAsync(isDark ? "dark" : "light");
}

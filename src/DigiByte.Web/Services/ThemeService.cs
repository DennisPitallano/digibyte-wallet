using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

public class ThemeService
{
    private readonly IJSRuntime _js;
    private bool _isDark;

    public bool IsDark => _isDark;

    public event Action? OnThemeChanged;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        _isDark = await _js.InvokeAsync<bool>("themeManager.init");
    }

    public async Task ToggleAsync()
    {
        _isDark = !_isDark;
        await _js.InvokeVoidAsync("themeManager.setDark", _isDark);
        OnThemeChanged?.Invoke();
    }

    public async Task SetDarkAsync(bool isDark)
    {
        _isDark = isDark;
        await _js.InvokeVoidAsync("themeManager.setDark", isDark);
        OnThemeChanged?.Invoke();
    }
}

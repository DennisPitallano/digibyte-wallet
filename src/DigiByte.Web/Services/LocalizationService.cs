using System.Text.Json;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Client-side localization service. Loads JSON translation files
/// and provides a lookup method for localized strings.
/// </summary>
public class LocalizationService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private Dictionary<string, string> _translations = new();
    private string _currentLocale = "en";

    public string CurrentLocale => _currentLocale;
    public event Action? OnLocaleChanged;

    public static readonly Dictionary<string, string> SupportedLocales = new()
    {
        ["en"] = "English",
        ["es"] = "Espa\u00F1ol",
        ["zh"] = "\u4E2D\u6587",
        ["ja"] = "\u65E5\u672C\u8A9E",
        ["ko"] = "\uD55C\uAD6D\uC5B4",
        ["tl"] = "Filipino",
        ["hi"] = "\u0939\u093F\u0928\u094D\u0926\u0940",
        ["ar"] = "\u0627\u0644\u0639\u0631\u0628\u064A\u0629",
        ["pt"] = "Portugu\u00EAs",
        ["fr"] = "Fran\u00E7ais",
    };

    public LocalizationService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public async Task InitializeAsync()
    {
        // Load saved locale from localStorage
        var saved = await _js.InvokeAsync<string?>("localStorage.getItem", "dgb-locale");
        if (saved != null && SupportedLocales.ContainsKey(saved))
        {
            await SetLocaleAsync(saved);
        }
        else
        {
            await LoadTranslationsAsync("en");
        }
    }

    public async Task SetLocaleAsync(string locale)
    {
        if (!SupportedLocales.ContainsKey(locale)) return;
        _currentLocale = locale;
        await LoadTranslationsAsync(locale);
        await _js.InvokeVoidAsync("localStorage.setItem", "dgb-locale", locale);
        OnLocaleChanged?.Invoke();
    }

    /// <summary>
    /// Get a translated string by key. Falls back to the key itself if not found.
    /// </summary>
    public string T(string key)
    {
        return _translations.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Get a translated string with format parameters.
    /// </summary>
    public string T(string key, params object[] args)
    {
        var template = T(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    private async Task LoadTranslationsAsync(string locale)
    {
        try
        {
            var json = await _http.GetStringAsync($"locales/{locale}.json");
            _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            // Fallback to English keys
            _translations = new();
        }
    }
}

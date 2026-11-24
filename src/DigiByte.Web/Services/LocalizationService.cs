using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// The wallet's language (spec 028; contracts/localization-service.md). Loads the JSON translation files, looks up
/// translated text with an English fallback, and makes number and date style follow the chosen language.
/// <para>
/// Startup order: the language saved in Settings, else the browser's first language if it is offered (not saved, so it is
/// never mistaken for a choice), else English. A switch only happens once its translation file has loaded.
/// </para>
/// </summary>
public class LocalizationService
{
    private const string StorageKey = "dgb-locale";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly Action<CultureInfo> _applyCulture;
    private Dictionary<string, string> _translations = new();
    // English is always loaded as the fallback so a string missing from another locale shows English, not its raw key.
    private Dictionary<string, string> _english = new();
    private string _currentLocale = "en";

    public string CurrentLocale => _currentLocale;

    /// <summary>The culture for the current language: number, date and currency style everywhere in the wallet.</summary>
    public CultureInfo CurrentCulture => CultureFor(_currentLocale);

    /// <summary>Raised once after a successful switch, when text, culture and the saved choice are all updated.</summary>
    public event Action? OnLocaleChanged;

    /// <summary>
    /// The languages offered in Settings, code -> name in that language, in the order they are listed. Only languages
    /// that ship a <c>wwwroot/locales/{code}.json</c> belong here (spec 028, FR-003); adding one means adding its file,
    /// one line here, and passing the locale-file tests.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SupportedLocales = new Dictionary<string, string>
    {
        ["en"] = "English",
        ["es"] = "Español",
    };

    public LocalizationService(HttpClient http, IJSRuntime js) : this(http, js, ApplyToProcess) { }

    /// <summary>
    /// For tests: <paramref name="applyCulture"/> replaces setting the process-wide culture, which would leak into other
    /// tests running at the same time. The app uses the two-argument constructor.
    /// </summary>
    public LocalizationService(HttpClient http, IJSRuntime js, Action<CultureInfo> applyCulture)
    {
        _http = http;
        _js = js;
        _applyCulture = applyCulture;
    }

    /// <summary>
    /// Makes <paramref name="culture"/> the wallet's number and date style. The defaults are what every later render reads;
    /// a culture set only on the current async flow would be lost when that flow ends.
    /// </summary>
    private static void ApplyToProcess(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>English uses US number style; every other language uses its own neutral culture.</summary>
    public static CultureInfo CultureFor(string locale) =>
        CultureInfo.GetCultureInfo(locale == "en" ? "en-US" : locale);

    /// <summary>Picks the language at startup. Never writes storage and never throws.</summary>
    public async Task InitializeAsync()
    {
        await ChooseAtStartupAsync();
        await SetDocumentLanguageAsync();
    }

    private async Task ChooseAtStartupAsync()
    {
        _english = await TryLoadAsync("en") ?? new();

        var saved = await TryJsAsync<string?>("localStorage.getItem", StorageKey);
        if (saved is not null && SupportedLocales.ContainsKey(saved) && await TryUseAsync(saved))
            return;

        var browser = await TryJsAsync<string?>("domHelpers.getPreferredLanguage");
        var primary = browser?.Split('-', '_')[0].Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(primary) && SupportedLocales.ContainsKey(primary) && await TryUseAsync(primary))
            return;

        Use("en", _english);
    }

    /// <summary>The page's lang attribute follows the wallet language, so screen readers pronounce it right (FR-012).</summary>
    private Task SetDocumentLanguageAsync() => TryJsVoidAsync("domHelpers.setDocumentLanguage", _currentLocale);

    /// <summary>
    /// Switches to <paramref name="locale"/> once its file has loaded, saves the choice and raises <see cref="OnLocaleChanged"/>.
    /// Returns false, changing nothing, when the language isn't offered or its file can't be loaded.
    /// </summary>
    public async Task<bool> SetLocaleAsync(string locale)
    {
        if (!SupportedLocales.ContainsKey(locale)) return false;
        if (!await TryUseAsync(locale)) return false;

        // Saving is best effort: with storage blocked the switch still holds for this visit.
        await TryJsVoidAsync("localStorage.setItem", StorageKey, locale);
        await SetDocumentLanguageAsync();
        OnLocaleChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Get a translated string by key. Falls back to English, then to the key itself, if not found.
    /// </summary>
    public string T(string key)
    {
        if (_translations.TryGetValue(key, out var value)) return value;
        return _english.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>
    /// Get a translated string with format parameters, formatted in the current language's number style.
    /// </summary>
    public string T(string key, params object[] args)
    {
        var template = T(key);
        try { return string.Format(CurrentCulture, template, args); }
        catch { return template; }
    }

    private async Task<bool> TryUseAsync(string locale)
    {
        var translations = locale == "en" ? (_english.Count > 0 ? _english : await TryLoadAsync("en")) : await TryLoadAsync(locale);
        if (translations is null) return false;
        if (locale == "en") _english = translations;
        Use(locale, translations);
        return true;
    }

    private void Use(string locale, Dictionary<string, string> translations)
    {
        _currentLocale = locale;
        _translations = translations;
        _applyCulture(CultureFor(locale));
    }

    private async Task<Dictionary<string, string>?> TryLoadAsync(string locale)
    {
        try
        {
            var json = await _http.GetStringAsync($"locales/{locale}.json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<T?> TryJsAsync<T>(string identifier, params object?[] args)
    {
        try { return await _js.InvokeAsync<T>(identifier, args); }
        catch { return default; }
    }

    private async Task TryJsVoidAsync(string identifier, params object?[] args)
    {
        try { await _js.InvokeVoidAsync(identifier, args); }
        catch { /* storage blocked: keep the switch for this visit */ }
    }
}

using System.Globalization;
using System.Net;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How the wallet picks and switches its language (spec 028, FR-004–FR-006a, FR-011; contracts/localization-service.md).
/// Every test restores the thread culture, because the service sets it.
/// <para>
/// The service is given a recorder instead of setting the process-wide culture, so these tests can't change the number
/// style other tests see while they run in parallel.
/// </para>
/// </summary>
public class LocalizationServiceTests
{
    private const string EnJson = """{ "hello": "Hello", "only.en": "Only English" }""";
    private const string EsJson = """{ "hello": "Hola" }""";

    /// <summary>Serves the locale files, or 404 for any code listed as missing, and records every request.</summary>
    private sealed class FakeFiles(params string[] missing) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Requests.Add(path);
            string? body = path switch
            {
                "locales/en.json" when !missing.Contains("en") => EnJson,
                "locales/es.json" when !missing.Contains("es") => EsJson,
                _ => null,
            };
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>An in-memory localStorage and browser language; either can be made to throw.</summary>
    private sealed class FakeBrowser : IJSRuntime
    {
        public Dictionary<string, string> Storage { get; } = [];
        public List<string> Writes { get; } = [];
        public string? BrowserLanguage { get; set; } = "en-US";
        public bool StorageThrows { get; set; }
        public bool LanguageThrows { get; set; }
        public CultureInfo? AppliedCulture { get; set; }
        /// <summary>Every value given to the page's lang attribute, in order.</summary>
        public List<string> DocumentLanguages { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.getItem":
                    if (StorageThrows) throw new JSException("storage blocked");
                    return new((TValue)(object?)Storage.GetValueOrDefault((string)args![0]!)!);
                case "localStorage.setItem":
                    if (StorageThrows) throw new JSException("storage blocked");
                    Storage[(string)args![0]!] = (string)args[1]!;
                    Writes.Add((string)args[0]!);
                    return new(default(TValue)!);
                case "domHelpers.getPreferredLanguage":
                    if (LanguageThrows) throw new JSException("no navigator");
                    return new((TValue)(object?)BrowserLanguage!);
                case "domHelpers.setDocumentLanguage":
                    DocumentLanguages.Add((string)args![0]!);
                    return new(default(TValue)!);
                default:
                    throw new InvalidOperationException($"Unexpected JS call {identifier}");
            }
        }
    }

    private static (LocalizationService Service, FakeBrowser Browser, FakeFiles Files) Create(params string[] missingFiles)
    {
        var files = new FakeFiles(missingFiles);
        var http = new HttpClient(files) { BaseAddress = new Uri("http://localhost/") };
        var browser = new FakeBrowser();
        return (new LocalizationService(http, browser, c => browser.AppliedCulture = c), browser, files);
    }

    [Fact]
    public async Task SavedSpanish_OpensInSpanish_WithoutWritingStorage()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        browser.Storage["dgb-locale"] = "es";

        await service.InitializeAsync();

        Assert.Equal("es", service.CurrentLocale);
        Assert.Equal("es", browser.AppliedCulture?.Name);
        Assert.Equal("es", service.CurrentCulture.Name);
        Assert.Equal("Hola", service.T("hello"));
        Assert.Empty(browser.Writes);
    }

    [Fact]
    public async Task UnknownSavedValue_AndSpanishBrowser_OpensInSpanish_WithoutSaving()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        browser.Storage["dgb-locale"] = "xx";
        browser.BrowserLanguage = "es-MX";

        await service.InitializeAsync();

        Assert.Equal("es", service.CurrentLocale);
        Assert.Equal("xx", browser.Storage["dgb-locale"]);
        Assert.Empty(browser.Writes);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("")]
    [InlineData(null)]
    public void NoSavedChoice_AndAnUnofferedBrowserLanguage_OpensInEnglish(string? browserLanguage)
    {
        using var _ = new CultureScope("es");
        var (service, browser, _) = Create();
        browser.BrowserLanguage = browserLanguage;

        service.InitializeAsync().GetAwaiter().GetResult();

        Assert.Equal("en", service.CurrentLocale);
        Assert.Equal("en-US", browser.AppliedCulture?.Name);
    }

    [Fact]
    public async Task SavedLanguageNoLongerOffered_AndEnglishBrowser_OpensInEnglish()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        browser.Storage["dgb-locale"] = "fr";
        browser.BrowserLanguage = "en-GB";

        await service.InitializeAsync();

        Assert.Equal("en", service.CurrentLocale);
        Assert.Equal("English", LocalizationService.SupportedLocales[service.CurrentLocale]);
    }

    [Fact]
    public async Task BrowserAndStorageThrowing_OpensInEnglish_WithoutAnError()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        browser.StorageThrows = true;
        browser.LanguageThrows = true;

        await service.InitializeAsync();

        Assert.Equal("en", service.CurrentLocale);
    }

    [Fact]
    public async Task SavedSpanish_ButItsFileIsMissing_ShowsEnglish_AndKeepsTheChoice()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create("es");
        browser.Storage["dgb-locale"] = "es";

        await service.InitializeAsync();

        Assert.Equal("en", service.CurrentLocale);
        Assert.Equal("Hello", service.T("hello"));
        Assert.Equal("es", browser.Storage["dgb-locale"]);
    }

    [Fact]
    public async Task Switching_WhenTheFileIsMissing_ChangesNothing()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create("es");
        await service.InitializeAsync();
        var raised = 0;
        service.OnLocaleChanged += () => raised++;

        var switched = await service.SetLocaleAsync("es");

        Assert.False(switched);
        Assert.Equal("en", service.CurrentLocale);
        Assert.Equal("en-US", browser.AppliedCulture?.Name);
        Assert.Empty(browser.Writes);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Switching_ToSpanish_AppliesSavesAndAnnouncesOnce_AndTouchesNothingElse()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, files) = Create();
        await service.InitializeAsync();
        var raised = 0;
        service.OnLocaleChanged += () => raised++;

        var switched = await service.SetLocaleAsync("es");

        Assert.True(switched);
        Assert.Equal("es", service.CurrentLocale);
        Assert.Equal("es", browser.AppliedCulture?.Name);
        Assert.Equal("Hola", service.T("hello"));
        Assert.Equal("Only English", service.T("only.en")); // missing in Spanish → English, never the key
        Assert.Equal(1, raised);
        // FR-011: only the language is stored, and only the wallet's own locale files are fetched.
        Assert.Equal(["dgb-locale"], browser.Writes);
        Assert.Equal("es", browser.Storage["dgb-locale"]);
        Assert.All(files.Requests, r => Assert.Matches(@"^locales/[a-z]{2}\.json$", r));
    }

    [Fact]
    public async Task Switching_WhenStorageIsBlocked_StillSwitchesForThisVisit()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        await service.InitializeAsync();
        browser.StorageThrows = true;

        Assert.True(await service.SetLocaleAsync("es"));
        Assert.Equal("es", service.CurrentLocale);
    }

    [Fact]
    public async Task Switching_ToALanguageThatIsNotOffered_IsRefused()
    {
        using var _ = new CultureScope("en-US");
        var (service, _, _) = Create();
        await service.InitializeAsync();

        Assert.False(await service.SetLocaleAsync("fr"));
        Assert.Equal("en", service.CurrentLocale);
    }

    [Fact]
    public async Task FormattedText_UsesTheChosenLanguagesNumberStyle()
    {
        using var _ = new CultureScope("en-US");
        var (service, browser, _) = Create();
        browser.Storage["dgb-locale"] = "es";
        await service.InitializeAsync();

        // An unknown key falls back to itself, so the format string is the key: enough to see the culture used.
        Assert.Equal("1,5", service.T("{0}", 1.5m));
    }

    // ---------- FR-012: screen readers pronounce the page in the chosen language ----------

    [Fact]
    public async Task Startup_SetsThePageLanguage()
    {
        var (service, browser, _) = Create();
        browser.Storage["dgb-locale"] = "es";

        await service.InitializeAsync();

        Assert.Equal(["es"], browser.DocumentLanguages);
    }

    [Fact]
    public async Task Startup_InEnglish_SetsThePageLanguageToEnglish()
    {
        var (service, browser, _) = Create();
        browser.BrowserLanguage = "de-DE";

        await service.InitializeAsync();

        Assert.Equal(["en"], browser.DocumentLanguages);
    }

    [Fact]
    public async Task Switching_SetsThePageLanguage_AndAFailedSwitchDoesNot()
    {
        var (service, browser, _) = Create();
        await service.InitializeAsync();

        Assert.True(await service.SetLocaleAsync("es"));
        Assert.Equal(["en", "es"], browser.DocumentLanguages);

        Assert.False(await service.SetLocaleAsync("fr"));
        Assert.Equal(["en", "es"], browser.DocumentLanguages);
    }

    [Fact]
    public async Task SettingThePageLanguage_CanFail_WithoutBreakingTheSwitch()
    {
        var throwing = new HttpClient(new FakeFiles()) { BaseAddress = new Uri("http://localhost/") };
        var js = new ThrowingOnLangBrowser();
        var s = new LocalizationService(throwing, js, _ => { });

        await s.InitializeAsync();

        Assert.True(await s.SetLocaleAsync("es"));
        Assert.Equal("es", s.CurrentLocale);
    }

    /// <summary>A browser whose page-language call fails (script not loaded yet).</summary>
    private sealed class ThrowingOnLangBrowser : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            identifier == "domHelpers.setDocumentLanguage"
                ? throw new JSException("domHelpers not loaded")
                : new(default(TValue)!);
    }
}

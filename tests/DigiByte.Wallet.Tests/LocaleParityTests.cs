using System.Text.Json;
using System.Text.RegularExpressions;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// SC-008 and FR-022: every string this feature added or changed exists in both English and Spanish, none is empty or
/// left in English in the Spanish file, placeholders match, and the wording the code relies on has not drifted.
/// </summary>
public class LocaleParityTests
{
    // Keys added or reworded by this feature, by prefix.
    private static readonly string[] FeaturePrefixes =
    [
        "protection.", "create.passphrase.", "create.credential.", "create.pin_limitation", "passphrase.reject.",
        "credential.", "throttle.", "upgrade.notice.", "biometric.upgrade.", "biometric.weak.", "legacy.",
        "unlock.passphrase_prompt", "unlock.forgot_passphrase", "unlock.or_passphrase", "recover.new_passphrase",
    ];

    // Words that are the same in both languages on purpose.
    private static readonly HashSet<string> SameInBoth = ["credential.word_pin"];

    private static string LocalesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DigiByte.Web", "wwwroot", "locales");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find src/DigiByte.Web/wwwroot/locales above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> Load(string locale) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LocalesDirectory(), locale + ".json")))!;

    private static bool IsFeatureKey(string key) => FeaturePrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal))
                                                    || key == "common.cancel";

    [Fact]
    public void Every_feature_key_in_English_exists_in_Spanish_and_neither_is_empty()
    {
        var en = Load("en");
        var es = Load("es");

        var featureKeys = en.Keys.Where(IsFeatureKey).ToList();
        Assert.NotEmpty(featureKeys);

        foreach (var key in featureKeys)
        {
            Assert.True(es.ContainsKey(key), $"Spanish is missing '{key}'");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"English '{key}' is empty");
            Assert.False(string.IsNullOrWhiteSpace(es[key]), $"Spanish '{key}' is empty");
        }
        foreach (var key in es.Keys.Where(IsFeatureKey))
            Assert.True(en.ContainsKey(key), $"English is missing '{key}'");
    }

    [Fact]
    public void Spanish_feature_strings_are_not_left_in_English()
    {
        var en = Load("en");
        var es = Load("es");

        foreach (var key in en.Keys.Where(IsFeatureKey).Where(k => !SameInBoth.Contains(k) && es.ContainsKey(k)))
            Assert.NotEqual(en[key], es[key]);
    }

    [Fact]
    public void Placeholders_match_between_English_and_Spanish()
    {
        var en = Load("en");
        var es = Load("es");
        var placeholder = new Regex(@"\{\d+\}");

        foreach (var key in en.Keys.Where(IsFeatureKey).Where(es.ContainsKey))
        {
            var a = placeholder.Matches(en[key]).Select(m => m.Value).OrderBy(x => x).ToList();
            var b = placeholder.Matches(es[key]).Select(m => m.Value).OrderBy(x => x).ToList();
            Assert.True(a.SequenceEqual(b), $"'{key}' has different placeholders in English and Spanish");
        }
    }

    [Fact]
    public void Every_literal_key_the_feature_screens_ask_for_exists_in_both_languages()
    {
        var en = Load("en");
        var es = Load("es");
        var web = Path.Combine(LocalesDirectory(), "..", "..");
        var files = new[]
        {
            "Components/Shared/CredentialInput.razor", "Components/Wallet/WalletProtectionCard.razor",
            "Components/BiometricNotice.razor", "Components/LegacyProtectionConsent.razor",
            "Pages/CreateWallet.razor", "Pages/Unlock.razor",
        };
        var call = new Regex("L\\.T\\(\"([^\"]+)\"");

        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.GetFullPath(Path.Combine(web, file)));
            foreach (Match m in call.Matches(text).Where(m => IsFeatureKey(m.Groups[1].Value)))
            {
                var key = m.Groups[1].Value;
                Assert.True(en.ContainsKey(key), $"{file} uses '{key}', missing in English");
                Assert.True(es.ContainsKey(key), $"{file} uses '{key}', missing in Spanish");
            }
        }
    }

    [Fact]
    public void Every_passphrase_rejection_reason_has_text_in_both_languages()
    {
        var en = Load("en");
        var es = Load("es");

        foreach (var reason in Enum.GetValues<PassphraseRejection>().Where(r => r != PassphraseRejection.None))
        {
            var key = CredentialPolicy.ReasonKey(reason);
            Assert.True(en.ContainsKey(key) && es.ContainsKey(key), key);
        }
    }

    // ---------- the wording the code relies on ----------

    [Theory]
    [InlineData(CredentialKind.Pin)]
    [InlineData(CredentialKind.Passphrase)]
    public async Task The_English_locale_matches_the_reference_messages_the_tests_check(CredentialKind kind)
    {
        var l = await LocalizerAsync("en");

        Assert.Equal(CredentialMessages.Incorrect(kind), CredentialText.Incorrect(l, kind));
        Assert.Equal(CredentialMessages.BiometricFailed(kind), CredentialText.BiometricFailed(l, kind));
        foreach (var failure in Enum.GetValues<CryptoFailure>())
            Assert.Equal(CredentialMessages.ForFailure(kind, failure), CredentialText.ForFailure(l, kind, failure));
    }

    [Fact]
    public async Task The_English_locale_matches_the_deferred_upgrade_reference_messages()
    {
        var l = await LocalizerAsync("en");

        Assert.Equal(CredentialMessages.MigrationDeferred(CryptoFailure.Unsupported), CredentialText.MigrationDeferred(l, CryptoFailure.Unsupported));
        Assert.Equal(CredentialMessages.MigrationDeferred(CryptoFailure.ResourceExhausted), CredentialText.MigrationDeferred(l, CryptoFailure.ResourceExhausted));
    }

    [Fact]
    public async Task The_throttle_reads_its_wording_from_the_locale_and_English_matches_its_built_in_text()
    {
        var en = await LocalizerAsync("en");
        var now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var builtIn = new PinAttemptThrottle(() => now);
        var fromLocale = new PinAttemptThrottle(() => now) { Localize = en.T };

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(builtIn.RecordFailure(), fromLocale.RecordFailure());
            Assert.Equal(builtIn.LockoutMessage, fromLocale.LockoutMessage);
        }
    }

    [Theory]
    [InlineData(CredentialKind.Pin, "PIN incorrecto")]
    [InlineData(CredentialKind.Passphrase, "Frase de acceso incorrecta")]
    public async Task Spanish_users_see_Spanish_for_a_wrong_credential_and_the_attempt_counts(CredentialKind kind, string expectedStart)
    {
        var es = await LocalizerAsync("es");
        var throttle = new PinAttemptThrottle { Kind = kind, Localize = es.T };

        var first = throttle.RecordFailure();

        Assert.StartsWith(expectedStart, first);
        Assert.Contains("2", first);
        Assert.DoesNotContain("{", first);
        Assert.DoesNotContain("attempt", first);
    }

    [Fact]
    public async Task Spanish_environmental_messages_never_call_the_credential_wrong_and_have_no_leftover_placeholders()
    {
        var es = await LocalizerAsync("es");

        foreach (var kind in new[] { CredentialKind.Pin, CredentialKind.Passphrase })
        foreach (var failure in new[] { CryptoFailure.Unreadable, CryptoFailure.UnsupportedVersion, CryptoFailure.ResourceExhausted, CryptoFailure.Unsupported })
        {
            var text = CredentialText.ForFailure(es, kind, failure);
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain(CredentialText.Incorrect(es, kind), text); // never the "wrong PIN / passphrase" message
            Assert.NotEqual(CredentialMessages.ForFailure(kind, failure), text); // translated, not English
        }
    }

    [Fact]
    public async Task The_update_required_message_exists_in_both_languages_and_is_not_a_wrong_credential_message()
    {
        foreach (var locale in new[] { "en", "es" })
        {
            var l = await LocalizerAsync(locale);
            var text = CredentialText.ForFailure(l, CredentialKind.Pin, CryptoFailure.UnsupportedVersion);

            Assert.NotEqual("credential.update_required", text); // not a raw key
            Assert.NotEqual(CredentialText.Incorrect(l, CredentialKind.Pin), text);
        }
    }

    private static async Task<LocalizationService> LocalizerAsync(string locale)
    {
        var http = new HttpClient(new LocaleFiles(LocalesDirectory())) { BaseAddress = new Uri("http://localhost/") };
        var l = new LocalizationService(http, new NullJs());
        await l.SetLocaleAsync(locale);
        return l;
    }

    private sealed class LocaleFiles(string directory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            var path = Path.Combine(directory, name);
            return Task.FromResult(File.Exists(path)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(path)) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class NullJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}

# Contract: LocalizationService

`src/DigiByte.Web/Services/LocalizationService.cs` is the only owner of the active language. Changes from today are marked **new** or **changed**.

## Members

| Member | Contract |
|--------|----------|
| `static IReadOnlyDictionary<string,string> SupportedLocales` **changed** | Exactly the offered languages, code → native name: `en` → "English", `es` → "Español". |
| `string CurrentLocale` | The active code. Always a key of `SupportedLocales`. |
| `CultureInfo CurrentCulture` **new** | The culture for `CurrentLocale` (`en-US` or `es`). |
| `event Action? OnLocaleChanged` | Raised once after a successful switch, after culture and storage are updated. Not raised on a failed switch or on startup. |
| `Task InitializeAsync()` **changed** | Picks the active language by the startup rules in [data-model.md](../data-model.md#active-language). Sets the thread cultures. Never writes storage. Never throws: storage or JS failures fall back to English. |
| `Task<bool> SetLocaleAsync(string code)` **changed** | Unknown code → `false`, no change. Loads the file; if loading fails → `false`, no change. If it loads: sets `CurrentLocale`, sets the thread cultures, writes `localStorage["dgb-locale"]` (a storage failure is ignored), raises `OnLocaleChanged`, and returns `true`. |
| `string T(string key)` | Translation in the active language, else English, else the key. The last case is a bug that the key usage test catches. |
| `string T(string key, params object[] args)` | As above, then `string.Format(CurrentCulture, …)`. |

## Invariants

- No network call other than `GET locales/{code}.json` from the wallet's own origin (FR-011; CSP unchanged).
- Changing the language never touches wallet storage, keys, balances or other settings (FR-011).
- After `InitializeAsync`, `CultureInfo.CurrentCulture.Name` matches `CurrentCulture.Name`.

## Required tests (`tests/DigiByte.Wallet.Tests/LocalizationServiceTests.cs`)

These use fake `HttpClient` and `IJSRuntime` objects.

- Saved `es` with the file available: active `es`, culture `es`, no storage write.
- Saved `xx` and browser `es-MX`: active `es`, nothing saved.
- No saved choice and browser `de-DE`: active `en`.
- Browser JS throws: active `en`, no exception.
- `SetLocaleAsync("es")` with the file missing: returns `false`; locale, culture and storage are unchanged; no event.
- `SetLocaleAsync("es")` succeeds: returns `true`, storage is written, the event fires once.
- `SetLocaleAsync("fr")` returns `false`.

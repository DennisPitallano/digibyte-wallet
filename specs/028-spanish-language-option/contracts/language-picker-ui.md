# Contract: Language setting UI

## Settings row (`Pages/Settings.razor`)

- Label: `L.T("settings.language")`. The value shows `SupportedLocales[L.CurrentLocale]`, the native name of the language in use (FR-005).
- Tapping the row opens `LanguagePickerSheet`. The "coming soon" dialog, `showLanguageToast` and `ShowLanguageComingSoon` are removed (FR-001, SC-005).

## LanguagePickerSheet (`Components/Shared/LanguagePickerSheet.razor`, new)

| Parameter | Type | Meaning |
|-----------|------|---------|
| `IsOpen` | bool | The sheet is shown. |
| `OnClosed` | EventCallback | Raised on dismiss or after a successful pick. |

How it behaves:

- Lists every `SupportedLocales` entry in a fixed order (English, Español), each in its native name. The active one is marked with a check and `aria-checked="true"`.
- The list uses `role="radiogroup"`; each item is a `role="radio"` button. It works with Tab, arrow keys and Enter/Space, closes on Escape, and returns focus to the Settings row on close (FR-012).
- Fits 375 px wide with no horizontal scroll.
- Picking the active language just closes the sheet.
- Picking another language calls `SetLocaleAsync`:
  - `true` → the sheet closes, and the screen behind is already in the new language.
  - `false` → the sheet stays open and shows `language.loadFailed` ("Couldn't load {0}. Try again when you're online.").
- Picking does not reload the page and keeps any state on other screens (FR-004).

## Live update (`Layout/MainLayout.razor`)

- Subscribes to `L.OnLocaleChanged` → `InvokeAsync(StateHasChanged)`, and unsubscribes on dispose.
- Wraps all of its markup (the whole shell: page, navigation, banners, toasts) in `<CascadingValue Name="Locale" Value="L.CurrentLocale">` (research R13).
- Every in-scope page and component (FR-009) declares `[CascadingParameter(Name = "Locale")] public string? Locale { get; set; }`, so it re-renders on a switch. That includes the navigation, banners and toast container, which sit inside the same cascading value.

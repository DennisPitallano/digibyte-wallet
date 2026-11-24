# Tasks: Spanish in the Language Setting

**Input**: Design documents from `specs/028-spanish-language-option/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (Principle III) requires tests written first for every `DigiByte.Wallet` change, and the plan names the test files. Tests marked "write first" must fail before the matching implementation task.

**Organization**: Tasks are grouped by user story so each story can be built and checked on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks). One exception: tasks that add translation keys may share `en.json` and `es.json`. Each adds only its own key prefix and never edits another task's keys, so parallel work merges by keeping both blocks (see "Locale files" under Dependencies).
- **[Story]**: US1, US2 or US3 from spec.md
- Paths are relative to the repository root.

## Conventions for every translation task

- **Keys** use the dotted style with one prefix per screen: `settings.*`, `welcome.*`, `create.*`, `backup.*`, `recover.*`, `unlock.*`, `home.*`, `receive.*`, `send.*`, `history.*`, `amount.*`, `language.*`. Reuse an existing key when its English text is identical.
- **Text with values** uses `L.T("key", arg0, …)` with `{0}` placeholders, never string concatenation. Plural forms get two keys (`….one` / `….other`).
- **Both files**: add every new key to `src/DigiByte.Web/wwwroot/locales/en.json` **and** `src/DigiByte.Web/wwwroot/locales/es.json` in the same task.
- **Spanish style** (research R11): neutral Spanish, informal "tú", "billetera" for wallet ("Ingresa" for entering text), "dirección", "saldo", "frase de recuperación". Never translate DigiByte, DGB, DigiDollar or Digi-ID.
- **What gets translated**: visible text, `placeholder`, `title`, `aria-label` and toast/error messages. Not CSS classes, routes, log messages or `data-*` values.
- **Live switching**: each page or component translated in US2 also gets `[CascadingParameter(Name = "Locale")] public string? Locale { get; set; }` in its `@code` block (contract: [language-picker-ui.md](contracts/language-picker-ui.md#live-update-layoutmainlayoutrazor)), and `@inject LocalizationService L` if missing.
- **Warnings**: any key that is a security or money warning (recovery phrase, PIN, send confirmation, address warnings) gets a row in `specs/028-spanish-language-option/translation-review.md` with approver and date left blank.

---

## Phase 1: Setup

**Purpose**: Branch and working files.

- [X] T001 Create branch `feature/spanish-language-option` from `main` (constitution: `feature/<slug>`) and confirm `dotnet build src/src.sln` and `dotnet test tests/DigiByte.Wallet.Tests` pass before any change, noting any test that already fails.
- [X] T002 [P] Create `specs/028-spanish-language-option/translation-review.md`. Add a short intro saying the release is blocked until every row has an approver (SC-006), and a table with columns `key | English | Español | approvedBy | date`. Pre-fill rows for the existing warning keys in `src/DigiByte.Web/wwwroot/locales/es.json` whose names start with `identity.addrWarning`, `create.phrase_warning`, or contain `warning`.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Make machine-read numbers immune to the display language (research R5), and add the locale-file guards. **US1 sets the thread culture, so it MUST NOT start until T003–T015 are done.** These tasks also fix four bugs that are already live: payment links written with a comma on Spanish browsers (research F7), comma amounts in incoming links read as 15 instead of 1,5, "12,5" typed on Send read as 125 (F8), and the same in spending limits.

- [X] T003 [P] Write first: in `tests/DigiByte.Wallet.Tests/PaymentRequestTests.cs` (new), add tests that run under `CultureInfo.CurrentCulture = new CultureInfo("es")` and restore it afterwards:
  - `new PaymentRequest { Id="", Address=<valid testnet or mainnet address used elsewhere in the tests>, AmountDgb=1.5m }.ToUri()` contains `amount=1.50000000`.
  - `PaymentRequest.FromUri("digibyte:<addr>?amount=1.5").AmountDgb == 1.5m`.
  - `FromUri("digibyte:<addr>?amount=1,5").AmountDgb` is `null`, because a comma is not valid in a payment link and must not read as 15.
  - `FromUri("digibyte:<addr>?amount=1,000.5").AmountDgb` is `null`.
- [X] T004 Fix `src/DigiByte.Wallet/Models/PaymentRequest.cs` so the T003 tests pass:
  - In `ToUri()`, format the amount with `AmountDgb.Value.ToString("F8", CultureInfo.InvariantCulture)`.
  - In `FromUri`, parse `amount` with `NumberStyles.AllowDecimalPoint` (not `NumberStyles.Any`) and `CultureInfo.InvariantCulture`.
- [X] T005 [P] In `src/DigiByte.Web/Components/Shared/TransactionDetailModal.razor` (line ~255), build the send link with `amount.ToString(CultureInfo.InvariantCulture)` instead of `{amount}`.
- [X] T006 [P] In `src/DigiByte.Web/Pages/Send.razor` (line ~861, query `amount`) and `src/DigiByte.Web/Pages/MultisigSend.razor` (lines ~379 and ~392, `QueryAmount`), parse with `NumberStyles.AllowDecimalPoint` + `CultureInfo.InvariantCulture`, so `?amount=1,5` is rejected rather than read as 15.
- [X] T007 [P] In `src/DigiByte.Web/Services/DonationInfo.cs:15`, make `Bip21Uri` format `amountDgb` with `CultureInfo.InvariantCulture` (an `int` today, but keep it safe if the type changes), and add a case to the existing `tests/DigiByte.Wallet.Tests/DonationInfoTests.cs` that runs under `es` culture.
- [X] T008 Audit the rest of `src/DigiByte.Web` and `src/DigiByte.Wallet` for culture-sensitive machine output, using the search patterns in research R5:
  - `decimal`/`double` interpolated into strings with `amount=`, `?`, `&` or `:`.
  - `ToString()` / `ToString("F…")` / `ToString("N…")` results passed to `NavigateTo`, JS clipboard/share/download calls, `JsonSerializer`, `localStorage`, or any HTTP body or query.
  - `decimal`/`double` `Parse`/`TryParse` without an explicit culture.

  Fix every hit to use `CultureInfo.InvariantCulture`, and list what was checked and changed in a new "R5 audit result" section at the end of `specs/028-spanish-language-option/research.md`.
- [X] T009 [P] Write first: `tests/DigiByte.Wallet.Tests/AmountEntryTests.cs` (new) as a `[Theory]` covering **every row** of the example table in [contracts/amount-entry.md](contracts/amount-entry.md#examples-required-unit-tests-testsdigibytewallettestsamountentrytestscs), all with `maxDecimals: 8, maxValue: 21_000_000_000m`. Include:
  - `"1 234,5"` → Value 1234.5.
  - The same with a no-break space instead of the space (U+00A0; build it in C# as `"1" + (char)0xA0 + "234,5"`) → Value 1234.5.
  - `",5"` → Value 0.5, and `"5."` → Value 5.
  - Rule 10: an `Ambiguous` input where only one reading is valid, e.g. `"1.234"` with `maxDecimals: 2` → `Value(1234)`.

  Also run each case under `CultureInfo.CurrentCulture` `en-US` and `es` and assert the result is the same (a culture-independence test).
- [X] T010 Create `src/DigiByte.Wallet/Services/AmountEntry.cs`:
  - `public static AmountEntryResult Parse(string? text, int maxDecimals, decimal maxValue)`.
  - `AmountEntryResult` is a sealed record hierarchy or a record with a `Kind` enum `{ Empty, Value, Ambiguous, Invalid }`, holding `decimal? Amount`, `decimal? AsDecimal`, `decimal? AsThousands` and `AmountEntryError? Error`.
  - `enum AmountEntryError { NotANumber, MoreThanOneMark, TooManyDecimals, NotPositive, TooLarge }`.
  - Follow rules 1–10 in [contracts/amount-entry.md](contracts/amount-entry.md#rules-applied-in-order) exactly: build the decimal from digit strings with `decimal.Parse(..., CultureInfo.InvariantCulture)`, never with the current culture. "Whitespace" means `char.IsWhiteSpace`, plus U+00A0 and U+202F.

  Make T009 pass.
- [X] T011 Fix the "12,5 → 125" bug in `src/DigiByte.Web/Pages/SpendingLimits.razor`:
  - Replace the `NumberStyles.Any` parse (line ~286) with `AmountEntry.Parse(text, 8, 21_000_000_000m)`.
  - Treat `Ambiguous` as invalid, with the message for `MoreThanOneMark`: a limit isn't worth a two-button question.
  - Switch the three limit inputs (~87, ~101, ~115) to `type="text" inputmode="decimal"`.
  - Add the error keys to `en.json` and `es.json`: `amount.invalid.notANumber`, `amount.invalid.moreThanOneMark` ("Leave out thousands marks, for example 1234.5" / "No uses separadores de miles, por ejemplo 1234,5"), `amount.invalid.tooManyDecimals` ("Use at most {0} decimal places"), `amount.invalid.notPositive`, `amount.invalid.tooLarge` ("No more than {0} DGB"). Show them with `L.T` (add `@inject LocalizationService L` if missing).
- [X] T012 [P] Write `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs` (new). Locate `src/DigiByte.Web/wwwroot/locales/` by walking up from `AppContext.BaseDirectory` to the folder containing `src/src.sln`. Tests:
  - (a) Every key in `en.json` has a value that is not empty or whitespace.
  - (b) For every key present in both files, the set of `{n}` placeholders is equal.
  - (c) Every literal first argument of `L.T("…"` found by regex in `src/DigiByte.Web/**/*.razor` exists in `en.json`. Skip non-literal arguments.

  Leave the key-parity test for T049. Run it; (a)–(c) should pass today. Fix any real gap found in `en.json`.
- [X] T013 Write first, in `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs`:
  - `LocalizationService.SupportedLocales` keys are exactly `{ "en", "es" }`, in that order, with values "English" and "Español".
  - For every key, `src/DigiByte.Web/wwwroot/locales/{key}.json` exists.
  - Every `*.json` file in that folder has a matching key (no orphan file).
- [X] T014 In `src/DigiByte.Web/Services/LocalizationService.cs`, reduce `SupportedLocales` to `en` → "English" and `es` → "Español" (research R1), and expose it as `IReadOnlyDictionary<string,string>`. Remove the other eight entries. Make T013 pass.
- [X] T015 Run `dotnet test tests/DigiByte.Wallet.Tests` and check that all Phase 2 tests pass.

**Checkpoint**: Payment links, QR codes, navigation and storage are culture-safe; typed amounts have one tested parser; only English and Spanish are offered. The culture can now follow the wallet language.

---

## Phase 3: User Story 1 — Choose Spanish in Settings (Priority: P1) 🎯 MVP

**Goal**: The Language row opens a picker; choosing Español switches the wallet at once without a reload, keeps the choice across visits, and a Spanish browser opens in Spanish on a first visit.

**Independent test**: [quickstart.md](quickstart.md) §2 and §3, plus §6 rows 1, 2, 4, 5 and 6 (offline, private window, keyboard, screen reader, slow network).

### Tests for User Story 1 (write first)

- [X] T016 [US1] Write `tests/DigiByte.Wallet.Tests/LocalizationServiceTests.cs` (new) with fake `HttpClient` (a `HttpMessageHandler` returning JSON for `locales/en.json` and `locales/es.json`, or 404 when a test says the file is missing) and fake `IJSRuntime` (an in-memory `localStorage` that can be set to throw, and a `getPreferredLanguage` result). Cover every case in [localization-service.md § Required tests](contracts/localization-service.md#required-tests-testsdigibytewallettestslocalizationservicetestscs):
  - Saved `es` with the file available → active `es`, culture `es`, no storage write.
  - Saved `xx` and browser `es-MX` → active `es`, nothing saved.
  - No saved choice and browser `de-DE` → active `en`.
  - Browser JS throws → active `en`, no exception.
  - `SetLocaleAsync("es")` with the file missing → returns `false`; locale, culture and storage unchanged; no event.
  - `SetLocaleAsync("es")` succeeds → returns `true`, storage written, event fires once.
  - `SetLocaleAsync("fr")` → `false`.

  Also check that after `InitializeAsync`, `CultureInfo.CurrentCulture.Name` is `en-US` or `es`. Save and restore the thread cultures in each test.

  For FR-011, also assert after a successful `SetLocaleAsync("es")` that the fake storage was written only under `dgb-locale`, and that the fake HTTP handler saw no request other than `locales/*.json`.

### Implementation for User Story 1

- [X] T017 [P] [US1] Add `window.getPreferredLanguage = () => { try { return navigator.language || ""; } catch { return ""; } };` (or the module/export style `dom-helpers.js` already uses) to `src/DigiByte.Web/wwwroot/js/dom-helpers.js`. Confirm the script is loaded from `src/DigiByte.Web/wwwroot/index.html`.
- [X] T018 [US1] Change `src/DigiByte.Web/Services/LocalizationService.cs` to meet [contracts/localization-service.md](contracts/localization-service.md):
  - Add `public CultureInfo CurrentCulture`, mapping `en` → `en-US` and `es` → `es`.
  - Add a private `ApplyCulture()` that sets `CultureInfo.DefaultThreadCurrentCulture`, `DefaultThreadCurrentUICulture`, `CultureInfo.CurrentCulture` and `CurrentUICulture`.
  - `LoadTranslationsAsync` returns `bool`.
  - `SetLocaleAsync` returns `Task<bool>`. It loads first, and only on success sets the locale, applies the culture, writes `localStorage` (inside try/catch) and raises `OnLocaleChanged`.
  - `InitializeAsync`: read the saved code inside try/catch. If it is offered and loads, use it. Otherwise call `getPreferredLanguage` inside try/catch, take the part before `-`, lower-cased; if offered and it loads, use it **without saving**. Otherwise use `en`. Always end by applying the culture.
  - `T(key, args)` formats with `CurrentCulture`.

  Make T016 pass.
- [X] T019 [US1] Update callers of `SetLocaleAsync` and `InitializeAsync` for the new signatures: `src/DigiByte.Web/Program.cs` (~line 368), and any other result of `grep -rn "SetLocaleAsync\|InitializeAsync()" src/DigiByte.Web` that is the localization service.
- [X] T020 [US1] In `src/DigiByte.Web/Layout/MainLayout.razor`, `@inject LocalizationService L` if missing. Subscribe to `L.OnLocaleChanged` with a handler that calls `InvokeAsync(StateHasChanged)`, and unsubscribe in the existing dispose method (add `@implements IDisposable` only if the layout has no dispose yet; if it has `IAsyncDisposable`, unsubscribe there). Wrap `@Body` (line ~27) in `<CascadingValue Name="Locale" Value="L.CurrentLocale">…</CascadingValue>`.
- [X] T021 [P] [US1] Add keys to `en.json` and `es.json`:
  - `language.title` ("Language" / "Idioma")
  - `language.loadFailed` ("Couldn't load {0}. Try again when you're online." / "No se pudo cargar {0}. Inténtalo de nuevo cuando tengas conexión.")
  - `language.close` ("Close" / "Cerrar")
  - `settings.language` ("Language" / "Idioma")
  - `settings.language.hint` (short subtitle, e.g. "Text and number style" / "Texto y formato de números")
- [X] T022 [US1] Create `src/DigiByte.Web/Components/Shared/LanguagePickerSheet.razor`, modelled on `src/DigiByte.Web/Components/Shared/CurrencyPickerSheet.razor` (same sheet markup, backdrop, animation and dark-mode classes), per [contracts/language-picker-ui.md](contracts/language-picker-ui.md):
  - **Parameters**: `[Parameter] bool IsOpen`, `[Parameter] EventCallback OnClosed`.
  - **List**: `LocalizationService.SupportedLocales` in insertion order, each shown by its native name. A check mark and `aria-checked="true"` on `L.CurrentLocale`. The container has `role="radiogroup" aria-labelledby` the title; each item is a `role="radio"` button.
  - **Keyboard**: arrow up/down moves focus, Enter/Space picks, Escape closes.
  - **Picking**: picking the active item closes. Otherwise `await L.SetLocaleAsync(code)`; `true` → `OnClosed`; `false` → show `L.T("language.loadFailed", SupportedLocales[code])` inside the sheet and stay open. Disable the items while loading.
  - **Layout**: works at 375 px with no horizontal scroll.
- [X] T023 [US1] In `src/DigiByte.Web/Pages/Settings.razor`:
  - Delete the "coming soon" dialog (`@if (showLanguageToast) { … }`, lines ~21–35), the `showLanguageToast` field (~782) and `ShowLanguageComingSoon()` (~1013).
  - In the Language row (~403–422), show `L.T("settings.language")` as the label and `LocalizationService.SupportedLocales[L.CurrentLocale]` as the subtitle and button text (replacing both fixed "English" strings). Tapping opens the sheet (`showLanguagePicker = true`).
  - Render `<LanguagePickerSheet IsOpen="showLanguagePicker" OnClosed="CloseLanguagePicker" />` next to `CurrencyPickerSheet` (~400). `CloseLanguagePicker` sets the flag false and returns focus to the row button through an `ElementReference` and `FocusAsync()`.
  - Add the `Locale` cascading parameter.
- [ ] T024 [US1] Run `dotnet test tests/DigiByte.Wallet.Tests`, then follow [quickstart.md](quickstart.md) §2, §3 and §6 (rows 1, 2, 4, 5 and 6) in the running app (`.claude/launch.json` dev configuration). Confirm that Settings' Language row and the already-translated screens (CreateWallet, Unlock, DigiDollar) switch with no reload. Search the source for `Multi-language support is coming` and expect no match (SC-005).

**Checkpoint**: Spanish can be chosen and is remembered. Already-translated text switches live. Most screens are still English until US2.

---

## Phase 4: User Story 2 — Settings and core screens read in Spanish (Priority: P2)

**Goal**: With Spanish active, Settings, Welcome, create/backup/recover, lock/unlock, Home, Receive, Send and History show no fixed English. Amounts are shown in Spanish style and typed with either mark, and "1.234" asks which was meant.

**Independent test**: [quickstart.md](quickstart.md) §4 and §5, and the source-scan test T048.

### Implementation for User Story 2: amounts

The parser (`AmountEntry`) and its tests are built in Phase 2 (T009, T010).

- [X] T025 [P] [US2] Add the question keys to `en.json`/`es.json` (the `amount.invalid.*` keys come from T011):
  - `amount.ambiguous.question` ("Did you mean:" / "¿Quisiste decir:")
  - `amount.ambiguous.choice` ("{0} DGB")

  Add rows for `amount.ambiguous.*` to `translation-review.md`.
- [X] T026 [US2] Create `src/DigiByte.Web/Components/Shared/AmountAmbiguityChoice.razor`.
  - **Parameters**: `decimal AsDecimal`, `decimal AsThousands`, `string Unit` (default "DGB"), `EventCallback<decimal> OnPicked`.
  - **Display**: `L.T("amount.ambiguous.question")`, then two buttons showing each value formatted with `ToString("#,##0.########", CultureInfo.CurrentCulture)` + unit. The larger reading comes first.
  - **Accessibility**: `role="group"`, buttons reachable by keyboard.
  - **Live switching**: add the `Locale` cascading parameter.
- [X] T027 [US2] In `src/DigiByte.Web/Pages/Send.razor`, change the single amount field (line ~315) from `type="number" @bind="amount"` to `type="text" inputmode="decimal" autocomplete="off"`, with `value="@amountText"` and `@oninput` → `OnAmountInput`:
  - **On each input**: store `amountText`, clear any stored pick (`amountPick = null`), and run `AmountEntry.Parse(amountText, 8, 21_000_000_000m)`.
    - `Value` → set `amount`.
    - `Ambiguous` → set `amount = null`, show `<AmountAmbiguityChoice>`; `OnPicked` sets `amountPick = (amountText, picked)` and `amount = picked`.
    - `Invalid` → set `amount = null` and show `<InlineError>` with the matching `amount.invalid.*` key.
  - **Gating**: Review/Continue stays disabled while `amount` is null.
  - **Prefill**: when `amount` is filled from a query, scan or paste (lines ~861, ~980), set `amountText = amount.Value.ToString("0.########", CultureInfo.CurrentCulture)` **and** `amountPick = (amountText, amount.Value)`. The value from the link is then already settled: no question appears until the person edits the text, and editing clears the pick as usual. Payment-link and QR amounts never trigger the question (FR-013a).
  - **Existing callers**: keep `UpdateAmountWarning` working; call it after `amount` changes.
- [X] T028 [US2] In `src/DigiByte.Web/Pages/Send.razor`, apply the same pattern to batch amounts (input ~153, `OnBatchAmountChanged` ~766). Add `AmountText` and `Pick` properties to the batch recipient model next to `Amount`, and show `<AmountAmbiguityChoice>` / `<InlineError>` per row. Batch review is disabled while any row is ambiguous or invalid. Remove the `NumberStyles.Any` parse. Apply the same prefill rule as T027 to rows filled from a scan or paste.
- [X] T029 [US2] In `src/DigiByte.Web/Pages/Receive.razor` (`OnRequestAmountInput`, ~314–335), replace the `Replace(',', '.')` + `TryParse` logic with `AmountEntry.Parse(requestAmountText, 8, 21_000_000_000m)`. `Ambiguous` shows `<AmountAmbiguityChoice>` and `requestAmount` stays null until picked. Map `Invalid` errors to `amount.invalid.*` keys, replacing the two English messages. Confirm the input is `type="text" inputmode="decimal"`.
- [X] T030 [US2] Make the send confirmation step in `src/DigiByte.Web/Pages/Send.razor` show amounts and fees with `CultureInfo.CurrentCulture` (`ToString("N8")` or the page's existing format helper). This is the person's last check of the picked value (FR-013).

### Implementation for User Story 2: translating screens

Each task applies the conventions above to the files listed. Each moves every visible English string into keys, adds the `Locale` cascading parameter, and adds review rows for warnings.

- [X] T031 [US2] Translate `src/DigiByte.Web/Pages/Settings.razor` with key prefix `settings.*`:
  - Page title.
  - Section labels Wallet / Security / Display / App / Network & advanced.
  - Every row label, subtitle and button.
  - The install dialog (~49+).
  - All confirm dialogs, toasts and error strings in `@code`.

  Reuse the 22 keys missing from `es.json` that belong here (`settings.display_mode`, `settings.currency`, `settings.network`, `settings.backup`) and give them Spanish values.
- [X] T032 [P] [US2] Translate `src/DigiByte.Web/Components/Wallet/WalletProtectionCard.razor` and `src/DigiByte.Web/Components/BiometricNotice.razor` (both shown in Settings and Unlock), with `settings.protection.*` and `biometric.*` keys.
- [X] T033 [P] [US2] Translate `src/DigiByte.Web/Pages/Welcome.razor` (`welcome.*`; reuse the existing `welcome.create` / `welcome.recover`).
- [X] T034 [P] [US2] Translate the remaining English in `src/DigiByte.Web/Pages/CreateWallet.razor` (`create.*`). Fill the missing Spanish for `create.phrase_title`, `create.phrase_warning`, `create.written`, `create.confirm_title`, `create.set_pin` and `create.pin_hint`. All phrase and PIN warnings go to `translation-review.md`.
- [X] T035 [P] [US2] Translate `src/DigiByte.Web/Pages/BackupSeed.razor` (`backup.*`; every warning goes to `translation-review.md`).
- [X] T036 [P] [US2] Translate `src/DigiByte.Web/Pages/RecoverWallet.razor` (`recover.*`), including error messages for an invalid phrase or wrong word count.
- [X] T037 [P] [US2] Translate the remaining English in `src/DigiByte.Web/Pages/Unlock.razor` and `src/DigiByte.Web/Components/Shared/UnlockWalletSheet.razor` (`unlock.*`). Also translate `src/DigiByte.Web/Components/BiometricEnrollPrompt.razor` (`biometric.*`) and `src/DigiByte.Web/Components/Shared/PinInput.razor` / `CredentialInput.razor` labels if they show text.
- [X] T038 [US2] Translate `src/DigiByte.Web/Pages/Home.razor` (`home.*`): balance labels, "Pending", the price-change `aria-label` (with `{0}` for the percent and separate keys for up/down), empty states, shortcuts and toasts. Amount displays keep their `ToString("N…")` calls, which now follow the wallet language.
- [X] T039 [P] [US2] Translate Home's sub-components: `src/DigiByte.Web/Components/Wallet/WalletInfoRow.razor`, `src/DigiByte.Web/Components/Shared/WalletPickerSheet.razor`, `src/DigiByte.Web/Components/Wallet/ReceiveEmptyState.razor` and `src/DigiByte.Web/Components/Wallet/SearchFurtherOffer.razor` (`home.*`/`wallet.*`). Add the `Locale` parameter to `src/DigiByte.Web/Components/DigiDollar/DigiDollarHomeCard.razor`, which is already translated.
- [X] T040 [US2] Translate `src/DigiByte.Web/Pages/Receive.razor` (`receive.*`): the address card, request-amount panel (lines ~92–128 and others), note field, copy/share/download buttons, toasts, and address-type and warning text. Warnings go to `translation-review.md`.
- [X] T041 [US2] Translate `src/DigiByte.Web/Pages/Send.razor` (`send.*`): recipient and amount labels, placeholders (`"What's this for?"`, `"Custom sat/vB"`), fee options, batch mode, review/confirm screens, every validation and warning message in `@code`, and success/failure text. Confirmation and address warnings go to `translation-review.md`.
- [X] T042 [P] [US2] Translate Send's sub-components: `src/DigiByte.Web/Components/Shared/SendUnavailableNotice.razor`, `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`, `src/DigiByte.Web/Components/Shared/SlideToConfirm.razor` and `src/DigiByte.Web/Components/Shared/TxSuccessOverlay.razor` (`send.*`/`scan.*`).
- [X] T043 [US2] Translate `src/DigiByte.Web/Pages/Transactions.razor` (`history.*`): title, filters, empty state, and date-group headings, formatted with `CultureInfo.CurrentCulture` where they are built in code.
- [X] T044 [P] [US2] Translate `src/DigiByte.Web/Components/Shared/TransactionRow.razor`, `src/DigiByte.Web/Components/Shared/TransactionDetailModal.razor` and `src/DigiByte.Web/Components/Shared/ConfirmationBadge.razor` (`history.*`), including "Sent", "Received", "Pending" and the confirmations text with plural keys.
- [X] T045 [P] [US2] Translate the layout chrome the in-scope screens sit in: `src/DigiByte.Web/Layout/NavMenu.razor`, the labels in `src/DigiByte.Web/Layout/MoreMenu.cs` and `NavIcons.cs` that are shown to people, `src/DigiByte.Web/Components/Shared/PageHeader.razor`, `OfflineBanner.razor`, `NodeDegradedBanner.razor` and `ToastContainer.razor` (`nav.*`, `banner.*`; reuse the existing `nav.*` keys). Layout components re-render through `MainLayout`; components inside `@Body` need the `Locale` parameter.
- [X] T046 [US2] Fill every key in `en.json` that is still missing from `src/DigiByte.Web/wwwroot/locales/es.json`, including any of the original 22 not covered above (`contacts.*`, `payments.*`, `identity.description`, `identity.scan`, `remittance.*`). Follow the style in research R11.
- [ ] T047 [US2] Review the Spanish screens at 375 px width, including the FR-009a screens (T058) (quickstart §6 row 3). Where a Spanish label overflows, let it wrap or shorten the Spanish text; never shrink touch targets.
- [X] T048 [US2] Add a source-scan test to `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs` for SC-002. For each in-scope file from T031–T045:
  - Strip `@code { … }` blocks, Razor `@…` expressions, comments, `<svg>…</svg>` and `<path …>`.
  - Fail on any remaining text node or `placeholder=`/`title=`/`aria-label=` value that holds two or more consecutive letters [A-Za-z] and isn't on an allow-list (`DigiByte`, `DGB`, `DigiDollar`, `Digi-ID`, `BIP21`, `sat/vB`, `QR`, `PIN`, `ID`).

  Fix any finding in the matching screen.
- [X] T049 [US2] Add the key-parity test to `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs`: the key set of `es.json` equals that of `en.json` (SC-003, FR-008). On failure, list the missing and extra keys. Make it pass.
- [ ] T050 [US2] Run `dotnet test tests/DigiByte.Wallet.Tests`, then [quickstart.md](quickstart.md) §4 and §5 in the running app with Spanish active, including the `1.234` question in English too.

**Checkpoint**: A Spanish speaker can create, back up, unlock, receive, send and review history fully in Spanish, and amounts can't be misread.

---

## Phase 5: User Story 3 — Only offer languages that are ready (Priority: P3)

**Goal**: The picker lists only languages that ship a translation; a saved language that is no longer offered falls back cleanly. The list itself is cut to two in Phase 2 (T013, T014); this phase checks the fallback and the picker.

**Independent test**: [quickstart.md](quickstart.md) §2 step 2 (exactly two entries), plus T013 and T051.

- [X] T051 [US3] In `tests/DigiByte.Wallet.Tests/LocalizationServiceTests.cs`, add: saved `"fr"` and browser `"en-GB"` → active `en`, and the `SupportedLocales[CurrentLocale]` row value is "English" (US3 acceptance 2).
- [X] T052 [US3] In the running app, open Settings → Language and check the sheet lists exactly English and Español, with no other language anywhere in the app's language UI (US3 acceptance 1).

**Checkpoint**: The picker shows English and Español only.

---

## Phase 7: Scope widened during implementation (2026-11-25)

**Purpose**: Record work merged into the same pull request after the original plan (spec FR-009a, FR-009b; constitution: scope changes go back into the artifacts).

- [X] T058 Translate contacts, Digi-ID, the DigiDollar screens, multisig (wallets, create, import, detail, activity, pending payments, send, the guide and both signing rooms), recovery tools, wallet management, payments, help and the version dialog (FR-009a): every file added to `InScopeFiles` in `tests/DigiByte.Wallet.Tests/LocaleFilesTests.cs`, new warnings added to `translation-review.md`, shared copy button and Digi-ID dates included. Multisig Send keeps its point-only amount rule (spec Assumptions).
- [X] T059 Translate wallet-service messages by reason (FR-009b): `ReceiveAddressException`, the `NodeBanner` state, `UpdateAdvice.ResultOf`, and `LocaleReasonKeys` with `LocaleReasonKeysTests`.
- [ ] T060 Check the FR-009a screens in the browser in Spanish, as T050 does for the first screens (needs an unlocked wallet; multisig screens need a multisig).
- [ ] T061 Decide whether startup should stop waiting for the English fallback file. Measured already (research R16): production gzips the files; a first visit now loads about 53 KB more before the first screen in English and about 110 KB more in Spanish.

---

## Phase 6: Polish & cross-cutting concerns

- [X] T053 [P] Update the docs for the language change (constitution: documentation rule):
  - `docs/ARCHITECTURE.md`: add a "Localization" paragraph. It covers where locale files live, how a language is chosen (saved → browser → English), that number style follows the wallet language, that machine-read values always use invariant formatting, the `Locale` cascading parameter, and how to add a language (file + one `SupportedLocales` line + tests).
  - `docs/ROADMAP.md`: line ~104, change "Multi-language support (10 locales defined, translation files pending)" to done for English and Spanish, with other languages as future work. Update the "Multi-Language" section (~118) to match.
  - `docs/PROCESS_FLOWS.md`: line ~597, replace "Language selector (10 locales) → LocalizationService.SetLocale()" with the real flow: Settings → Language sheet (English, Español) → `SetLocaleAsync`, plus the startup order (saved → browser → English).
- [X] T054 [P] Add a CHANGELOG entry under "Unreleased" in `CHANGELOG.md`:
  - Spanish can be chosen in Settings and is used on a first visit from a Spanish browser.
  - Number style now follows the wallet language.
  - Fixed: payment links written with a comma on Spanish browsers.
  - Fixed: "12,5" typed on Send or in spending limits read as 125.
  - Typed amounts like "1.234" now ask which value was meant.
  - The Spanish security and money warnings were reviewed by a native speaker.
- [ ] T055 Get a native Spanish speaker to approve every row of `specs/028-spanish-language-option/translation-review.md`, filling in approver and date. Apply any wording changes they request to `es.json`. **The release is blocked until every row is filled (SC-006).**
- [ ] T056 Run the whole of [quickstart.md](quickstart.md) (§1–§7) on a production build (`dotnet publish src/DigiByte.Web -c Release` served locally), including offline (§6 row 1) with the service worker active.
- [X] T057 Final pass:
  - `dotnet build src/src.sln` and `dotnet test` across `tests/` pass.
  - `grep -rn "coming in a future update" src/DigiByte.Web` finds nothing about languages.
  - Update `specs/028-spanish-language-option/plan.md` if anything changed from the design during implementation (constitution: scope changes go back into the artifacts).

---

## Dependencies & execution order

### Phases

```text
Phase 1 Setup ─> Phase 2 Foundational ─> Phase 3 US1 (MVP) ─┬─> Phase 4 US2 ─┐
                                                           └─> Phase 5 US3 ─┴─> Phase 6 Polish
```

- **Phase 2 blocks everything after it**: US1's culture switch (T018) must not land before the whole safety phase (T003–T015).
- **US1 depends on Phase 2 only.**
- **US2** depends on US1's `MainLayout` cascade (T020) for live switching, and on Phase 2. Its amount tasks (T025–T030) need only Phase 2, so they can start in parallel with US1.
- **US3** depends on US1 (it checks the picker). The list itself is cut in Phase 2, so US1 passes its own acceptance scenarios without US3.

### Within phases

- **Tests before code**: T003 → T004, T009 → T010 (then T011), T013 → T014, T016 → T018.
- **US1**: T017 and T021 come before T022. T018 comes before T019, T020, T022 and T023.
- **US2**:
  - T010 and T025 before T026.
  - T026 before T027, T028 and T029.
  - Translation tasks T031–T045 before T046, T047, T048 and T049.
  - T027, T028, T030 and T041 all edit `Send.razor`, so do them one after another, in the order T027 → T028 → T030 → T041.
  - T029 and T040 both edit `Receive.razor`; do T029 first.
- **Locale files**: tasks that add keys all edit `en.json`/`es.json`. When running them in parallel, merge the JSON by hand. Each task adds its own prefix block, which keeps conflicts small.

## Parallel examples

**Phase 2**: T003, T005, T006, T007, T012 and T009 touch different files and can run together. T004 follows T003; T010 follows T009; T011 follows T010; T008 follows T005–T007; T014 follows T013; T015 comes last.

**US1**: T017 (JS) and T021 (keys) run alongside T018. T022 and T023 follow.

**US2**, after T026, for example:

```text
T029 Receive amount input        T033 Welcome
T034 CreateWallet                 T035 BackupSeed
T036 RecoverWallet                T037 Unlock + sheets
T039 Home sub-components          T042 Send sub-components
T044 History components           T045 Layout chrome
```

The `Send.razor` chain (T027 → T028 → T030 → T041) and the `Home.razor` / `Transactions.razor` tasks (T038, T043) run on their own tracks.

## Implementation strategy

1. **Ship the safety fixes first (Phase 2).** They fix live bugs for Spanish-browser users and can go out as their own `fix(wallet):` pull request.
2. **MVP = US1 (+ US3's checks).** Spanish can be picked, is remembered, and a Spanish browser opens in Spanish. Most screens are still English until US2, so either don't announce it yet, or ship US2 in the same release.
3. **US2 screen by screen.** Settings first (the screen people switch from), then create/backup/unlock (safety), then Home, Receive, Send and History. Each screen can be merged on its own; the T048 scan grows with it.
4. **Release gate**: T055, native-speaker approval of the warnings, must be complete before the release that announces Spanish.

## Summary

| Phase | Tasks | Count |
|-------|-------|-------|
| Setup | T001–T002 | 2 |
| Foundational | T003–T015 | 13 |
| US1 (P1) | T016–T024 | 9 |
| US2 (P2) | T025–T050 | 26 |
| US3 (P3) | T051–T052 | 2 |
| Polish | T053–T057 | 5 |
| **Total** | | **57** |

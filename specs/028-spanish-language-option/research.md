# Research: Spanish in the Language Setting

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-11-24

What the code does today, and the decisions that follow. Each decision lists what was chosen, why, and what was turned down.

## Findings from the code

| # | Finding | Where |
|---|---------|-------|
| F1 | The Language row is fixed text ("English") and opens a "coming soon" dialog. No code calls `SetLocaleAsync`. | `src/DigiByte.Web/Pages/Settings.razor` (row ~403–422, dialog ~21–35, `ShowLanguageComingSoon` ~1013) |
| F2 | `LocalizationService` already loads `locales/{code}.json`, falls back to English per key, saves the choice in `localStorage["dgb-locale"]` and raises `OnLocaleChanged`. | `src/DigiByte.Web/Services/LocalizationService.cs` |
| F3 | `SupportedLocales` lists 10 languages; only `en.json` (430 keys) and `es.json` (408 keys) exist. | same file, `wwwroot/locales/` |
| F4 | Only two components subscribe to `OnLocaleChanged`. Every other page keeps its old text until it is re-created, so switching today would not update an open screen. | `Components/Shared/DigiIdAddressWarning.razor`, `DigiIdRecoverFunds.razor` |
| F5 | In-scope screens are mostly fixed English text: Settings 5 `L.T` calls in 1,212 lines; Home 2 / 1,403; Receive 0 / 760; Send 0 / 1,260; Transactions 1 / 572; Welcome 0; RecoverWallet 0. CreateWallet (12) and Unlock (18) are partly done. | `src/DigiByte.Web/Pages/*.razor` |
| F6 | Displayed amounts use `ToString("N8")` etc. without a culture, so they follow `CultureInfo.CurrentCulture`, which Blazor WebAssembly takes from the browser. A Spanish browser already shows `1.234,5` today; a German one shows German style. | `Pages/Home.razor` 165–185, 1132–1136 and others |
| F7 | Some machine-read values are also formatted with the current culture. `PaymentRequest.ToUri()` writes `amount={AmountDgb:F8}`, so a Spanish browser today produces `amount=1,50000000`, which other wallets cannot read. `TransactionDetailModal` builds `/send?...&amount={amount}` the same way. **This is a live bug, independent of this feature.** | `src/DigiByte.Wallet/Models/PaymentRequest.cs:21`, `Components/Shared/TransactionDetailModal.razor:255` |
| F8 | The Send amount is `<input type="number">` parsed with `NumberStyles.Any` + invariant culture: `"12,5"` becomes **125**. Batch amounts the same. Receive replaces `,` with `.`, so `"1,234"` silently becomes 1.234. | `Pages/Send.razor` 153, 315, 768; `Pages/Receive.razor` 314–335 |
| F9 | `DigiDollarFormat` already formats and parses with an explicit culture and guards bad grouping; DigiDollar components take the culture from `L.CurrentLocale`. | `src/DigiByte.Wallet/Services/DigiDollarFormat.cs` |
| F10 | Every page renders inside `MainLayout` (`@Body` at line 27). | `src/DigiByte.Web/Layout/MainLayout.razor` |
| F11 | The service worker caches every `.json` for offline use, so `es.json` is available offline after the first visit. | `wwwroot/service-worker.published.js:19` |
| F12 | `DigiByte.Wallet.Tests` references both `DigiByte.Wallet` and `DigiByte.Web`, so web services and the locale files can be tested there. | `tests/DigiByte.Wallet.Tests/DigiByte.Wallet.Tests.csproj` |

## Decisions

### R1. Offered languages

- **Decision**: Reduce `SupportedLocales` to the languages that ship a file: `en` → "English", `es` → "Español". The picker lists exactly that dictionary. Adding a language later means adding its file and one dictionary line.
- **Rationale**: FR-003. The other eight entries are dead configuration that would leak into the picker.
- **Alternatives**: Probe for each file at startup (slow, network-dependent, adds a failure mode); keep 10 entries and filter with a second list (two sources of truth).

### R2. Picking a language

- **Decision**: New `Components/Shared/LanguagePickerSheet.razor`, built like `CurrencyPickerSheet` (bottom sheet, radio-style list, current item marked, closes on pick or dismiss). The Settings row shows `SupportedLocales[L.CurrentLocale]` and opens the sheet. The "coming soon" dialog and `showLanguageToast` / `ShowLanguageComingSoon` are deleted.
- **Rationale**: FR-001, FR-002, FR-005, FR-012; reuses a pattern people already know from the currency setting.
- **Alternatives**: A native `<select>` (inconsistent with the rest of Settings, poor styling on iOS).

### R3. Updating open screens without a reload

- **Decision**: `MainLayout` subscribes to `OnLocaleChanged`, and wraps `@Body` in `<CascadingValue Name="Locale" Value="L.CurrentLocale">`. Each in-scope page and component declares `[CascadingParameter(Name = "Locale")] public string? Locale { get; set; }`. When the value changes, Blazor re-renders exactly those components, and their state (typed text, scroll, open sheets) is kept.
- **Rationale**: FR-004. Uses Blazor's own change notification; no disposal hooks are needed per page, which matters because several pages already implement `IAsyncDisposable`.
- **Alternatives**: A shared base class subscribing to the event (clashes with pages' existing `@implements IAsyncDisposable`, and Blazor calls only one dispose method); re-keying `@Body` (re-creates the page and loses what the person typed); a full page reload (forbidden by FR-004).

### R4. Number style follows the wallet language

- **Decision**: On startup and on every switch, `LocalizationService` sets `CultureInfo.DefaultThreadCurrentCulture`, `DefaultThreadCurrentUICulture`, `CurrentCulture` and `CurrentUICulture` to the wallet language's culture (`en` → `en-US`, `es` → `es`). Every amount shown with `ToString("N…")` then follows the chosen language, on every screen.
- **Rationale**: FR-013 display, with one switch instead of editing every `ToString` call. It also removes today's split where the browser decides number style and the wallet decides text.
- **Behaviour change to note**: someone with a German or French browser who keeps the wallet in English will now see English number style. This matches "amounts follow the chosen language" and is mentioned in the CHANGELOG.
- **Alternatives**: Pass an explicit culture into every in-scope `ToString` (dozens of call sites, easy to miss one, and out-of-scope screens would keep the browser's style, giving two styles in one app).

### R5. Machine-read values never use the display culture (prerequisite)

- **Decision**: Before R4 lands, every value written for a machine uses `CultureInfo.InvariantCulture`: BIP21 URIs (`PaymentRequest.ToUri`), internal navigation query strings (`TransactionDetailModal`), QR content, clipboard copies of amounts meant for pasting, CSV/JSON export, storage, and API payloads. Audit with these searches over `src/DigiByte.Web` and `src/DigiByte.Wallet`: interpolations of `decimal`/`double` into strings containing `amount=`, `?`, `&` or `:`; `ToString()` / `ToString("F…")` / `ToString("N…")` on values passed to `NavigateTo`, `clipboard`, `JsonSerializer`, or `localStorage`; `decimal.Parse`/`TryParse` without a culture. Each finding is fixed and covered by a test that runs under `es` culture.
- **Rationale**: FR-013 ("payment requests and QR codes are read by the payment standard's format"), Principle V (must not break receive/send), and F7 is already broken for Spanish browsers.
- **Alternatives**: Keep the current culture invariant and format only displays (see R4, rejected).

### R6. Typing amounts

- **Decision**: New `AmountEntry` in `src/DigiByte.Wallet/Services/AmountEntry.cs` (pure, culture-independent) with `Parse(string text, int maxDecimals)` returning one of:
  - `Empty`
  - `Value(decimal)`
  - `Ambiguous(decimal asDecimal, decimal asThousands)`
  - `Invalid(reason)`: `NotANumber`, `MoreThanOneMark`, `TooManyDecimals`, `NotPositive`, `TooLarge`

  Rules, in order:
  1. Trim; strip spaces.
  2. Only digits plus `,`/`.` are allowed.
  3. More than one mark → `MoreThanOneMark`.
  4. No mark → integer.
  5. One mark with exactly three digits after it, and a whole part of 1–3 digits that does not start with `0` → `Ambiguous` (for example `1.234` → 1.234 or 1234).
  6. Otherwise the mark is the decimal mark.

  DGB uses `maxDecimals = 8` and the 21-billion cap already used in Receive.
- **Spec refinement**: Rule 5 narrows FR-013a. `0.123` and `1234.567` can't be a thousands mark, so they don't trigger the question. FR-013a in the spec is updated to say so.
- **UI**: Send (single and batch amounts) and the Receive request amount switch from `type="number"` to `type="text" inputmode="decimal"`, because a number input drops or rewrites commas depending on the browser. When the result is `Ambiguous`, an inline choice under the field shows both readings in the chosen style ("1.234 DGB" / "1,234 DGB") as two buttons. Review/Continue stays disabled until one is picked. The pick is remembered for that exact text (editing the text clears it), and the text itself is not rewritten (see data-model.md). Payment-request and QR amounts keep their own invariant parsing and never reach `AmountEntry`.
- **Rationale**: FR-013, FR-013a, SC-007; fixes F8. A pure function in `DigiByte.Wallet` falls under Principle III and is easy to test.
- **Alternatives**: Use the chosen culture's parser (`decimal.TryParse` with `es`): reads `1.234` as 1234 in Spanish but 1.234 in English, so the same keystrokes mean different amounts depending on a setting (rejected in clarification). Reuse `DigiDollarFormat.TryParseUsdToCents`: it is culture-bound by design and limited to 2 decimals.
- **Spending limits**: `Pages/SpendingLimits.razor` has the same "12,5 → 125" bug, so its three limit fields also use `AmountEntry`. There an `Ambiguous` result is treated as invalid (asking "which did you mean" isn't worth it for a limit).
- **Out of scope**: The DigiDollar send form and multisig send keep their current parsing. They are not FR-009 screens, and DigiDollar already parses by locale with grouping checks.

### R7. First-visit browser language

- **Decision**: Add `getPreferredLanguage()` to `wwwroot/js/dom-helpers.js` (returns `navigator.language`, or `""` if unavailable). In `InitializeAsync`, when `localStorage["dgb-locale"]` is missing or not offered, take the primary subtag of that value (`es-MX` → `es`). If it is offered, apply it **without saving**; otherwise use English. Only the browser's first preference is used.
- **Rationale**: FR-006, FR-006a; not saving means a later change of browser language is still honoured until the person picks one.
- **Alternatives**: Walk the whole `navigator.languages` list (a German-first browser that also lists Spanish would open in Spanish, which is surprising); save the detected value (would freeze the guess as a choice the person never made).

### R8. When a translation file cannot load

- **Decision**: `SetLocaleAsync` loads the file first and only switches (state, culture, storage, event) if it loaded. It returns `false` otherwise, and the picker shows "Couldn't load Español. Try again when you're online." and stays open. At startup, a saved `es` whose file can't load shows English for that visit but keeps the saved choice.
- **Rationale**: Edge case "translation fails to load"; never shows a Language row that disagrees with the screen.
- **Alternatives**: Switch anyway and fall back per key (today's behaviour; the row says Español over an English screen).

### R9. Translation keys and checks

- **Decision**: Keys follow the existing dotted style, one prefix per screen (`settings.*`, `home.*`, `receive.*`, `send.*`, `history.*`, `create.*`, `backup.*`, `unlock.*`, `welcome.*`, `recover.*`, `amount.*`, `language.*`). Formatted values use `{0}` placeholders, never string concatenation, so word order can change in Spanish. New tests in `DigiByte.Wallet.Tests`:
  - `es.json` has exactly the keys of `en.json` (SC-003).
  - No value is empty.
  - Each key has the same `{n}` placeholders in both files.
  - Every literal key passed to `L.T("…")` in `src/DigiByte.Web/**/*.razor` exists in `en.json` (FR-007, so a raw key is never shown).
  - The in-scope pages contain no visible English text outside `L.T` (a source scan with an allow-list for product names; a guard for SC-002, backed by the manual walk-through in quickstart).
- **Rationale**: Keeps the files in step automatically; reviewers get a failing test instead of a missing string in production.

### R10. Approval of security and money warnings

- **Decision**: `specs/028-spanish-language-option/translation-review.md` lists every Spanish key under `create.*` (phrase warnings, PIN), `backup.*`, `send.confirm*`, `send.warning*`, `receive.warning*`, `unlock.*` warnings, and the existing `identity.addrWarning.*`, with English, Spanish, approver and date. The release PR links it, and the CHANGELOG entry says the warnings were reviewed by a native speaker.
- **Rationale**: FR-010, SC-006 ("recorded with the release").

### R11. Style of the Spanish text

- **Decision**: Follow `es.json` as it stands: neutral Spanish, informal "tú", "billetera" for wallet ("Ingresa" for entering text), "dirección", "saldo", "frase de recuperación". Product and unit names (DigiByte, DGB, DigiDollar, Digi-ID) are never translated.
- **Rationale**: Spec assumption; consistency with the 408 existing strings.

## Open items

None. All Technical Context unknowns are resolved above.

## R5 audit result (T008, 2026-11-24)

Searched `src/DigiByte.Web` and `src/DigiByte.Wallet` with the R5 patterns.

**Changed**

| Where | Problem | Fix |
|-------|---------|-----|
| `DigiByte.Wallet/Models/PaymentRequest.cs` `ToUri` | `amount={AmountDgb:F8}` used the current culture (`1,50000000` on a Spanish browser). | Invariant `F8`. Tests: `PaymentRequestTests`. |
| `DigiByte.Wallet/Models/PaymentRequest.cs` `FromUri` | `NumberStyles.Any` read `amount=1,5` as 15. | `AllowDecimalPoint` + invariant; a comma leaves the amount empty. |
| `DigiByte.Web/Pages/Send.razor` `?amount=` query | Same `NumberStyles.Any` problem. | `AllowDecimalPoint` + invariant. |
| `DigiByte.Web/Services/DonationInfo.cs` `Bip21Uri` | Integer interpolated with the current culture (safe today, fragile). | Invariant `ToString`. Test: `DonationInfoTests.Bip21Uri_IsTheSame_UnderSpanish`. |

**Checked, already correct**

- `Components/Shared/TransactionDetailModal.razor` "Send again" link: the amount is formatted with `InvariantCulture` before the link is built.
- `Pages/MultisigSend.razor` query amount and "pay from personal wallet" link: `AllowDecimalPoint` + invariant both ways.
- `Services/ScanRouter.cs`: invariant amount.
- All other `NavigateTo($"…")`, invite links and API URLs interpolate only IDs, codes or integers (`int`/`long`), which have no decimal or thousands mark in any culture.
- No amount is passed to clipboard, share, download or `localStorage` as preformatted text; stored data goes through `System.Text.Json`, which is culture-independent.
- `decimal`/`double` parsing: every remaining call passes a culture (`DigiDollarFormat` passes the chosen culture on purpose).

**Display-only, left for the screen tasks**

`ToString("0.########")` without a culture in `Receive.razor:68`, `TransactionRow.razor:108` and `DigiIdRecoverFunds.razor:698`, and `MultisigFormat.Dgb` (invariant on purpose, multisig screens are out of scope). These are screen text, not machine output; the first two are reviewed in T040 and T044.

## Decisions made during implementation (2026-11-24)

### R12. Load one ICU subset that has both languages

- **Found**: with Spanish saved, the wallet failed to start: Blazor refuses a culture other than the browser's at startup unless all globalization data is loaded, because by default each browser gets only its own ICU subset (EFIGS 220 KB, CJK 360 KB, or no_CJK 348 KB gzipped).
- **Decision**: `<BlazorIcuDataFileName>icudt_EFIGS.dat</BlazorIcuDataFileName>` in `DigiByte.Web.csproj`. Since number style now follows the wallet language (English or Spanish), only that subset is ever needed. Every browser gets the same 220 KB file; Chinese, Japanese and Korean browsers get a smaller one than before, and still format Spanish correctly.
- **Alternatives**: `BlazorWebAssemblyLoadAllGlobalizationData` (about 400 KB more on every first load); setting the culture only after startup (avoids the check, but a CJK browser would not have Spanish number data).
- **Consequence**: a future language outside English, French, Italian, German and Spanish needs this setting changed.

### R13. The language value wraps the whole shell, not only the page

R3 put the `CascadingValue` around `@Body`. Navigation, banners and toasts live outside it and would not have re-rendered on a switch, so `MainLayout` now wraps all of its markup in it.

### R14. Tests never set the process-wide culture

The service sets the default thread culture on purpose. In tests that leaked into other test classes running in parallel (a multisig message showed "2,00000000"). `LocalizationService` has a second constructor that takes the culture-applying action; tests pass a recorder (or a no-op), the app uses the two-argument constructor.

**R11 correction (2026-11-25)**: the existing file uses "billetera" 42 times and "cartera" 11 times, so R11 originally named the wrong word. New text uses "billetera" and "Ingresa". Two existing entries still say "Cartera": `app.name` ("Cartera DigiByte") and `nav.wallet` ("Cartera"); they are left for the native-speaker review to settle, listed in `translation-review.md`.

### R15. Gaps closed after implementation (2026-11-25)

- **Page language**: `index.html` hard-coded `<html lang="en">`, so screen readers read Spanish with English pronunciation (FR-012). `LocalizationService` now sets `document.documentElement.lang` at startup and on every successful switch (`domHelpers.setDocumentLanguage`); a failed switch leaves it alone.
- **Raw-HTML translations**: eight keys are rendered as raw HTML to keep a bold word inside a sentence. `LocaleFilesTests` now requires the same `<strong>`/`<b>` tags in both languages for those keys, no other markup or bare `<`, `>`, `&` in them, and no tags in any other key.
- **Date patterns**: the `history.format.*` keys hold .NET date patterns; a test formats a real date with each in both languages.

### R16. First-load size of the translation files (measured 2026-11-25)

- **Measured**: production (`dgbwallet.app`) serves `locales/*.json` gzip-compressed (`Content-Encoding: gzip`, 7,790 bytes for today's `en.json`), with `Cache-Control: no-cache`, and the service worker caches them for offline use.
- **With this feature**: `en.json` is 52.7 KB gzipped (was 7.8 KB) and `es.json` 56.6 KB. `Program.cs` awaits `LocalizationService.InitializeAsync()` before the app starts, and it always loads English as the fallback. So a first visit downloads about 53 KB more before the first screen in English, and about 110 KB more in Spanish. The plan's target (the language file in under 1 s on 3G, quickstart §6 row 6 "under ~60 KB") holds for each file on its own, not for the two together.
- **Open decision (T061)**: accept it (cached after the first visit), or stop blocking startup on the English fallback (load it after the first render, or only when a Spanish key is missing), or split the files per screen. No change made yet.

### R17. Browser check with a throwaway testnet wallet (2026-11-25)

Created, used and deleted a testnet wallet in Spanish at 375 px: create (phrase, word check, PIN with Spanish screen-reader labels), unlock, biometric offer, Home, Receive (request "1.234" asks, the pick shows "1.234 DGB"), Send ("1.234" asks, "12,5" reads as 12.5, "1.234,5" is refused), History, Contacts, multisig list/create/guide, Digi-ID, Help, wallet management, recovery tools, delete with PIN. No horizontal scroll; `lang` was `es`; dates "24 nov 2026".

Gaps found and fixed on `fix/spanish-gaps`: the Getting started card (now scanned), the error page in `App.razor` (now scanned), the static `#blazor-error-ui` bar (relabelled by `dom-helpers.js`), default wallet names saved in English, "¿Quisiste decir:" without its closing mark, Send's "0.00" placeholder and hand-built "$0.00" fiat zero, and Receive opened while locked throwing instead of letting `<AuthGuard>` redirect. Also fixed a development-only reload loop: the dev `service-worker.js` ignored SKIP_WAITING, so an update waiting after a rebuild was applied on every load and the 4-second fallback reloaded the page again.

Not covered (need funds or a real multisig): the send confirmation, the slider, the success screen, pending multisig payments and the signing rooms.

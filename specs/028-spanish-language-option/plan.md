# Implementation Plan: Spanish in the Language Setting

**Branch**: `feature/spanish-language-option` (not created yet; the checkout is on `main`) | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/028-spanish-language-option/spec.md`

## Summary

Spanish can't be picked because the Language row in Settings is fixed text that opens a "coming soon" dialog. The translation machinery behind it (`LocalizationService`, `locales/es.json` with 408 of 430 keys, per-key English fallback, a saved choice) already works.

The plan:

- Connect the row to a new language picker sheet.
- Make open screens re-render on a switch, through a cascading locale value in `MainLayout`.
- Open in Spanish on a first visit from a Spanish browser, without saving that as a choice.
- Make number style follow the wallet language by setting the thread culture.
- Move the in-scope screens' fixed English text into translation keys, and complete `es.json`.

Two safety pieces come with it:

- **Payment links and QR amounts.** These must always be written with a point, whatever the language. Today they follow the browser's number style, which is already broken for Spanish browsers (research F7). Incoming links are also too lenient today: `?amount=1,5` is read as 15. Both are fixed before the culture change lands.
- **Typed amounts.** A new culture-independent parser in `DigiByte.Wallet` accepts a comma or a point and asks when "1.234" could mean two things. It replaces today's parsing on Send and Receive, which reads "12,5" as 125 on Send (F8). Spending limits have the same bug and use the same parser.

Together these fix four bugs that are already live: comma payment links written on Spanish browsers, comma amounts in incoming links read as 15, "12,5" on Send read as 125, and the same on spending limits.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`, SDK pinned by `global.json`)

**Primary Dependencies**: Blazor WebAssembly (`DigiByte.Web`), `System.Globalization` with ICU (already loaded; not invariant mode), Tailwind CSS. No new packages.

**Storage**: Browser `localStorage["dgb-locale"]` (existing key). Translation files are static assets in `wwwroot/locales/`, cached offline by the service worker.

**Testing**: xUnit 2.9 in `tests/DigiByte.Wallet.Tests` (references `DigiByte.Wallet` and `DigiByte.Web`), with fake `HttpClient` and `IJSRuntime` objects. Manual walk-through in [quickstart.md](quickstart.md).

**Target Platform**: Static PWA in modern mobile and desktop browsers.

**Project Type**: Web application, client-only for this feature (no server change).

**Performance Goals**: The switch is visible within one render after the file loads. The `es.json` file (~40 KB) loads in under 1 s on 3G, and instantly from cache.

**Constraints**: No page reload on switch (FR-004). Strict CSP unchanged, with no new origins. Works offline once cached. No server call except the locale file from the wallet's own origin (FR-011). Fits 375 px wide.

**Scale/Scope**:

- **Languages**: 2 (en, es).
- **In-scope screens**: about 11 at first (Settings, Welcome, CreateWallet, BackupSeed, RecoverWallet, Unlock and the unlock sheet, Home, Receive, Send, Transactions, the language sheet) plus their sub-components. Widened during implementation (spec FR-009a) to contacts, Digi-ID, the DigiDollar screens, multisig (wallets, create, import, detail, activity, pending payments, send, the guide and both signing rooms), recovery tools, wallet management, payments, help and the version dialog.
- **Text to move into keys**: estimated 400–600 for the first screens (actual about 640); about 1,940 new entries in all once the scope was widened. `en.json` went from 7.8 KB to 52.7 KB gzipped and is loaded at startup as the fallback, so first-load size needs watching (see research R16).
- **Amount inputs**: 6 (Send single, Send batch, Receive request, and the daily, weekly and monthly spending limits).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle / rule | Applies | Status | Notes |
|---|---|---|---|
| I. Self-custody | Indirectly | ✅ Pass | No key, seed or password is touched. Translated warnings keep their full meaning (FR-010), with native-speaker approval (R10). |
| II. In-browser cryptography | No | ✅ Pass | No crypto or server change. |
| III. Test-first for `DigiByte.Wallet` | **Yes** | ✅ Pass (planned) | `AmountEntry` (new, `DigiByte.Wallet/Services`) and the `PaymentRequest.ToUri` culture fix get tests written first, including negative paths (more than one mark, too many decimals, non-numeric, zero, too large, Spanish-culture URI). |
| IV. Boundary discipline | Yes | ✅ Pass | `AmountEntry` goes in `DigiByte.Wallet` (the lowest layer that owns amounts), next to `DigiDollarFormat`. Localization stays in `DigiByte.Web`. No new reference direction. |
| V. Resilience | Yes | ✅ Pass | The locale file is a static, offline-cached asset. A failed load keeps the current language and shows a message (R8), and never blocks balance, receive, send or history. |
| CSP / self-hosted assets | Yes | ✅ Pass | Only `locales/*.json` from the wallet's own origin. |
| Secret hygiene | No | ✅ Pass | Only a language code is stored. |
| Address handling | No | ✅ Pass | Unchanged. |
| Conventional commits, spec-kit flow, docs | Yes | ✅ Pass (planned) | `feat(wallet): …`/`fix(wallet): …` commits; the CHANGELOG notes the number-style change. `docs/ARCHITECTURE.md` gets a short localization paragraph. |

**Post-design re-check**: ✅ No violations. The design adds one class to `DigiByte.Wallet` (tested), one component and one JS function to `DigiByte.Web`, and changes no project references.

## Project Structure

### Documentation (this feature)

```text
specs/028-spanish-language-option/
├── spec.md
├── plan.md                  # this file
├── research.md              # Phase 0: findings F1–F12, decisions R1–R11
├── data-model.md            # Phase 1
├── quickstart.md            # Phase 1: validation guide
├── contracts/
│   ├── localization-service.md
│   ├── language-picker-ui.md
│   └── amount-entry.md
├── checklists/requirements.md
├── translation-review.md    # created during implementation (R10)
└── tasks.md                 # /speckit-tasks, not created here
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/
├── Services/AmountEntry.cs                 # new: culture-free typed-amount parser (R6)
└── Models/PaymentRequest.cs                # fix: invariant amount in ToUri (R5)

src/DigiByte.Web/
├── Services/LocalizationService.cs         # 2 offered languages, culture, first-visit rule, failed-load handling (R1, R4, R7, R8)
├── Layout/MainLayout.razor                 # subscribe + CascadingValue "Locale" around @Body (R3)
├── Components/Shared/LanguagePickerSheet.razor   # new (R2)
├── Components/Shared/AmountAmbiguityChoice.razor # new: "did you mean" buttons for typed amounts (R6)
├── Components/Shared/TransactionDetailModal.razor # invariant amount in /send link (R5)
├── Components/Shared/UnlockWalletSheet.razor     # translate
├── Pages/Settings.razor                    # row → picker, remove dialog, translate
├── Pages/{Welcome,CreateWallet,BackupSeed,RecoverWallet,Unlock,Home,Receive,Send,Transactions}.razor  # translate; Send/Receive use AmountEntry
├── Pages/SpendingLimits.razor              # AmountEntry for the three limits (fixes "12,5" → 125)
├── Services/AmountEntryText.cs             # added in implementation: translated message for a parse error
├── Services/LocaleReasonKeys.cs            # added with FR-009b: every service reason → translation key
├── (DigiByte.Wallet) Services/ReceiveAddressException.cs, NodeSourceStatus.cs, WalletService.cs  # FR-009b: reasons instead of English text
├── Services/Css.cs                         # added in implementation: invariant numbers for inline CSS (research R5 follow-up)
├── Components/Shared/ConfirmGestureBase.cs # added in implementation: slider screen-reader text translated at the source
├── DigiByte.Web.csproj                     # added in implementation: BlazorIcuDataFileName = icudt_EFIGS.dat (research R12)
├── wwwroot/js/dom-helpers.js               # getPreferredLanguage()
└── wwwroot/locales/{en,es}.json            # new keys; es completed

tests/DigiByte.Wallet.Tests/
├── AmountEntryTests.cs                     # new
├── LocalizationServiceTests.cs             # new
├── LocaleFilesTests.cs                     # new: parity, placeholders, key usage, no fixed English on in-scope pages
└── PaymentRequestTests.cs                  # new or extended: es culture → "amount=1.50000000"
```

**Structure Decision**: Use the existing monorepo layout. The only new production files are `AmountEntry.cs` and `LanguagePickerSheet.razor`. The work splits into four slices, which `/speckit-tasks` should keep in this order:

1. **Safety prerequisites**: R5 machine-format audit and fixes, `AmountEntry` and its tests (also used for spending limits), and cutting the offered list to English and Spanish (R1). These ship value on their own, since they fix live bugs F7 and F8.
2. **Picker and live switch**: US1.
3. **Screen translation**: US2, one screen per task, Settings first.
4. **Offered-language checks**: US3 (the list itself is cut in slice 1).

## Complexity Tracking

No constitution violations to justify.

| Risk | Mitigation |
|---|---|
| Setting the thread culture changes number style on **every** screen, including out-of-scope ones, and for non-English browsers that keep the wallet in English. | Intended (R4); CHANGELOG note. The R5 audit plus Spanish-culture tests guard machine-read output. |
| The R5 audit misses a machine-format site. | Audit by search patterns listed in R5; Spanish-culture tests for URI, navigation and clipboard paths; quickstart §5 payment-link checks. |
| Hundreds of strings moved by hand. | Key usage and parity tests catch missing keys. A source scan flags fixed English left on in-scope pages. Screens are done one task at a time. |
| Native-speaker approval not available at release time. | The release is blocked by `translation-review.md` (SC-006). The rest can merge behind the existing English default. |

# Quickstart: validating Spanish in the Language Setting

Run these once the feature is built, to prove it works end to end. References: [contracts/](contracts/), [data-model.md](data-model.md).

## Prerequisites

- .NET SDK as pinned in `global.json`.
- The wallet runs locally (the `.claude/launch.json` dev configuration, or `dotnet run --project src/DigiByte.Web`).
- A browser where you can clear site data, switch the browser language, and go offline (DevTools → Network → Offline).
- A testnet wallet with a small balance, for the send checks.

## 1. Automated checks

```powershell
dotnet build src/src.sln
```

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~AmountEntry|FullyQualifiedName~LocalizationService|FullyQualifiedName~LocaleFiles|FullyQualifiedName~PaymentRequest"
```

These tests must pass:

| Test | What it proves |
|------|----------------|
| AmountEntry tests | Every row of the example table in [amount-entry.md](contracts/amount-entry.md) (FR-013, FR-013a, SC-007). |
| LocalizationService tests | Startup, switch and failure rules in [localization-service.md](contracts/localization-service.md) (FR-006, FR-006a, FR-011). |
| Locale file tests | `es.json` key parity, no empty values, matching placeholders, every `L.T` key used in `.razor` exists, and no fixed English text on in-scope pages (FR-007, FR-008, FR-009, SC-002, SC-003). |
| Payment request tests | `PaymentRequest.ToUri()` writes `amount=1.50000000` while the current culture is `es` (R5). |

## 2. Picking Spanish (User Story 1)

1. Clear site data and set the browser language to English. Open the wallet. **Expect**: English; Settings → Language reads "English".
2. Tap Language. **Expect**: a sheet with English (checked) and Español, nothing else.
3. Tap Español. **Expect**: the sheet closes, Settings is in Spanish at once, the row reads "Español", and there is no page reload (DevTools Network shows no document request). Starting from Settings, that took 2 taps (Language, Español) and well under 10 seconds (SC-001).
4. Reload. **Expect**: still Spanish.
5. Pick English. Reload. **Expect**: English.
6. Search the app for "coming soon" / "Multi-language". **Expect**: nothing (SC-005).

## 3. First visit (FR-006a)

1. Clear site data and set the browser language to Español (México). Open the wallet. **Expect**: Spanish; the row reads "Español"; `localStorage["dgb-locale"]` is not set.
2. Pick English. Reload. **Expect**: English, although the browser is still Spanish.
3. Clear site data and set the browser language to Deutsch. **Expect**: English.

## 4. Core screens in Spanish (User Story 2, SC-002)

With Spanish active, walk through Welcome → create wallet → back up the phrase → set a PIN → lock → unlock → Home → Receive → Send (up to confirmation) → History → Settings. **Expect**: no English text except DigiByte, DGB, DigiDollar and Digi-ID. Note any you find as a bug against FR-009.

## 5. Amounts

With Spanish active:

| Action | Expect |
|--------|--------|
| View Home with a balance of 1234.5 DGB | `1.234,50000000 DGB` (Spanish style) |
| Send amount `12,5`, then `12.5` | Both reach confirmation as `12,5 DGB` |
| Send amount `1.234` | Two buttons, "1.234 DGB" and "1,234 DGB"; Continue disabled until one is picked; confirmation shows the picked one |
| Send amount `1.234,5` | Message to leave out thousands marks; Continue disabled |
| Receive, request `2,5` DGB, then copy the payment link | Link contains `amount=2.50000000` |
| Scan or open `digibyte:<address>?amount=1.5` | Send form shows `1,5`, never 15 |
| Open `digibyte:<address>?amount=1.234` | Send amount shows `1,234` with **no** question; confirmation shows 1,234 DGB |
| Spending limits: enter `12,5` as the daily limit | Saved as 12,5 DGB, never 125 |

Switch to English and repeat the `1.234` row. **Expect**: the same question, because the rule doesn't depend on the language.

## 6. Failure cases

| Action | Expect |
|--------|--------|
| First visit, go offline before picking Español, then pick it | Sheet stays open with "Couldn't load Español…"; wallet stays English |
| Private window (storage blocked), pick Español | Works for this visit, no error |
| Width 375 px, open the Language sheet and Spanish Settings | No overlap and no horizontal scroll |
| Keyboard only: Tab to Language, Enter, arrows, Enter, Escape | Sheet works and focus returns to the row (FR-012) |
| Screen reader (TalkBack, VoiceOver or NVDA): open the Language sheet | Reads the title, then each language with its selected state, e.g. "Español, not selected" (FR-012) |
| First visit with DevTools set to "Slow 3G", pick Español | The switch appears within about 1 second; `locales/es.json` in the Network panel is under ~60 KB transferred |

## 7. Release check

`translation-review.md` has an approver and a date on every row (SC-006), and the CHANGELOG mentions Spanish, the reviewed warnings, and that number style now follows the wallet language.

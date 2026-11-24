# Data Model: Spanish in the Language Setting

**Feature**: [spec.md](spec.md) · **Research**: [research.md](research.md)

Nothing new is stored on a server. All state lives on the device.

## Language choice

What the person picked in Settings.

| Field | Type | Rules |
|-------|------|-------|
| code | string | Saved in `localStorage["dgb-locale"]`. Must be a key of `LocalizationService.SupportedLocales`; anything else is treated as "not set". |

- **Written** only when the person picks a language and its translation file loaded (R8).
- **Never written** by the first-visit browser rule (R7).
- **Unreadable storage** (private mode, blocked site data) is treated as "not set", with no error shown.

## Active language

The language the wallet is showing right now. It lives in memory only.

| Field | Type | Rules |
|-------|------|-------|
| CurrentLocale | string | `en` or `es`. |
| Culture | CultureInfo | `en` → `en-US`, `es` → `es`. Applied to the current and default thread culture (R4). |

How it is chosen at startup:

```text
saved code is offered ──yes──> load file ──ok──> active = saved
        │                           └─fails─> active = en (saved code kept)
        no
        ▼
browser's first language, primary subtag, is offered ──yes──> load file ──ok──> active = it (not saved)
        │                                                          └─fails─> active = en
        no
        ▼
active = en
```

When the person picks a language: load file → if it loaded, set active, set culture, save code, raise `OnLocaleChanged`. If it failed, nothing changes and the picker shows an error.

## Offered language

| Field | Type | Rules |
|-------|------|-------|
| code | string | Two-letter ISO 639-1 code (`en`, `es`). |
| nativeName | string | The name written in that language ("English", "Español"). It is shown the same whatever the active language. |

Source: `LocalizationService.SupportedLocales`, reduced to the languages with a file (R1).

## Translation file

`src/DigiByte.Web/wwwroot/locales/{code}.json`: a flat JSON object of key → text.

| Rule | Checked by |
|------|-----------|
| `en.json` is complete and is the fallback for every key. | — |
| `es.json` has exactly the same keys as `en.json`. | Locale parity test (SC-003) |
| No value is empty or whitespace. | Locale parity test |
| `{0}`, `{1}`… placeholders match between languages for each key. | Locale parity test |
| Every literal key used in `.razor` files exists in `en.json`. | Key usage test (FR-007) |
| Product and unit names (DigiByte, DGB, DigiDollar, Digi-ID) appear unchanged. | Review |

## Amount entry result

The outcome of reading one typed amount (`AmountEntry.Parse`, R6). It is never stored.

| Variant | Fields | Meaning |
|---------|--------|---------|
| Empty | — | Nothing typed. |
| Value | amount: decimal | One clear reading. |
| Ambiguous | asDecimal: decimal, asThousands: decimal | `1.234` or `1,234` style input: the person must pick one (FR-013a). |
| Invalid | reason: `NotANumber` \| `MoreThanOneMark` \| `TooManyDecimals` \| `NotPositive` \| `TooLarge` | Shown as a message under the field; the amount is not usable. |

How a screen handles the result:

```text
typing ─> Empty / Invalid ─> Continue disabled, message for Invalid
      ─> Value ─────────────> Continue enabled
      ─> Ambiguous ─────────> Continue disabled, two-button choice shown
                                 └─ pick ─> choice remembered for this exact text ─> Value
                                            (any edit to the text clears the choice)
```

The text is not rewritten after a pick. Rewriting "1.234" as "1.234" (the decimal reading) would be ambiguous again, and rewriting it as "1234" would surprise the person. The confirmation step shows the picked value in the chosen style.

## Translation review record

`specs/028-spanish-language-option/translation-review.md` (R10). It has one row per security or money warning:

| Field | Meaning |
|-------|---------|
| key | The translation key. |
| English | The English text. |
| Español | The Spanish text. |
| approvedBy | The reviewer's name or handle. |
| date | The approval date. |

Release is blocked while any row has no approver (SC-006).

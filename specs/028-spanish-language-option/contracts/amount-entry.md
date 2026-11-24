# Contract: AmountEntry

`src/DigiByte.Wallet/Services/AmountEntry.cs` (**new**). It is a pure function with no culture input: the same keystrokes always give the same result, in every language (FR-013).

```text
AmountEntryResult AmountEntry.Parse(string? text, int maxDecimals, decimal maxValue)
```

## Rules (applied in order)

1. `null`, empty or only whitespace → `Empty`.
2. Remove all whitespace (including no-break and narrow no-break spaces).
3. Any character other than `0-9`, `,` or `.` → `Invalid(NotANumber)`.
4. More than one `,`/`.` in total → `Invalid(MoreThanOneMark)`.
5. No mark → the whole string is an integer.
6. One mark. Let W be the digits before it and F the digits after it. If F is exactly 3 digits and W is 1–3 digits not starting with `0` → `Ambiguous(asDecimal = W.F, asThousands = WF)`.
7. Otherwise the mark is the decimal mark. Empty W means 0 (`,5` → 0.5). Empty F is allowed (`5.` → 5).
8. More than `maxDecimals` fraction digits → `Invalid(TooManyDecimals)`. The input is never rounded.
9. Value ≤ 0 → `Invalid(NotPositive)`.
10. Value > `maxValue` → `Invalid(TooLarge)`. For an `Ambiguous` result, each reading is checked on its own: if only one reading is valid, the result is `Value` with that reading.

DGB callers pass `maxDecimals: 8, maxValue: 21_000_000_000m`.

## Examples (required unit tests, `tests/DigiByte.Wallet.Tests/AmountEntryTests.cs`)

| Input | Result |
|-------|--------|
| `""`, `"  "` | Empty |
| `"12"` | Value 12 |
| `"12,5"`, `"12.5"` | Value 12.5 |
| `"0.00012345"` | Value 0.00012345 |
| `"0,123"`, `"0.123"` | Value 0.123 (leading 0, not a thousands mark) |
| `"1234.567"` | Value 1234.567 (4 whole digits) |
| `"1.234"`, `"1,234"` | Ambiguous(1.234, 1234) |
| `"999,999"` | Ambiguous(999.999, 999999) |
| `"1.234,5"`, `"1,234.5"`, `"1.234.567"` | Invalid(MoreThanOneMark) |
| `"12a"`, `"-5"`, `"1e3"`, `"$5"` | Invalid(NotANumber) |
| `"0.000000001"` | Invalid(TooManyDecimals) |
| `"0"`, `"0,0"` | Invalid(NotPositive) |
| `"21000000001"` | Invalid(TooLarge) |
| `"1 234,5"` | Value 1234.5 |

The last row needs a note. Removing spaces turns `"1 234,5"` into `"1234,5"`, which reads as 1234.5. A space used as a thousands mark is common in Spanish, and this keeps its meaning.

## Screen contract (Send single, Send batch, Receive request amount)

- The input is `type="text" inputmode="decimal"`.
- `Invalid` → a translated message under the field (`amount.invalid.*` keys); Continue/Review is disabled.
- `Ambiguous` → two buttons under the field, each showing one reading in the chosen style (for example "1.234 DGB" and "1,234 DGB" in Spanish). Continue/Review is disabled until one is picked. The pick is kept until the text changes.
- The send confirmation shows the amount that will be sent, in the chosen style.
- Amounts from `digibyte:` URIs, QR codes and `?amount=` navigation are parsed with `CultureInfo.InvariantCulture` and never go through `AmountEntry`. When such an amount prefills the field, the shown text is stored as already picked, so it never triggers the question until the person edits it.
- Spending limits (`Pages/SpendingLimits.razor`) also use `AmountEntry`, but treat `Ambiguous` as invalid (with the `MoreThanOneMark` message) instead of asking.

# Contract: Price-Lock Fields in a Request Link

Extends `specs/030-desktop-paste-scan/contracts/payment-link.md`. FR-027a, FR-029.

## Format

A currency request's `digibyte:` link carries three extra query keys, after the existing `amount`, `label` and `message`:

```
digibyte:<address>?amount=<dgb>&label=<l>&message=<m>&fiat=<decimal>&currency=<ISO>&expires=<unix-seconds>
```

| Key | Value | Rule |
|---|---|---|
| `fiat` | Invariant decimal with a point and no grouping, at most `FiatCurrencies.DecimalsFor(currency)` decimals | The amount in the requester's currency. |
| `currency` | Upper-case code that `FiatCurrencies.Find` knows | |
| `expires` | Unix seconds, UTC | When the price lock ends: the request's `ExpiresAt`. |

- The web payment link stays `{walletBase}pay#` + the link above, so all three keys are after the `#` and never reach the web host.
- Requests in DGB never carry these keys.

## Writing (PaymentRequest.ToUri)

- All three keys are written together, or none are written.
- They are written only when `FiatAmount`, `FiatCurrency` and `ExpiresAt` are all set.

## Reading (PaymentLink.TryRead, PaymentRequest.FromUri)

- The three keys are read only as a group. If any of them is missing, cannot be unescaped or parsed, has an unknown currency, or is `expires ≤ 0`, then **all three are dropped and the rest of the link is read as today**. A broken price-lock field never makes a link invalid. This differs from other keys, where an unescape failure still rejects the link.
- Unknown keys stay ignored, as today.
- `PaymentLinkData` gains `FiatAmount?`, `FiatCurrency?` and `PriceExpiresAt?`. `ScanRouter.TryPayment`, `ScanHandOff`, `PaymentHandoff` and `Send.TryTakeHandoff` carry them through unchanged.

## Showing (Send, and Pay's no-wallet card)

| Condition | Shown |
|---|---|
| Fields present, `now < expires` | "≈ {fiat} {currency} · Price held until {local HH:mm}" |
| Fields present, `now ≥ expires` | Warning: "The price for this request expired at {HH:mm}. The amount may be out of date." Sending stays allowed. |
| Fields absent | Nothing new. |

## Compatibility

- BIP21: wallets ignore unknown keys that do not start with `req-`, so other wallets pay the DGB `amount` to the address as before.
- Links made before this update have no such keys and read exactly as today.

## Tests (xunit, `PaymentLinkTests`, `PaymentRequestTests`)

- Round trip with all three keys.
- Each key missing, malformed, with an unknown currency, or `expires` set to 0 or a negative value: the link stays valid and all three are dropped.
- A comma decimal in `fiat` is rejected.
- A DGB request writes none of the three.
- The values survive `ScanRouter` and `ScanHandOff`.

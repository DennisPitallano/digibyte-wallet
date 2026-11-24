# Contract: Payment Link and the `/pay` Page

## Link format

```text
{walletBase}pay#{bip21}

walletBase = NavigationManager.BaseUri of the wallet that made the link, e.g. https://dgbwallet.app/
bip21      = digibyte:{address}[?amount={0.00000000}][&label={escaped}][&message={escaped}]
             (exactly PaymentRequest.ToUri(); amount uses an invariant point and 8 decimals)
```

Examples:

```text
https://dgbwallet.app/pay#digibyte:dgb1qexampleaddress0000000000000000000000
https://dgbwallet.app/pay#digibyte:dgb1qexampleaddress0000000000000000000000?amount=12.50000000&label=Coffee
```

- Everything after `#` stays in the browser. The host receives `GET /pay` only.
- The link is plain text. No signature, no expiry, and no server state.

## Protocol handler (installed app)

`manifest.webmanifest`:

```json
"protocol_handlers": [
  { "protocol": "web+digiid",    "url": "/identity?uri=%s" },
  { "protocol": "web+digibyte",  "url": "/pay#%s" }
]
```

`/pay` receives `#web%2Bdigibyte%3A…` (escaped) or `#web+digibyte:…`. It unescapes once, replaces a leading `web+digibyte:` with `digibyte:`, and continues as below. If a browser rejects `%s` after `#`, the `web+digibyte` entry is removed from the manifest. There is no query-string fallback (research R6, FR-023).

## Reading rules (`PaymentLink.TryRead`)

| # | Check | On failure |
|---|---|---|
| 1 | An http(s) URL must match `walletBase` in scheme, host and port, with path `/pay`; otherwise the text must start with `digibyte:` / `web+digibyte:` | Invalid |
| 2 | `PaymentRequest.FromUri` returns a request | Invalid |
| 3 | Address ≤ 128 chars and `AddressValidator.IsValid(address, network)` | Invalid |
| 4 | Amount absent, or 0 < amount < 21,000,000,000 with ≤ 8 decimals and invariant `.` | Invalid |
| 5 | Label and message ≤ 256 chars each; control characters removed | Invalid if over the limit |

Invalid always means **no** data is used. Nothing is half filled in.

## `/pay` page behaviour

| Situation | Result |
|---|---|
| Invalid or empty fragment | Heading "Not a valid payment link". Text `pay.invalid`. Button **Go to wallet** (→ `/`). |
| Valid, no wallet on this device | Read-only page: heading `pay.title` ("Payment request"). Network badge when the address is testnet. Address in monospace with **Copy address**. Amount in the active language's number style with **Copy amount**; the copied value uses the invariant point and 8 decimals. Label and message as plain text, never rendered as HTML or links. QR code of the BIP21 text with caption `pay.qr_caption`. Note `pay.any_wallet` ("Pay from any DigiByte wallet."). Button **Set up this wallet** (→ `/welcome`). No send control. |
| Valid, wallet locked | `PaymentHandoff.Set(link)`, then `NavigateTo("/unlock?returnUrl=%2Fsend", replace: true)` |
| Valid, wallet unlocked | `PaymentHandoff.Set(link)`, then `NavigateTo("/send", replace: true)` |

Send calls `PaymentHandoff.Take()` first. When a link is waiting, Send fills in the address and amount, re-checked with its existing query rules as defence in depth, and ignores any query. The payment details never appear in a URL after `/pay#` (FR-021).

## Where links are offered

| Screen | Control | Shown when | Copies |
|---|---|---|---|
| Receive | **Copy payment link** (`receive.copy_pay_link`) | An amount is set | `PaymentLink.Create(base, request)` |
| Receive | **Copy** / **Copy link** (existing) | As today | Address, or the `digibyte:` URI |
| Request (`/payments`) | **Copy payment link** (`payments.copy_pay_link`) | A request is shown | `PaymentLink.Create(base, request)` |
| Request | **Copy link** (existing; relabelled `payments.copy_uri`, "Copy digibyte: link") | As today | `digibyte:` URI |
| Both | **Share** (existing) | Device can share | When an amount is set or a request is shown, shares the payment link (text), falling back to copying it. Otherwise shares the address, as today. |

The QR codes keep the `digibyte:` URI as their content.

# Data Model: Paste, Drop and Link Instead of Scan on Desktop

Nothing in this feature is stored except the scan preference. Everything else exists only while the panel or `/pay` page is open.

## PastedCode (`DigiByte.Wallet/Services/PastedCode.cs`, static)

The text that came from a paste, a typed entry, or a picture or screen decode.

| Member | Meaning | Rules |
|---|---|---|
| `MaxLength` | Largest text accepted | 8,192 characters. That is the largest a QR code can carry in byte mode (2,953 bytes) with margin for base64 PSBTs. Anything longer is refused with `scan.too_long`. |
| `Clean(string? raw) → string?` | Normalised text, or null when empty | Trim white space, including CR and LF. Remove one pair of matching surrounding quotes (`"…"`, `'…'`, `“…”`, `‘…’`, `` `…` ``). Trim again. Return null if the result is empty or longer than `MaxLength`. |
| `LooksSecret(string text) → bool` | Recovery phrase or private key | See research R9: 12/15/18/21/24 words with ≥80% in the BIP39 English list; or a valid WIF; or exactly 64 hex characters. |

Tests cover white space and quote variants, an empty value, the length limit, each phrase length, 11 and 25 words (not secret), a valid WIF on mainnet and testnet, 64-char hex, a redeem script (not flagged), an address (not flagged) and an ordinary sentence (not flagged).

## PaymentLinkData and PaymentLink (`DigiByte.Wallet/Services/PaymentLink.cs`)

```text
PaymentLinkData (record)
  Address   : string     validated DigiByte address, current network
  AmountDgb : decimal?   > 0, < 21,000,000,000, ≤ 8 decimals
  Label     : string?    ≤ 256 chars, control characters removed
  Message   : string?    ≤ 256 chars, control characters removed
  ToBip21(bool includeNote = true) : string
                         the same text PaymentRequest.ToUri() produces; includeNote: false
                         leaves out label and message (used for FR-011a hand-offs, so a
                         note never reaches Send's public on-chain memo)
```

| Member | Behaviour |
|---|---|
| `Create(Uri walletBase, PaymentRequest request) → string` | `{walletBase}pay#{request.ToUri()}`. The fragment is not escaped any further: BIP21 already escapes the label and message. |
| `TryRead(string text, Uri walletBase, Network? network, out PaymentLinkData? data) → bool` | `network` is the wallet's current network, passed to `AddressValidator.IsValid`. On `/pay` with no wallet, it is null, which accepts any DigiByte network, and the page shows which network the address is for. Accepts a `{walletBase}pay#…` URL, a bare `digibyte:` URI, or a `web+digibyte:` URI (escaped or not). Applies research R7. Returns false with `data = null` on any failure; there is never partial data. |
| `SendPath(PaymentLinkData) → string` | `/send?address={escaped}` plus `&amount={invariant}` when an amount is set. **Only `ScanRouter` uses it**, for links pasted into the menu panel, which matches today's scan behaviour. `/pay` never does: it uses `PaymentHandoff`. |

## PaymentHandoff (`DigiByte.Web/Services/PaymentHandoff.cs`, scoped)

An in-memory, one-shot carrier from `/pay` to Send, modelled on `ProposalHandoff`.

| Member | Behaviour |
|---|---|
| `Set(PaymentLinkData link)` | Replaces any waiting link |
| `Take() → PaymentLinkData?` | Returns the waiting link and clears it, or null |

It is never stored or put in a URL. A reload or a new tab starts empty.

**States of a link opened on `/pay`**:

```text
          ┌── TryRead false ───────────────────────────► Invalid (message + Home)
fragment ─┤
          └── TryRead true ─┬─ no wallet on device ────► ReadOnly (details, QR, copy, set up)
                            ├─ wallet locked ──────────► Handoff.Set ─► /unlock?returnUrl=%2Fsend ─► Send (Take → filled)
                            └─ wallet unlocked ────────► Handoff.Set ─► /send (replace) ─► Send (Take → filled)
```

Tests cover the round trip `Create` → `TryRead`, each R7 failure on its own, a look-alike host, another port, `http` against an `https` base, another path, a `web+digibyte` escaped form, an amount with a comma, an amount with 9 decimals, the maximum amount, a missing address, and a testnet address on mainnet.

## ScanInputMode (`DigiByte.Wallet/Services/ScanInputMode.cs`)

```text
enum ScanEntry { Paste, Camera }

record DeviceFacts(
  bool  FinePointer,
  bool? HasCamera,               // null = could not tell
  bool  CanCaptureScreen,
  bool  CanReadClipboardText,
  bool  CanReadClipboardImages)

static ScanEntry Decide(DeviceFacts facts, ScanEntry? remembered)
  remembered is not null                → remembered
  facts.FinePointer                     → Paste
  facts.HasCamera == false              → Paste
  otherwise                             → Camera

static ScanEntry? ParseRemembered(string? stored)   // "paste" | "camera" | anything else → null
static string    ToStored(ScanEntry mode)           // "paste" | "camera"
```

**Stored value**: `localStorage["dgb-scan-mode"]`, written only when the person switches views with the panel's own **Use camera** or **Paste instead** control (FR-004). Read and write go through `try`/`catch`, as `LocalizationService` does. When storage is blocked, the device rule applies every time.

Tests cover each rule branch, a remembered choice overriding the rule both ways, unknown camera with a fine pointer (Paste), unknown camera with touch (Camera), and junk or empty stored values.

## Scan panel state (`QrScannerModal.razor`, component state only)

| Field | Meaning |
|---|---|
| `mode` | `Paste`, `Camera` or `Screen`. Set by `ScanInputMode.Decide` when the panel opens; Screen is entered only on request. |
| `facts` | `DeviceFacts` read once per opening |
| `fieldText` | The text field's value. It is emptied on hand-off, on close, and when `LooksSecret` fires in menu mode. |
| `status` | `Idle`, `Reading`, `Decoding`, `Sharing`, or a message key (`scan.nothing_to_paste`, `scan.no_code_found`, `scan.not_image`, `scan.image_too_large`, `scan.too_long`, `scan.secret_cleared`, `scan.screen_cancelled`, `scan.screen_timeout`, `scan.unknown`) |
| `dragOver` | Highlights the drop area |
| `FromMenu` (parameter) | true only when opened from `NavMenu`; enables the secret check (FR-027) |

**Lifecycle**: Open → Decide mode → (Paste | Camera | Screen) → value obtained → `PastedCode.Clean`. Then:
- menu and `LooksSecret`: clear the field and show the message;
- not menu and `PaymentLink.TryRead` succeeds: the value becomes `link.ToBip21(includeNote: false)` (FR-011a);
- then `OnScanned(value)`, clear the field, stop any camera or screen stream, and `OnClosed`. Close or Dispose always stops the camera and screen streams and clears `fieldText`.

## Scan routing addition (`DigiByte.Web/Services/ScanRouter.cs`)

A new step, after Digi-ID and room invites and before `MultisigSendHelper.ClassifyScan`: if `PaymentLink.TryRead(data, appBaseUri, out var link)` succeeds for a `{appBaseUri}pay#…` URL, return `ScanTarget.Payment` with the URL built as today (personal `/send` or the open shared wallet's `/multisig/{id}/send`). A bare `digibyte:` URI keeps going through `ClassifyScan` exactly as today, so existing behaviour does not change.

# Contract: Scan Panel (`QrScannerModal`)

## Parameters (unchanged plus one)

| Parameter | Type | Notes |
|---|---|---|
| `IsOpen` | `bool` | Unchanged |
| `OnScanned` | `EventCallback<string>` | Unchanged. Receives the **cleaned** text (`PastedCode.Clean`) from any source: paste, typing, picture, drop, camera or screen. |
| `OnClosed` | `EventCallback` | Unchanged |
| `FromMenu` | `bool` (default false) | **New.** Set only by `NavMenu`. Enables the secret check (FR-027) and the menu wording. |

The 10 non-menu callers need no change: Send, Contacts, Identity, MultisigImport, MultisigPending, MultisigPendingDetail, MultisigSend, MultisigSigningRoom, RecoverWallet and DigiDollarSendForm.

## Views

### Paste (default when `ScanInputMode.Decide` → Paste)

```text
┌──────────────────────────────────────────────┐
│ Paste or scan a code                    [×]  │  scan.panel_title
│ ┌──────────────────────────────────────────┐ │
│ │ Paste an address, payment link, Digi-ID  │ │  textarea, scan.field_placeholder
│ │ link or multisig code…                   │ │  autocomplete=off, spellcheck=false,
│ │                                          │ │  autocapitalize=off, autocorrect=off
│ └──────────────────────────────────────────┘ │
│ [ Paste from clipboard ]   Ctrl+V works too  │  scan.paste_button / scan.keyboard_hint (⌘V on Mac)
│                                              │
│ ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐ │
│   Drop a QR code image here, or paste a      │  scan.drop_hint (highlighted while dragOver)
│   screenshot                                 │
│ └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘ │
│ [ Upload image ] [ Scan from screen ] [ Use camera ]   scan.upload_image / scan.from_screen / scan.use_camera
│ <status line, role=status, aria-live=polite> │
└──────────────────────────────────────────────┘
```

- **Scan from screen** appears only when `facts.CanCaptureScreen`.
- **Use camera** appears only when `facts.HasCamera != false`.
- The text field has focus on open.
- Enter submits. A paste event delivering text submits at once. Shift+Enter inserts a line break, for multi-line PSBT text.
- Escape closes the panel. Focus is trapped inside while it is open and returns to the opening button on close.

### Camera

Today's view. **Paste instead** (`scan.paste_instead`) is added next to Upload image and Cancel. Choosing it saves `paste` as the remembered mode.

### Screen

```text
Sharing "<tab title from the browser>" … looking for a QR code     scan.screen_looking
[ Stop ]                                                          scan.screen_stop
```

- No live preview is shown. That avoids a hall-of-mirrors effect when the wallet's own tab is shared, and shows nothing that isn't needed.
- It returns to the Paste view on stop, cancel or timeout, with the matching status message.

## Outcomes and messages

| Event | Menu (`FromMenu`) | Other screens |
|---|---|---|
| Cleaned text, not secret | `OnScanned(text)`, clear field, close | Same |
| Valid payment link to this wallet | Routed by `ScanRouter` as a payment | `OnScanned(link.ToBip21(includeNote: false))`, then clear and close (FR-011a) |
| Cleaned text, `LooksSecret` | Clear field. Status `scan.secret_cleared`. Panel stays open. | `OnScanned(text)`, clear field, close. The screen decides (for example, Recover accepts a WIF). |
| Text over 8,192 chars | `scan.too_long`, field kept for correction (it is not a secret by length alone) | Same |
| Empty clipboard or nothing usable | `scan.nothing_to_paste` | Same |
| Clipboard refused or unsupported | Focus field, `scan.keyboard_hint_full` | Same |
| Picture with no QR code | `scan.no_code_found` | Same |
| Dropped file not an image | `scan.not_image` | Same |
| Picture over 10 MB | `scan.image_too_large` (existing) | Same |
| Screen prompt cancelled or refused | `scan.screen_cancelled` | Same |
| Screen 60 s, no code | `scan.screen_timeout` | Same |
| `ScanRouter` → `Unknown` (menu only) | `scan.unknown` (existing text, extended to mention paste) | n/a |

All status messages go to a `role="status"` live region. Successful hand-offs announce `scan.found_opening` ("Code found. Opening…") just before closing.

## Menu label (FR-005)

| Mode decided for this device | Visible label | `aria-label` |
|---|---|---|
| Paste | `nav.paste_scan` ("Paste / Scan") | `nav.paste_scan.aria` ("Paste or scan a code to pay, sign in or open a shared wallet item") |
| Camera | `nav.scan` ("Scan"), unchanged | `nav.scan.aria`, unchanged |

The label follows the device rule. It does not follow the remembered choice, so the menu doesn't flip because of one camera use.

## New locale keys (English text; Spanish to be supplied and reviewed)

`scan.panel_title`, `scan.field_label`, `scan.field_placeholder`, `scan.paste_button`, `scan.keyboard_hint`, `scan.keyboard_hint_full`, `scan.drop_hint`, `scan.from_screen`, `scan.use_camera`, `scan.paste_instead`, `scan.screen_looking`, `scan.screen_stop`, `scan.screen_cancelled`, `scan.screen_timeout`, `scan.nothing_to_paste`, `scan.not_image`, `scan.too_long`, `scan.secret_cleared`, `scan.found_opening`, `nav.paste_scan`, `nav.paste_scan.aria`, `receive.copy_pay_link`, `receive.qr_for_phone`, `payments.copy_pay_link`, `payments.copy_uri`, `pay.title`, `pay.invalid_title`, `pay.invalid`, `pay.go_to_wallet`, `pay.any_wallet`, `pay.qr_caption`, `pay.copy_address`, `pay.copy_amount`, `pay.set_up`, `pay.testnet`.

`scan.secret_cleared` and `pay.any_wallet` are security and money text. Under spec 028's rule, a native Spanish speaker approves them before release.

# Implementation Plan: Paste, Drop and Link Instead of Scan on Desktop

**Branch**: `feature/desktop-paste-scan` (planned) | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/030-desktop-paste-scan/spec.md`

## Summary

On a desktop, every scan button opens a **paste panel** instead of the camera. A person can paste text, paste or drop a picture of a QR code, or (last priority) read a QR code from another tab or window. Whatever comes in is handed on exactly as a camera scan's result would be. Receive and Request gain **Copy payment link**, a `https://<wallet>/pay#digibyte:…` link. The part after `#` is never sent to the web host. A new `/pay` page opens Send with the payment filled in for a wallet user, or shows a read-only payment page to anyone else.

Technical approach (see [research.md](research.md)):

- **One component, eleven callers unchanged.** `QrScannerModal` keeps its parameters (`IsOpen`, `OnScanned`, `OnClosed`). Its body becomes the paste panel or the camera view, chosen by a small `ScanInputMode` rule. Only `NavMenu` passes the new `FromMenu` flag.
  - On screens other than the menu, a valid payment link to this wallet is turned into its `digibyte:` request, without its label or message, before hand-off (FR-011a). Each screen's own handler then treats it as a scanned request, and no note reaches Send's public on-chain memo.
- **Pure rules in `DigiByte.Wallet`, with tests** (Principle III):
  - `PastedCode`: cleans up pasted text and spots secrets.
  - `PaymentLink`: builds, reads and checks the `/pay#…` link.
  - `ScanInputMode`: decides paste-first or camera-first from the device's facts and the remembered choice.
- **Browser glue in one JS file.** `qrscanner.js` gains:
  - device facts (pointer type, camera, screen-sharing and clipboard support);
  - clipboard reading (text and pictures), and paste and drop listeners;
  - reading a QR code straight from a picture without a base64 round trip;
  - a screen-capture loop that stops itself.
- **Routing reuses `ScanRouter`.** It learns one more form, this wallet's own `/pay#` link, and treats it as a payment. Everything else is unchanged.
- **`/pay` page** (no `AuthGuard`):
  - no wallet on the device: shows the read-only payment page;
  - otherwise: hands the payment to Send in memory through a new one-shot `PaymentHandoff` (like `ProposalHandoff`), then goes to `/send` (through `/unlock?returnUrl=%2Fsend` if locked).

  The address and amount never appear in any URL, so a reload cannot send them to the host.
- **Manifest** gains a `web+digibyte` handler that points at `/pay#%s`. If a browser refuses it, the handler is dropped, with no query fallback.
- **Receive and Request** put the address or request and its copy buttons first on wide screens (≥1024 px, the app's existing breakpoint), with a smaller QR code below.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly), JavaScript (ES2020, plain browser scripts, no bundler)

**Primary Dependencies**: Existing only. `jsQR` 1.4.0 (self-hosted) decodes QR codes, `QRCoder` draws them, `NBitcoin` (via `DigiByte.Crypto`) supplies address, WIF and mnemonic checks, and Tailwind 3 handles styling. No new package.

**Storage**: `localStorage` key `dgb-scan-mode` (`paste` or `camera`) only, the same way the language choice is kept. Nothing pasted, dropped or captured is ever stored.

**Testing**:
- xUnit in `tests/DigiByte.Wallet.Tests`, which already references `DigiByte.Web`. It covers `PastedCode`, `PaymentLink`, `ScanInputMode`, `PaymentHandoff`, the `ScanRouter` additions, and locale parity.
- `node --test` in `src/DigiByte.Web/tests/js` covers the pure helpers in the scanner JS (frame scaling and the timeout clock).
- A manual browser pass follows [quickstart.md](quickstart.md).

**Target Platform**:
- Desktop Chrome, Edge, Firefox and Safari (current and previous major).
- Mobile Chrome and Safari keep today's camera-first flow.
- Installed PWA, for the `web+digibyte` handler.

**Project Type**: Static PWA (Blazor WASM) served by nginx with an SPA fallback. No server work.

**Performance Goals**:
- Paste to routed screen in under 300 ms after the clipboard is read.
- A picture of up to 10 MB decoded in under 1.5 s on a mid-range laptop.
- Screen capture sampled at about 4 frames a second, so a 60 s attempt does not peg a CPU core.

**Constraints**:
- CSP stays as it is: no new hosts, and no inline script beyond the existing hash.
- Nothing pasted or captured leaves the browser.
- Secrets are never kept in component state beyond the hand-off.
- Every new string exists in both `en.json` and `es.json`.
- The first-load size should not grow noticeably; JS changes only, no new library.

**Scale/Scope**:
- 1 shared component reworked, and 1 menu label.
- 11 existing callers with no code change, except `NavMenu` passing `FromMenu`.
- 1 new page (`/pay`), 2 pages re-laid out (Receive, Request), 1 small Send change (taking a waiting payment), 3 new `DigiByte.Wallet` classes and 1 new `DigiByte.Web` service (`PaymentHandoff`).
- About 30 new locale keys.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Verdict |
|---|---|---|
| I. Self-Custody | Yes: a private key can be pasted on Recover, or by mistake in the menu | **Pass.** A pasted key is handed to Recover's own field and the panel's field is cleared (FR-027). A secret pasted in the menu is cleared without being routed. Nothing is stored, logged or sent (FR-026). |
| II. In-Browser Cryptography | Indirectly | **Pass.** No server role is added. `/pay` is a static route of the existing site. QR reading from pictures and screen frames happens in the page. |
| III. Test-First for Wallet Logic | Yes: new `DigiByte.Wallet` classes | **Pass (planned).** `PastedCode`, `PaymentLink` and `ScanInputMode` get unit tests written first. These include negative paths: wrong network, bad checksum, amount ≤ 0 or above the maximum, more than 8 decimals, look-alike host, oversize text, quotes and white space, 12/24-word phrases and WIF keys. |
| IV. Boundary Discipline | Yes | **Pass.** Link and paste rules live in `DigiByte.Wallet`, which depends only on `DigiByte.Crypto` for address, WIF and word-list checks. Browser-only code (clipboard, media, DOM) stays in `DigiByte.Web`. `ScanRouter` stays in `DigiByte.Web` and calls the new Wallet classes. No reference is inverted. |
| V. Resilience | Yes | **Pass.** No backend dependency is added. Every browser capability is optional and degrades to one that always works (see research R3). The camera path is untouched. |
| Security: CSP | Checked | **Pass.** No change. Clipboard, `getDisplayMedia` and drag-and-drop are not governed by CSP, and the host sends no `Permissions-Policy`, so the same-origin defaults allow them. |
| Security: Address handling | Yes | **Pass.** Payment links and pasted addresses go through `AddressValidator.IsValid` before anything is filled in. Send keeps its own check of the query as well. |
| Workflow: Docs | Yes | **Planned.** `docs/PROCESS_FLOWS.md` (scan and pay-link flows), `docs/ARCHITECTURE.md` (the `/pay` route and the `web+digibyte` handler), `CHANGELOG.md`, and the Help text that mentions scanning. |

No violations, so the Complexity Tracking section is empty.

**Post-design re-check (after Phase 1, updated after the follow-up clarifications)**: Still passing. The design added no server call. The link data stays in the fragment and is handed to Send in memory, so it never appears in a URL. Every rule that touches addresses or secrets is in `DigiByte.Wallet`, covered by tests. One item to confirm during implementation: whether the browser accepts `%s` after `#` in the protocol handler's URL (research R6). If not, the handler is dropped, so no privacy exception is ever needed.

## Project Structure

### Documentation (this feature)

```text
specs/030-desktop-paste-scan/
├── plan.md              # This file
├── research.md          # Phase 0: decisions R1–R10
├── data-model.md        # Phase 1: PastedCode, PaymentLink, ScanInputMode, panel state
├── quickstart.md        # Phase 1: manual and automated checks
├── contracts/
│   ├── payment-link.md  # Link format, reading rules, /pay page behaviour
│   ├── scan-panel-ui.md # Panel modes, inputs, outputs, messages, accessibility
│   └── scanner-js.md    # window.qrScanner additions (JS ↔ .NET interop)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks), not created here
```

### Source Code (repository root)

```text
src/DigiByte.Wallet/Services/
├── PastedCode.cs            # NEW: Clean() trims white space and one pair of quotes and enforces the size limit; LooksSecret() spots mnemonics and WIF keys
├── PaymentLink.cs           # NEW: Create(baseUri, PaymentRequest) and TryRead(text, baseUri) → PaymentLinkData; all validation
└── ScanInputMode.cs         # NEW: Decide(DeviceFacts, remembered) → Paste | Camera

src/DigiByte.Web/
├── Components/Shared/
│   └── QrScannerModal.razor # REWORKED: paste panel + camera view + screen view; same parameters plus FromMenu
├── Pages/
│   ├── Pay.razor            # NEW: @page "/pay"; read-only page or hand-off to /unlock or /send
│   ├── Receive.razor        # Copy payment link; wide-screen order
│   └── Payments.razor       # Copy payment link; wide-screen order (the Request screen)
├── Layout/NavMenu.razor     # "Paste / Scan" label on desktop; passes FromMenu
├── Services/ScanRouter.cs   # Recognises this wallet's own /pay# link as a Payment
├── Services/PaymentHandoff.cs  # NEW: one-shot in-memory carrier /pay → Send (like ProposalHandoff)
├── Pages/Send.razor         # Takes a waiting PaymentHandoff before reading its query
├── wwwroot/js/qrscanner.js  # device facts, clipboard, paste/drop, picture decode, screen capture
├── wwwroot/manifest.webmanifest  # + web+digibyte handler
├── wwwroot/css/app.css      # paste panel and drop-zone styles, ≥1024 px Receive/Request order
└── wwwroot/locales/{en,es}.json  # new scan.*, pay.*, receive.* and payments.* keys

tests/DigiByte.Wallet.Tests/
├── PastedCodeTests.cs       # NEW
├── PaymentLinkTests.cs      # NEW
├── ScanInputModeTests.cs    # NEW
├── PaymentHandoffTests.cs   # NEW
└── ScanRouterTests.cs       # + /pay# link cases, + pasted-text parity cases

src/DigiByte.Web/tests/js/
└── qrscanner.test.mjs       # NEW: frame scaling and the 60 s give-up clock (pure helpers)
```

**Structure Decision**: The existing layered monorepo is kept. Rules go in `DigiByte.Wallet`, so they can be tested and are owned by the lowest layer. UI and browser glue go in `DigiByte.Web`. There are no new projects and no server changes.

## Complexity Tracking

None.

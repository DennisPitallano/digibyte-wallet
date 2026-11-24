# Research: Paste, Drop and Link Instead of Scan on Desktop

Each entry gives the decision, the reasons, and what else was considered. Technical Context had no open questions. These entries record the design choices the spec left to planning.

## R1. Where the paste panel lives

**Decision**: Rework `Components/Shared/QrScannerModal.razor` in place. It keeps its public parameters (`IsOpen`, `OnScanned`, `OnClosed`) and gains one optional `FromMenu` flag. Inside, it shows one of three views: **Paste** (default on desktop), **Camera** (today's view) or **Screen**.

**Rationale**:
- The eleven callers need no change, which satisfies User Story 2 at no cost.
- Each caller's `OnScanned` handler already runs its own checks, which covers FR-011.
- The menu is the only caller that needs the secret check, so only `NavMenu` needs to say it is the menu. The signing room also calls `ScanRouter`, but only to read room invites, and it runs its own checks.
- On every other screen, a valid payment link to this wallet is turned into its `digibyte:` request text, without label or message, before hand-off (FR-011a). Screens that already read `digibyte:` requests then accept payment links with no change of their own. That flag is what FR-027 uses to decide on the "secret pasted in the menu" handling.

**Alternatives considered**:
- A new `PastePanel` component next to the scanner: rejected, because all 11 call sites would have to choose between two components.
- A paste field on each page: rejected, because it would show eleven different designs and leave the menu still camera-first.

## R2. Deciding paste-first or camera-first

**Decision**: `ScanInputMode.Decide(facts, remembered)` in `DigiByte.Wallet` (pure, tested):
1. A remembered choice (`paste` or `camera`) wins (FR-004).
2. Otherwise, paste first when `finePointer` is true **or** `hasCamera` is false (FR-001).
3. Otherwise, camera first (FR-002).

The device facts come from `qrScanner.deviceFacts()` in JS:
- `finePointer = matchMedia('(pointer: fine)').matches`
- `hasCamera` = `enumerateDevices()` lists at least one `videoinput`, or `null` when it cannot tell
- `canCaptureScreen = !!navigator.mediaDevices?.getDisplayMedia`
- `canReadClipboardText`
- `canReadClipboardImages`

`hasCamera: null` (unknown) counts as "may have a camera". So an unknown camera on a touch device keeps today's behaviour, and on a mouse device paste still opens first.

**Rationale**:
- Before camera permission is granted, `enumerateDevices` returns entries with empty labels, which is enough to count cameras.
- Safari can return an empty list before permission, hence the tri-state.
- `pointer: fine` describes the **main** pointer, so touch laptops in tablet mode count as touch.

**Alternatives considered**:
- Window width: rejected in clarification Q3.
- User-agent sniffing: unreliable, and it treats iPadOS as a Mac.

## R3. Reading the clipboard, and what happens when the browser refuses

**Decision**: Three ways in, in this order:
1. **Paste from clipboard** button: try `navigator.clipboard.read()` and take the first `image/*` item, else `text/plain`. If `read` is missing, try `navigator.clipboard.readText()`. If either throws (no permission, unsupported, or Firefox's paste prompt dismissed), focus the field and show the hint "Press Ctrl+V (⌘V on Mac) to paste" (FR-007).
2. **Ctrl+V / ⌘V anywhere in the panel**: a `paste` listener on the panel reads `event.clipboardData.items`. An image item goes to picture decoding, otherwise the text is used. This path needs no permission in any browser.
3. **Typing or pasting into the field**: Enter submits. A paste event that delivers text submits at once (FR-008).

**Rationale**: Path 2 always works, so the button is a convenience and never the only way in. Firefox gained `readText` (with a paste prompt) and `read` in recent versions, and older ones fall through to path 2.

**Alternatives considered**: Asking for clipboard permission up front was rejected. It is a prompt the person did not ask for, and Firefox and Safari have no such permission.

## R4. Reading a QR code from a pasted or dropped picture

**Decision**: Add `qrScanner.decodeBlob(blob)` in JS: `createImageBitmap(blob)`, then a canvas, then `jsQR` with `attemptBoth`. It scales down so the longer side is at most 2048 px before decoding. The 10 MB limit and the image-type check happen in JS before decoding (FR-014). Only the decoded **text** crosses to .NET.

The existing **Upload image** button keeps its file input, but uses the same JS path: the `InputFile` element's file is read in JS by element reference. It no longer goes through a .NET base64 data URL.

**Rationale**:
- Today a 10 MB image becomes a 13 MB base64 string marshalled into WASM and back. Decoding in JS avoids that and keeps picture bytes out of .NET memory.
- 2048 px keeps phone screenshots and 4K snips readable while bounding decode time (performance goal: under 1.5 s).

**Alternatives considered**: Native `BarcodeDetector` was rejected because Firefox and desktop Safari don't have it. It could be added later as a faster path when available.

## R5. Scan from screen

**Decision**: `qrScanner.startScreen(dotnetRef)` calls `getDisplayMedia({ video: { frameRate: 5 }, audio: false })`. Frames play into an off-screen `<video>`. Every 250 ms (about 4 fps) a frame is drawn to a canvas, scaled down to at most 1920 px on its longer side, and given to `jsQR`.

Capture stops (all tracks stopped, element cleared) when any of these happens (FR-017):
- a code is found;
- the person presses Stop;
- the panel closes, or the component is disposed when the person navigates away;
- the browser's own "Stop sharing" ends the track (`ended` event);
- 60 s pass with no code.

A refused or cancelled prompt (`NotAllowedError` or `AbortError`) reports `cancelled`, not an error (FR-018). The option is hidden when `canCaptureScreen` is false (FR-015).

**Rationale**:
- A 4 fps sample is enough for a still QR code and keeps CPU low.
- A screen capture is often 2560×1440 or larger, and scaling to 1920 keeps typical on-screen QR codes (≥150 px) readable.
- The pure parts (frame-size maths, the give-up clock) are split into small functions so `node --test` can cover them.

**Alternatives considered**:
- `ImageCapture.grabFrame`: Chromium only.
- Region-select cropping: more UI, and snip-and-paste already covers it.

## R6. Payment link format and the `web+digibyte` handler

**Decision**:
- The link is `<wallet base URI>pay#<BIP21 URI>`, for example `https://dgbwallet.app/pay#digibyte:dgb1q…?amount=12.5&label=Coffee`. The fragment is exactly the existing `PaymentRequest.ToUri()` output. Reading it reuses `PaymentRequest.FromUri` and then applies the checks in R7.
- The manifest gets a handler: `{ "protocol": "web+digibyte", "url": "/pay#%s" }`. `/pay` accepts both `digibyte:…` and `web+digibyte:…` in the fragment, the latter URL-escaped by the browser. The `web+` prefix is stripped exactly as Digi-ID's is in `ScanRouter`.

**Rationale**:
- A fragment is never sent in the HTTP request, and the site already sends `Referrer-Policy: no-referrer`, so the address, amount and note never reach the host or a third party. This is clarification Q1.
- Reusing BIP21 means one reader, and the same text works when pasted into other wallets (after `#`).

**Risk / check during implementation**: The Web App Manifest spec substitutes `%s` anywhere in the handler URL, but this must be confirmed in Chrome and Edge for a `%s` placed after `#` (quickstart step Q-12). **If a browser rejects it, the handler is removed from the manifest and there is no fallback.** This was decided in the clarification session of 2026-11-25. A query-string fallback would put payment details in the host's logs, so it is rejected. Web payment links still use `#` and still work everywhere.

**Alternatives considered**:
- `?address=&amount=` query: rejected in Q1.
- A short-link service: a new server role, rejected under Principle II.
- Encoding the data as base64 in the fragment: makes no privacy difference and makes the link unreadable.

## R7. Checks applied to a payment link (FR-020)

**Decision**: `PaymentLink.TryRead(text, baseUri)` returns data only when all of these hold:
1. If the text is an `http(s)` URL, its scheme, host and port equal `baseUri`'s (the same rule `ScanRouter.RoomInvite` uses), and its path is `/pay`. Otherwise the text must itself start with `digibyte:` or `web+digibyte:`.
2. `PaymentRequest.FromUri` succeeds.
3. The address is at most 128 characters and `AddressValidator.IsValid` is true for the wallet's current network.
4. The amount, if present, is greater than 0 and less than 21,000,000,000, with at most 8 decimal places and an invariant-culture point (this matches `Send.razor`'s query rule).
5. The label and message are each at most 256 characters after unescaping, with control characters removed.

A link that fails any check returns `Invalid`, with no partial data (FR-020).

**Rationale**: These are the same limits Send already applies to its query string, now enforced once in the Wallet layer so `/pay`, `ScanRouter` and tests agree.

## R8. What `/pay` does with a valid link

**Decision**: `Pages/Pay.razor` (`@page "/pay"`, no `AuthGuard`) reads `location.hash` through `NavigationManager.Uri`. Blazor exposes the fragment client-side, and the fragment is never sent to the host. Then:
- **No wallet on the device**: render the read-only page (FR-022): address, amount (shown in the active language's number style), label and message, a QR code of the BIP21 URI, Copy address and Copy amount, and "Set up this wallet" (→ `/welcome`). There is no send control.
- **Wallet exists (locked or unlocked)**: `PaymentHandoff.Set(link)`, a new scoped, in-memory, one-shot service modelled on `ProposalHandoff`. Then:
  - unlocked: `NavigateTo("/send", replace: true)`;
  - locked: `NavigateTo("/unlock?returnUrl=%2Fsend", replace: true)`. `Unlock.razor` already honours a local `returnUrl`.
- **Send**: in `OnInitializedAsync`, *before* reading its query, it calls `PaymentHandoff.Take()`. When that returns a link, Send fills in the address and amount from it, re-checking them with the same rules as its query, and ignores the query.
- **Invalid**: show "This link isn't a valid payment request." with a button to Home.

Clarification (2026-11-25, follow-up Q1): the payment details never appear in any URL after `/pay#`. A reload of Send shows an empty form, because the hand-off is cleared on `Take()`. Blazor WASM scoped services live for the tab, so the hand-off survives the client-side trip through Unlock, and a new tab or a reload starts empty. The label and message are shown only on the read-only page. Send's Memo is written on-chain as public data (an `OP_RETURN` output, read back by `WalletService.ExtractOpReturnMemo`), so a link's label and message are **not** carried into it. The same applies to FR-011a hand-offs, which drop label and message. Separate from this feature: scanning a `digibyte:` request today copies its `message` into the memo (`Send.razor`, `OnQrScanned`). That is worth a separate fix.

**Alternatives considered**:
- `NavigateTo("/send?address=…&amount=…")`, as scans do today: rejected in clarification, because a reload would send the details to the host.
- Putting `/pay` behind `AuthGuard`: rejected, because AuthGuard drops the target and sends people without a wallet to `/welcome`, losing the payment.

## R9. Spotting a secret pasted in the menu (FR-027)

**Decision**: `PastedCode.LooksSecret(text)` is true when either holds:
- after splitting on white space, there are 12, 15, 18, 21 or 24 words and at least 80% of them are in the BIP39 English word list;
- the text passes `PrivateKeyImporter.IsValidWif`, or is a 64-character hex string (a raw private key).

When the panel is opened from the menu and `LooksSecret` is true, the field is cleared at once, nothing is routed, and the person sees "This looks like a recovery phrase or private key. It was cleared and not used." From other screens the text is handed to the screen as a scan would be, and the field is cleared after hand-off in all cases.

**Rationale**:
- 64-char hex overlaps with a redeem script only in theory: redeem scripts are longer and start with `5x…ae`. `ScanRouter` checks the redeem-script shape first in the menu path, so a valid redeem script is never flagged.
- The 80% threshold catches typos without flagging ordinary sentences.

**Alternatives considered**: Checking BIP39 checksums was rejected. It isn't needed to decide "don't keep this on screen", and a near-miss phrase is just as sensitive.

## R10. Wide-screen order on Receive and Request

**Decision**: At `min-width: 1024px` (the app's existing breakpoint in `app.css`), the main card uses CSS `order` and a two-column grid:
- left column: the address in monospace with a wrap-anywhere break, **Copy address**, **Copy payment link** (when an amount is set), and **Share**;
- right column: a smaller QR code (`w-36`), with the caption "Scan with your phone".

Below 1024 px the phone look stays as it is (FR-025).

**Rationale**: One markup tree serves both layouts, so tests and phone behaviour are untouched. On Receive the address block moves before the QR code in the DOM, so wide screens show and read address-first. Below 1024 px CSS `order` puts the QR code back on top, so phones look the same as today. On phones, screen readers then read the address before the QR image, which helps more than it hurts.

**Alternatives considered**: A separate desktop component would duplicate every Receive state (used-address warning, amount, address list).

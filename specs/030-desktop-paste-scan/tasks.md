---
description: "Task list for spec 030: paste, drop and link instead of scan on desktop"
---

# Tasks: Paste, Drop and Link Instead of Scan on Desktop

**Input**: Design documents from `specs/030-desktop-paste-scan/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included for every new class in `DigiByte.Wallet` and for the `ScanRouter` additions. The constitution's Principle III requires tests written first, with negative paths, for wallet logic. The pure JS helpers get `node --test` coverage. UI behaviour is checked with [quickstart.md](quickstart.md).

**Organization**: Tasks are grouped by user story. Phases follow the spec's priorities: US1 and US2 (P1), then US3 and US5 (P2), then US4 and US6 (P3). Story numbers match the spec.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The spec's user story (US1–US6)

## Path Conventions

- Wallet rules: `src/DigiByte.Wallet/Services/`
- Web UI and browser glue: `src/DigiByte.Web/`
- .NET tests: `tests/DigiByte.Wallet.Tests/` (references both `DigiByte.Wallet` and `DigiByte.Web`)
- JS tests: `src/DigiByte.Web/tests/js/` (`npm test --prefix src/DigiByte.Web`)

---

## Phase 1: Setup

**Purpose**: Branch and a clean baseline. No new projects or packages.

- [X] T001 Create branch `feature/desktop-paste-scan` from `main`, and confirm `dotnet build DigiByte.slnx` and `dotnet test tests/DigiByte.Wallet.Tests` pass before any change (repository root)
- [X] T002 [P] Read the current scanner flow end to end so the rework keeps its behaviour: `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`, `src/DigiByte.Web/wwwroot/js/qrscanner.js`, `src/DigiByte.Web/Services/ScanRouter.cs`, and `OnScannedAsync` in `src/DigiByte.Web/Layout/NavMenu.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Text cleaning and the multi-view panel skeleton that every story builds on.

**⚠️ CRITICAL**: No user story work starts until this phase is complete.

- [X] T003 [P] Write failing tests for `PastedCode.Clean` in `tests/DigiByte.Wallet.Tests/PastedCodeTests.cs`:
  - leading and trailing spaces, tabs, CR and LF are removed;
  - one pair of matching quotes is removed for each of `"…"`, `'…'`, `“…”`, `‘…’` and `` `…` ``;
  - mismatched quotes are kept;
  - two nested pairs: only the outer pair is removed;
  - empty or white-space-only input returns null;
  - exactly 8,192 characters is accepted, and 8,193 returns null;
  - the `MaxLength` constant equals 8,192.
- [X] T004 Implement `PastedCode` (static) with `MaxLength = 8192` and `Clean(string? raw) → string?` in `src/DigiByte.Wallet/Services/PastedCode.cs`. Rule: "Trim white space, including CR and LF. Remove one pair of matching surrounding quotes. Trim again. Return null if the result is empty or longer than `MaxLength`." T003 must then pass.
- [X] T005 Restructure `src/DigiByte.Web/Components/Shared/QrScannerModal.razor` into a view switch with `mode` ∈ {`Paste`, `Camera`, `Screen`}. Keep `IsOpen`, `OnScanned` and `OnClosed` exactly as they are, and add `[Parameter] public bool FromMenu { get; set; }`.
  - Move today's camera markup and logic into the `Camera` view, unchanged. For now, the panel opens in `Camera`, so behaviour is identical.
  - Route every value through one private `HandOffAsync(string raw)`: call `PastedCode.Clean`, then `OnScanned`, clear `fieldText`, stop the streams, then `OnClosed`.
  - Implement `IAsyncDisposable`, calling `qrScanner.stop()` and disposing the `DotNetObjectReference`.
- [X] T006 Add a `role="status"` `aria-live="polite"` status line, plus a `status` message-key field rendered through `L.T(...)`, to `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`. Replace the existing `errorMessage` usage with it, so all later messages share one live region (FR-029).

**Checkpoint**: The app builds, every scan button behaves exactly as before, and `PastedCodeTests` pass.

---

## Phase 3: User Story 1 - Paste instead of scan on a desktop (Priority: P1) 🎯 MVP

**Goal**: On a mouse or trackpad device, or one with no camera, the menu's **Paste / Scan** opens a paste panel. Pasted or typed text routes exactly as a scanned code would. Phones keep camera-first, with paste available.

**Independent Test**: Quickstart Q-1 to Q-7, Q-19, and P-1 and P-2.

### Tests for User Story 1 ⚠️ (write first, must fail)

- [X] T007 [P] [US1] Write failing tests for `ScanInputMode` in `tests/DigiByte.Wallet.Tests/ScanInputModeTests.cs`:
  - the remembered choice wins both ways;
  - `FinePointer=true` → Paste;
  - `HasCamera=false` → Paste;
  - touch with `HasCamera=true` → Camera;
  - touch with `HasCamera=null` → Camera;
  - fine pointer with `HasCamera=null` → Paste;
  - `ParseRemembered`: `"paste"` and `"camera"` parse, while `"PASTE"`, `""`, `null` and `"x"` give null;
  - `ToStored` round trip.
- [X] T008 [P] [US1] Write failing tests for `PastedCode.LooksSecret` in `tests/DigiByte.Wallet.Tests/PastedCodeTests.cs`:
  - true for 12-, 15-, 18-, 21- and 24-word BIP39 English phrases, and for a 12-word phrase with 2 misspelt words (≥80% rule);
  - false for 11 and 25 words, and for an ordinary 12-word English sentence;
  - true for a valid mainnet WIF, a valid testnet WIF, and exactly 64 hex characters;
  - false for a DigiByte address, a `digibyte:` URI, a multisig redeem script hex (starts `5x…`, ends `ae`, longer than 64), a base64 PSBT and a Digi-ID URI.
- [X] T009 [P] [US1] Extend `tests/DigiByte.Wallet.Tests/ScanRouterTests.cs` with a parity theory. For every code kind (bare address, `digibyte:` with amount, Digi-ID, `web+digiid`, room invite, sign-room invite, PSBT, redeem script, unknown text), `ScanRouter.Route(PastedCode.Clean("  \"" + code + "\"\r\n"), base, null)` must equal `ScanRouter.Route(code, base, null)` (SC-003). Also check that a pasted payment with an open shared wallet still goes to `/multisig/{id}/send`.

### Implementation for User Story 1

- [X] T010 [P] [US1] Implement `ScanInputMode` in `src/DigiByte.Wallet/Services/ScanInputMode.cs`:
  - `enum ScanEntry { Paste, Camera }`;
  - `record DeviceFacts(bool FinePointer, bool? HasCamera, bool CanCaptureScreen, bool CanReadClipboardText, bool CanReadClipboardImages)`;
  - `Decide(facts, remembered)`, `ParseRemembered(string?)` and `ToStored(ScanEntry)`, exactly as in data-model.md.

  T007 must then pass.
- [X] T011 [P] [US1] Implement `PastedCode.LooksSecret(string text)` in `src/DigiByte.Wallet/Services/PastedCode.cs`:
  - split on white space;
  - a word count in {12, 15, 18, 21, 24} with ≥80% of the words in `NBitcoin.Wordlist.English` → true;
  - `DigiByte.Crypto.KeyGeneration.PrivateKeyImporter.IsValidWif(text)` → true;
  - `^[0-9a-fA-F]{64}$` → true;
  - otherwise false.

  T008 must then pass.
- [X] T012 [P] [US1] Add `qrScanner.deviceFacts()` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`, per contracts/scanner-js.md:
  - `finePointer` from `matchMedia('(pointer: fine)')`;
  - `hasCamera` from `enumerateDevices()` (any `videoinput` → true, an empty list → false, a throw or missing API → null);
  - `canCaptureScreen`, `canReadClipboardText` (`navigator.clipboard?.readText`) and `canReadClipboardImages` (`navigator.clipboard?.read`).

  It must never prompt.
- [X] T013 [P] [US1] Add `qrScanner.readClipboard()` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`:
  - try `navigator.clipboard.read()` and take the first `text/plain` item for now (image items are handled in US3); fall back to `readText()`;
  - return `{kind:'text', text}`, `{kind:'empty'}` or `{kind:'refused'}` on any throw;
  - never log values.
- [X] T014 [US1] Build the Paste view in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`, per contracts/scan-panel-ui.md:
  - a labelled `<textarea>` with `autocomplete="off" spellcheck="false" autocapitalize="off" autocorrect="off"`, focused on open;
  - **Paste from clipboard** calls `qrScanner.readClipboard`;
  - on `refused`, focus the field and show `scan.keyboard_hint_full`; on `empty`, show `scan.nothing_to_paste`;
  - Enter submits and Shift+Enter inserts a newline;
  - an `@onpaste` that delivers text submits once the field updates;
  - Cleaned text over 8,192 characters shows `scan.too_long`, and the field is kept;
  - **Use camera** is shown only when `facts.HasCamera != false`.
- [X] T015 [US1] On open in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`:
  - call `qrScanner.deviceFacts()`, read `localStorage["dgb-scan-mode"]` inside try/catch (as `LocalizationService` does), and set `mode = ScanInputMode.Decide(facts, ScanInputMode.ParseRemembered(stored))`;
  - start the camera only when `mode == Camera`;
  - add **Paste instead** to the Camera view;
  - write `ScanInputMode.ToStored(mode)` only when the person uses **Use camera** or **Paste instead** (FR-004).
- [X] T016 [US1] Secret handling in `HandOffAsync` in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`. When `FromMenu && PastedCode.LooksSecret(clean)`: clear `fieldText` at once, set status `scan.secret_cleared`, do not call `OnScanned`, and keep the panel open. In every other case, clear `fieldText` after hand-off. Also clear `fieldText` on close and on Dispose (FR-026, FR-027).
- [X] T017 [US1] Keyboard and focus in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`: Escape closes; Tab and Shift+Tab are trapped inside the dialog; focus returns to the element that opened it. Reuse the pattern the More sheet uses in `src/DigiByte.Web/Layout/NavMenu.razor` (`OnMoreKeyDown`). Announce `scan.found_opening` before closing on success (FR-029).
- [X] T018 [US1] Update `src/DigiByte.Web/Layout/NavMenu.razor`:
  - pass `FromMenu="true"` to `<QrScannerModal>`;
  - in `OnInitialized`, read `matchMedia('(pointer: fine)').matches` **synchronously** through `IJSInProcessRuntime` (available in Blazor WebAssembly), so the first render already shows the right label and never flashes "Scan" on a desktop;
  - after first render, read `qrScanner.deviceFacts()` and keep `ScanInputMode.Decide(facts, null)`. This only changes the label when a touch device turns out to have no camera;
  - when it is Paste, render `nav.paste_scan` and `aria-label` `nav.paste_scan.aria`; otherwise keep `nav.scan` and `nav.scan.aria` (FR-005).

  The label ignores the remembered choice.
- [X] T019 [P] [US1] Add the English text for the US1 keys to `src/DigiByte.Web/wwwroot/locales/en.json`: `scan.panel_title`, `scan.field_label`, `scan.field_placeholder`, `scan.paste_button`, `scan.keyboard_hint`, `scan.keyboard_hint_full`, `scan.use_camera`, `scan.paste_instead`, `scan.nothing_to_paste`, `scan.too_long`, `scan.secret_cleared`, `scan.found_opening`, `nav.paste_scan` and `nav.paste_scan.aria`. Extend `scan.unknown` to mention pasting. The keyboard hints use `{0}` for "Ctrl+V" or "⌘V", chosen in the component from `navigator.platform`/`userAgentData`.
- [X] T020 [P] [US1] Add the Spanish text for the same keys to `src/DigiByte.Web/wwwroot/locales/es.json`. Mark `scan.secret_cleared` for native-speaker review in `specs/028-spanish-language-option/translation-review.md`.
- [X] T021 [P] [US1] Paste panel styles in `src/DigiByte.Web/wwwroot/css/app.css`: panel card, textarea, button row and status line. Use the existing `--ui-*` tokens, in light and dark. The panel stays usable at phone width.

**Checkpoint**: Quickstart Q-1 to Q-7, Q-19 and P-1, P-2 pass. The MVP can ship here.

---

## Phase 4: User Story 2 - The same paste panel on every screen that scans (Priority: P1)

**Goal**: All 10 non-menu scan buttons open the same panel on desktop, and their own handlers receive the value.

**Independent Test**: For each of the 10 screens, the scan button on desktop opens Paste, and a valid pasted value behaves like a scan. Quickstart Q-8.

- [X] T022 [US2] Check every non-menu caller still compiles and needs no change: `Pages/Send.razor`, `Pages/Contacts.razor`, `Pages/Identity.razor`, `Pages/MultisigImport.razor`, `Pages/MultisigPending.razor`, `Pages/MultisigPendingDetail.razor`, `Pages/MultisigSend.razor`, `Pages/MultisigSigningRoom.razor`, `Pages/RecoverWallet.razor` and `Components/DigiDollar/DigiDollarSendForm.razor` under `src/DigiByte.Web/`. Fix any handler that relied on the panel closing **before** `OnScanned` ran: `HandOffAsync` calls `OnScanned`, then `OnClosed`, as today's `OnScanResult` does.
- [X] T023 [US2] In `src/DigiByte.Web/Pages/RecoverWallet.razor`, confirm the pasted WIF reaches `wifInput` through `OnQrScanned`, and that the panel's field is empty on reopen (T016). Also make sure no other copy of the key is held by the panel component after close.
- [X] T024 [P] [US2] Update the scan-button labels in `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`: `send.scan_qr` ("Scan QR"), `dd.send.scan` ("Scan QR"), `identity.scan` ("Scan Digi-ID QR Code"), `wallet.scan` and `dd.scan` ("Scan"). Find where each key is used first. Change the wording to "Paste or scan" (and "Paste or scan a Digi-ID code" for `identity.scan`) in both languages **only** for keys on a button that opens `QrScannerModal`. Keep the same keys.

**Checkpoint**: US1 and US2 together give the complete paste experience.

---

## Phase 5: User Story 3 - Read a QR code from a screenshot or image (Priority: P2)

**Goal**: A pasted screenshot or dropped image is decoded locally and handed on as a scan.

**Independent Test**: Quickstart Q-9 and Q-10.

### Tests for User Story 3 ⚠️

- [X] T025 [P] [US3] Create `src/DigiByte.Web/tests/js/qrscanner.test.mjs` with `node:test` cases for `qrScanner._fitWithin(w, h, max)`:
  - 4000×3000 at max 2048 → 2048×1536;
  - 1000×800 is unchanged;
  - a portrait image is scaled on its height;
  - zero or negative sizes return zero.

  Load `qrscanner.js` into a fake `window` object, as the existing `crypto.test.mjs` loads its script.

### Implementation for User Story 3

- [X] T026 [US3] Add `_fitWithin(w, h, max)` and `decodeBlob(blob)` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`:
  - reject `!blob.type.startsWith('image/')` → `'notimage'`, and `blob.size > 10 * 1024 * 1024` → `'toolarge'`;
  - `createImageBitmap`, then scale so the longer side is ≤ 2048, then canvas, then `jsQR(..., { inversionAttempts: 'attemptBoth' })`;
  - return the text or null;
  - lazy-load `js/jsqr.min.js` as `decodeImage` does.

  T025 must then pass.
- [X] T027 [US3] Extend `qrScanner.readClipboard()` in `src/DigiByte.Web/wwwroot/js/qrscanner.js` to prefer the first `image/*` clipboard item. Decode it with `decodeBlob`, then return `{kind:'code', text}` or `{kind:'nocode'}`.
- [X] T028 [US3] Add `attachPanel(panelEl, dotnetRef)` and `detachPanel(panelEl)` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`:
  - a `paste` listener intercepts image items anywhere in the panel and leaves text pastes inside the textarea to the field;
  - `dragenter`, `dragover`, `dragleave` and `drop` call `OnDragState(bool)`;
  - a drop decodes the first file with `decodeBlob`;
  - results go to `OnPanelPaste(kind, text)` / `OnPanelDrop(kind, text)` with `kind` ∈ {`code`, `nocode`, `notimage`, `toolarge`}.
- [X] T029 [US3] Add `decodeInputFile(inputEl)` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`. Switch the existing **Upload image** in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor` to call it with an `ElementReference`, removing the .NET base64 data-URL path (`OnFileSelected`'s buffer and `Convert.ToBase64String`). Keep `decodeImage` in JS for compatibility.
- [X] T030 [US3] In `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`:
  - add the drop area to the Paste view (contracts/scan-panel-ui.md) with a `dragOver` highlight class;
  - call `attachPanel` after the Paste view renders, and `detachPanel` on view change, close and Dispose;
  - add `[JSInvokable] OnPanelPaste`, `OnPanelDrop` and `OnDragState`;
  - map `code` to `HandOffAsync(text)`, `nocode` to `scan.no_code_found`, `notimage` to `scan.not_image`, and `toolarge` to `scan.image_too_large`;
  - show `scan.decoding` while decoding.
- [X] T031 [P] [US3] Add `scan.drop_hint` and `scan.not_image` to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`.
- [X] T032 [P] [US3] Drop-area styles in `src/DigiByte.Web/wwwroot/css/app.css`: dashed border, the highlight while dragging, and a visible focus ring.

**Checkpoint**: Snip-and-paste and drag-and-drop work alongside text paste.

---

## Phase 6: User Story 5 - Share a payment link that works when clicked (Priority: P2)

**Goal**: **Copy payment link** on Receive and Request. `/pay` opens Send for a wallet user, or a read-only page for anyone else. The data stays after `#`.

**Independent Test**: Quickstart Q-12 to Q-17.

### Tests for User Story 5 ⚠️ (write first, must fail)

- [X] T033 [P] [US5] Write failing tests in `tests/DigiByte.Wallet.Tests/PaymentLinkTests.cs`:
  - `Create(new Uri("https://dgbwallet.app/"), request)` equals `"https://dgbwallet.app/pay#" + request.ToUri()`;
  - `TryRead` round trip for an address only; an address and amount; an address, amount, label and message;
  - a bare `digibyte:` URI; `web+digibyte:` plain; `web%2Bdigibyte%3A` escaped.

  Each of these must fail with `data == null`:
  - another host, another port, `http` against an `https` base, path `/send`;
  - an address with one changed character, a testnet address with a mainnet `network`, a 129-character address, a missing address;
  - `amount=0`, `amount=-1`, `amount=1,5`, `amount=0.123456789`, `amount=21000000000`;
  - a label of 257 characters.

  Also: a label with control characters comes back with them removed; `SendPath` gives `/send?address=…&amount=12.5` with an invariant point, and drops `&amount` when there is no amount; `ToBip21(includeNote: false)` round-trips through `PaymentRequest.FromUri` to the same address and amount, with null label and message; `ToBip21()` keeps the label and message.
- [X] T034 [P] [US5] Extend `tests/DigiByte.Wallet.Tests/ScanRouterTests.cs`:
  - `{base}pay#digibyte:{addr}?amount=2` routes to `ScanTarget.Payment` with URL `/send?address={addr}&amount=2`, or `/multisig/{id}/send?...` with a shared wallet open;
  - a look-alike-host `/pay#` link routes to `Unknown`;
  - a bare `digibyte:` URI still routes as before.

### Implementation for User Story 5

- [X] T035 [US5] Implement `PaymentLinkData` (record: `Address`, `AmountDgb`, `Label`, `Message`, `ToBip21(bool includeNote = true)`, where `includeNote: false` leaves out the label and message) and static `PaymentLink` in `src/DigiByte.Wallet/Services/PaymentLink.cs`, with:
  - `Create(Uri walletBase, PaymentRequest request) → string`;
  - `TryRead(string text, Uri walletBase, NBitcoin.Network? network, out PaymentLinkData? data) → bool`;
  - `SendPath(PaymentLinkData) → string`.

  Apply research R7 verbatim:
  1. An http(s) URL must match `walletBase` in scheme, host and port, with path `/pay`; otherwise the text must start with `digibyte:` / `web+digibyte:`.
  2. `PaymentRequest.FromUri` succeeds.
  3. "Address ≤ 128 chars and `AddressValidator.IsValid(address, network)`".
  4. "0 < amount < 21,000,000,000 with ≤ 8 decimals and invariant `.`".
  5. "Label and message ≤ 256 chars each; control characters removed".

  T033 must then pass.
- [X] T036 [US5] In `src/DigiByte.Web/Services/ScanRouter.cs`, add a step after `RoomInvite` and before `MultisigSendHelper.ClassifyScan`: when `data` is an http(s) URL and `PaymentLink.TryRead(data, new Uri(appBaseUri), null, out var link)` succeeds, return `ScanTarget.Payment` with the same personal or shared Send URL the existing payment case builds. T034 must then pass.
- [X] T037 [US5] In `HandOffAsync` in `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`, when `!FromMenu` and `PaymentLink.TryRead(clean, new Uri(Nav.BaseUri), null, out var link)` succeeds, pass `link.ToBip21(includeNote: false)` to `OnScanned` instead of `clean` (FR-011a). A payment link pasted on any such screen then works exactly as scanning the request would on that screen, and no note reaches Send's public on-chain memo. The `ToBip21(includeNote: false)` behaviour is covered by T033.
- [X] T038 [US5] Create `PaymentHandoff` (scoped; `Set(PaymentLinkData)`, `Take() → PaymentLinkData?`, which clears on take) in `src/DigiByte.Web/Services/PaymentHandoff.cs`, modelled on `ProposalHandoff.cs`. Register it with `builder.Services.AddScoped<DigiByte.Web.Services.PaymentHandoff>()` in `src/DigiByte.Web/Program.cs`. Add `tests/DigiByte.Wallet.Tests/PaymentHandoffTests.cs`, checking that `Take` returns the link once and then null, and that `Set` replaces a waiting link.
- [X] T039 [US5] In `OnInitializedAsync` of `src/DigiByte.Web/Pages/Send.razor`, call `PaymentHandoff.Take()` **before** parsing the query. When a link is returned, set `destinationAddress` and the amount from it, after the same checks the query path applies (`AddressValidator.IsValid`, ≤128 chars, 0 < amount < 21,000,000,000), and skip the query parameters.
- [X] T040 [US5] Create `src/DigiByte.Web/Pages/Pay.razor` (`@page "/pay"`, **no** `<AuthGuard>`), per contracts/payment-link.md:
  - read the fragment from `Nav.Uri`, unescape it once, and map a leading `web+digibyte:` to `digibyte:`;
  - call `PaymentLink.TryRead` with the current network when a wallet exists, else null;
  - invalid → the invalid view;
  - no wallets (`WalletService.GetAllWalletsAsync()` is empty) → the read-only view;
  - otherwise `PaymentHandoff.Set(link)`, then: locked → `Nav.NavigateTo("/unlock?returnUrl=%2Fsend", replace: true)`; unlocked → `Nav.NavigateTo("/send", replace: true)`.

  The address and amount must never be put in any URL (FR-021).
- [X] T041 [US5] Read-only view in `src/DigiByte.Web/Pages/Pay.razor`:
  - address in monospace with **Copy address**;
  - amount in the active language's number style, with **Copy amount** copying the invariant `F8` value;
  - label and message as plain text;
  - a testnet badge when the address is testnet;
  - a QR code of `link.ToBip21()` via `QRCoder`, as `Payments.razor` does, with caption `pay.qr_caption`;
  - `pay.any_wallet`, and **Set up this wallet** → `/welcome`;
  - no send control.

  Invalid view: `pay.invalid_title`, `pay.invalid` and **Go to wallet** → `/`.
- [X] T042 [US5] Check `src/DigiByte.Web/Layout/MainLayout.razor` and `src/DigiByte.Web/App.razor`, so `/pay` renders for a visitor with no wallet. Remove any first-run redirect to `/welcome` that would fire before `Pay.razor` runs, the way `/identity` is allowed. Hide the bottom menu on `/pay` when no wallet exists.
- [X] T043 [P] [US5] In `src/DigiByte.Web/Pages/Receive.razor`:
  - add **Copy payment link** (`receive.copy_pay_link`), shown when `requestAmount is not null`, copying `PaymentLink.Create(new Uri(Nav.BaseUri), <current PaymentRequest>)`;
  - when an amount is set, **Share** shares that link, falling back to copying it;
  - keep the existing Copy / Copy link (`digibyte:`) and the QR content unchanged.
- [X] T044 [P] [US5] In `src/DigiByte.Web/Pages/Payments.razor`:
  - add **Copy payment link** (`payments.copy_pay_link`) for the active request;
  - relabel the existing copy button to `payments.copy_uri` ("Copy digibyte: link");
  - **Share** shares the payment link;
  - the QR content stays `activeRequest.ToUri()`.
- [X] T045 [P] [US5] Add `{ "protocol": "web+digibyte", "url": "/pay#%s" }` to `protocol_handlers` in `src/DigiByte.Web/wwwroot/manifest.webmanifest`, next to `web+digiid`.
- [X] T046 [P] [US5] Add the English and Spanish text in `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json` for `receive.copy_pay_link`, `payments.copy_pay_link`, `payments.copy_uri`, `pay.title`, `pay.invalid_title`, `pay.invalid`, `pay.go_to_wallet`, `pay.any_wallet`, `pay.qr_caption`, `pay.copy_address`, `pay.copy_amount`, `pay.set_up` and `pay.testnet`. Mark `pay.any_wallet` for native-speaker review.
- [ ] T047 [US5] Run quickstart Q-12 in installed Chrome or Edge, and check the manifest's `/pay#%s` handler is accepted (DevTools → Application → Manifest). If it is rejected, **remove** the `web+digibyte` entry from `src/DigiByte.Web/wwwroot/manifest.webmanifest`. There is no query fallback (FR-023). Record the result in `specs/030-desktop-paste-scan/research.md` (R6).

**Checkpoint**: Payment links work end to end and fill in nothing when invalid.

---

## Phase 7: User Story 4 - Read a QR code from another tab or window (Priority: P3)

**Goal**: **Scan from screen** reads a QR code from a shared tab or window, then stops sharing at once. This story can be left out of the release without affecting the others (clarification Q4).

**Independent Test**: Quickstart Q-11, and Q-17 for the network check.

### Tests for User Story 4 ⚠️

- [X] T048 [P] [US4] Add `node:test` cases for `qrScanner._giveUpClock(startMs, nowMs, limitMs = 60000)` to `src/DigiByte.Web/tests/js/qrscanner.test.mjs`: false at 0 and 59,999 ms, true at 60,000 and above, and a custom limit is honoured.

### Implementation for User Story 4

- [X] T049 [US4] Add `_giveUpClock`, `startScreen(dotnetRef)` and `stopScreen()` to `src/DigiByte.Web/wwwroot/js/qrscanner.js`, per research R5 and contracts/scanner-js.md:
  - `getDisplayMedia({ video: { frameRate: 5 }, audio: false })`;
  - an off-screen `<video>`, sampled every 250 ms, scaled with `_fitWithin(…, 1920)`, decoded with `jsQR`;
  - on a code, stop all tracks, then call `OnScreenResult(text)`;
  - on the track's `ended` event → `OnScreenEnded('ended')`; at 60 s → `OnScreenEnded('timeout')`;
  - `NotAllowedError` / `AbortError` → return `'cancelled'`; no API → `'unsupported'`;
  - `stopScreen` is idempotent and leaves no live track.
- [X] T050 [US4] Add the Screen view to `src/DigiByte.Web/Components/Shared/QrScannerModal.razor`:
  - a **Scan from screen** button in the Paste view, shown only when `facts.CanCaptureScreen`;
  - the Screen view shows `scan.screen_looking` and **Stop**, with no preview;
  - add `[JSInvokable] OnScreenResult` → `HandOffAsync`, and `OnScreenEnded(reason)` → back to Paste with `scan.screen_timeout` for `timeout`, or no message for `stopped`/`ended`;
  - a `'cancelled'` return → `scan.screen_cancelled`;
  - call `stopScreen()` on Stop, close, view change and Dispose.
- [X] T051 [P] [US4] Add `scan.from_screen`, `scan.screen_looking`, `scan.screen_stop`, `scan.screen_cancelled` and `scan.screen_timeout` to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`.

**Checkpoint**: Scan from screen works, and can be held back without touching the other stories.

---

## Phase 8: User Story 6 - Receive leads with the address on wide screens (Priority: P3)

**Goal**: At ≥1024 px, Receive and Request show the address or request with its copy buttons first, and a smaller QR code labelled for phones. Phones are unchanged.

**Independent Test**: Quickstart Q-18 and P-3.

- [X] T052 [US6] In `src/DigiByte.Web/Pages/Receive.razor`, move the address block (address text, Copy, Copy payment link, Share, Download) before the `qr-frame` block in the DOM. Wrap both in a container with class `receive-main`, and add the caption `receive.qr_for_phone` under the QR image.
- [X] T053 [US6] In `src/DigiByte.Web/Pages/Payments.razor`, wrap the active request card's details and QR code in a `receive-main` container, with the details first in the DOM, and add the same caption under the QR code.
- [X] T054 [P] [US6] In `src/DigiByte.Web/wwwroot/css/app.css`:
  - `.receive-main`: below 1024 px, a single column with the QR block at `order: -1`, so phones look as they do today;
  - inside `@media (min-width: 1024px)`: a two-column grid with details left and the QR code right at `w-36`;
  - hide `receive.qr_for_phone` below 1024 px.
- [X] T055 [P] [US6] Add the English and Spanish text for `receive.qr_for_phone` to `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`.

**Checkpoint**: All six stories are done.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T056 [P] Update `docs/PROCESS_FLOWS.md` with the scan flow (paste, camera or screen, then routing) and the payment-link flow (`/pay` states).
- [ ] T057 [P] Update `docs/ARCHITECTURE.md`: the `/pay` route, the `web+digibyte` handler, and `PastedCode`/`PaymentLink`/`ScanInputMode` in `DigiByte.Wallet`.
- [ ] T058 [P] Update the Help answers that say to scan or copy the BIP21 URI (for example `help.contacts_payments.2.a` in `src/DigiByte.Web/wwwroot/locales/en.json` and `es.json`) to mention **Paste / Scan** and **Copy payment link**.
- [ ] T059 [P] Add a `CHANGELOG.md` entry under Unreleased: "Paste / Scan on desktop, QR from screenshot or drop, scan from screen, and payment links".
- [ ] T060 Run `dotnet build DigiByte.slnx`, `dotnet test tests/DigiByte.Wallet.Tests` and `npm test --prefix src/DigiByte.Web`. Fix any locale parity failure (FR-028, SC-008).
- [ ] T061 Run every check in `specs/030-desktop-paste-scan/quickstart.md` (Q-1 to Q-21 including Q-14a, P-1 to P-4) in Chrome or Edge, Firefox and, if available, Safari, plus a phone. Record results and any browser gaps in the PR description.
- [ ] T062 Privacy pass (SC-007): with DevTools Network open, repeat Q-2, Q-9, Q-11 and Q-14. Confirm no request carries pasted text, image data, screen data, or a query on `/pay`. Search the new code for `console.log` or `console.error` of values, and remove any.
- [ ] T063 Send `scan.secret_cleared`, `pay.any_wallet` and any other money or security text to the native Spanish reviewer, per spec 028. Apply the corrections in `src/DigiByte.Web/wwwroot/locales/es.json`.

---

## Requirement Coverage

Every functional requirement in [spec.md](spec.md) maps to at least one task.

| Requirement | Tasks |
|---|---|
| FR-001 Paste first on mouse or no-camera devices | T010, T012, T015, T018 |
| FR-002 Camera first on touch phones, paste offered | T010, T015 |
| FR-003 One-step switch between paste and camera | T014, T015 |
| FR-004 Remember the last choice on this device | T007, T010, T015 |
| FR-005 "Paste / Scan" label and name on desktop | T018, T019, T020 |
| FR-006 Focused field and Paste from clipboard button | T014 |
| FR-007 Clipboard read, keyboard-hint fallback | T013, T014 |
| FR-008 Handle on paste or Enter | T014 |
| FR-009 Ignore white space and quotes | T003, T004 |
| FR-010 Menu paste routes like a scan | T005, T009 |
| FR-011 Other screens receive the value like a scan | T005, T022 |
| FR-011a Payment links on other screens become `digibyte:` requests without a note | T037 |
| FR-012 Read a QR code from a pasted picture | T026, T027, T028 |
| FR-013 Read a dropped image, with drop highlight | T028, T030, T032 |
| FR-014 Refuse non-images and files over 10 MB; keep Upload | T026, T029, T030 |
| FR-015 Scan from screen only where supported | T050 |
| FR-016 Screen frames stay in the browser | T049, T062 |
| FR-017 Sharing stops on code, Stop, close, leave, 60 s | T048, T049, T050 |
| FR-018 A refused prompt leaves the panel usable | T049, T050 |
| FR-019 Copy payment link on Receive and Request, link after `#` | T035, T043, T044 |
| FR-020 Links checked like a scanned payment | T033, T035 |
| FR-021 Send filled in after unlock, hand-off in memory | T038, T039, T040 |
| FR-022 Read-only page for payers without the wallet | T041, T042 |
| FR-023 `web+digibyte` handler after `#`, or left out | T045, T047 |
| FR-024 Only this wallet's own links followed | T034, T036 |
| FR-025 Wide-screen order on Receive and Request | T052, T053, T054 |
| FR-026 Nothing stored, logged or sent; no auto-fill | T014, T016, T062 |
| FR-027 Field emptied; secrets cleared in the menu | T008, T011, T016, T023 |
| FR-028 English and Spanish text, parity tests pass | T019, T020, T031, T046, T051, T055, T060, T063 |
| FR-029 Keyboard and screen-reader use, announced results | T006, T017 |

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)**: none.
- **Foundational (2)**: after Setup. **Blocks every story.**
- **US1 (3)**: after Foundational. This is the MVP.
- **US2 (4)**: after US1. It verifies callers against the finished Paste view.
- **US3 (5)**: after Foundational. It touches the same component and JS file as US1, so in practice it goes after US1.
- **US5 (6)**: after Foundational only. It is independent of US1–US3 with two exceptions:
  - T036 edits `ScanRouter.cs`, which T009's tests also touch;
  - T037 edits `HandOffAsync` in `QrScannerModal.razor`, so it waits for US1's T016.

  Run the rest in parallel with US1–US3 if staffed.
- **US4 (7)**: after US1 (Paste view) and T026 (`_fitWithin`).
- **US6 (8)**: after US5's T043 and T044, which edit the same Receive and Payments markup.
- **Polish (9)**: after every story planned for the release.

### Story dependency graph

```text
Setup ─► Foundational ─┬─► US1 ─┬─► US2
                       │        ├─► US3 ─► US4
                       │        └──────────┘
                       └─► US5 ─► US6
                                         └─► Polish
```

### Within each story

Tests first and failing. Then the `DigiByte.Wallet` classes, then the JS glue, then the Razor UI, then locale text and styles.

---

## Parallel Opportunities

- **Foundational**: T003 can run alongside T005 and T006 (a test file versus a component).
- **US1**: T007, T008 and T009 (three test files) in parallel. Then T010, T011, T012 and T013 (Wallet classes versus JS) in parallel. T019, T020 and T021 can run at any point in the phase.
- **US5**: T033 and T034 in parallel. T043, T044, T045 and T046 in parallel once T035 is done.
- **Across stories**: US5 (payment links) can be built by a second person while US1 to US3 are in progress, because it touches different files, apart from `ScanRouter.cs` and the one hand-off change in `QrScannerModal.razor` (T037).
- **Polish**: T056 to T059 in parallel.

### Example: User Story 1

```text
Together: T007 ScanInputModeTests.cs | T008 PastedCodeTests.cs (LooksSecret) | T009 ScanRouterTests.cs
Then together: T010 ScanInputMode.cs | T011 PastedCode.LooksSecret | T012 deviceFacts() | T013 readClipboard()
Then in order: T014 → T015 → T016 → T017 (same component) → T018 (NavMenu)
Any time: T019 en.json | T020 es.json | T021 app.css
```

### Example: User Story 5

```text
Together: T033 PaymentLinkTests.cs | T034 ScanRouterTests.cs
Then: T035 PaymentLink.cs → T036 ScanRouter.cs, T037 panel hand-off and T038 PaymentHandoff → T039 Send.razor → T040/T041 Pay.razor → T042 layout check
Together after T035: T043 Receive.razor | T044 Payments.razor | T045 manifest | T046 locales
Last: T047 handler check
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 and Phase 2.
2. Phase 3 (US1).
3. **Stop and check** with quickstart Q-1 to Q-7 and P-1, P-2. This alone fixes the main desktop complaint.

### Incremental delivery

1. MVP: US1 and US2 ship together (one release: paste everywhere).
2. Add US3 (screenshot and drop) and US5 (payment links). These are the two P2 wins and can be built in parallel.
3. Add US6 (wide layout), and US4 (scan from screen) if ready. US4 may slip to a later release without blocking anything (clarification Q4).
4. Polish before each release: docs, changelog, the privacy pass and the Spanish review.

### Notes

- Constitution Principle III: T003, T007, T008, T033 and T034 must be committed failing before their implementations.
- Keep every commit Conventional, for example `feat(wallet): paste-first scan panel on desktop` or `test(wallet): PaymentLink reading rules`.
- Never log pasted, decoded or captured values, in .NET or JS.

# Quickstart: Validating Paste, Drop and Link Instead of Scan

Run these once the feature is built, to prove it works end to end. References: [contracts/](contracts/), [data-model.md](data-model.md), [research.md](research.md).

## Prerequisites

- .NET SDK as pinned in `global.json`, and Node 20+ for the JS tests.
- The wallet running locally: the `digibyte-web` configuration in `.claude/launch.json` (http://localhost:5251), or `dotnet run --project src/DigiByte.Web`.
- A desktop with Chrome or Edge, Firefox, and, if possible, Safari. A phone, or DevTools device emulation with touch, for the phone checks.
- A testnet wallet. Have a second browser profile or a private window with **no** wallet, for the payer-without-wallet checks.
- A page that shows a QR code, for example the wallet's own Receive screen open in a second window.

## 1. Automated checks

```powershell
dotnet build DigiByte.slnx
```

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~PastedCode|FullyQualifiedName~PaymentLink|FullyQualifiedName~PaymentHandoff|FullyQualifiedName~ScanInputMode|FullyQualifiedName~ScanRouter|FullyQualifiedName~LocaleParity|FullyQualifiedName~LocaleFiles"
```

```powershell
npm test --prefix src/DigiByte.Web
```

| Test | What it proves |
|---|---|
| `PastedCodeTests` | White space and quotes are removed, the size limit holds, and secrets are spotted while addresses, redeem scripts and sentences are not (FR-009, FR-027) |
| `PaymentLinkTests` | Round trip, every R7 failure, look-alike hosts, and the `web+digibyte` forms (FR-020, FR-024, SC-006) |
| `ScanInputModeTests` | Device rule and remembered choice (FR-001, FR-002, FR-004) |
| `PaymentHandoffTests` | The in-memory hand-off is taken once, then empty, and a new link replaces a waiting one (FR-021) |
| `ScanRouterTests` (extended) | Pasted text routes exactly like scanned text for every code kind, and the wallet's own `/pay#` link routes as a payment (FR-010, SC-003) |
| `LocaleParityTests`, `LocaleFilesTests` | Every new key exists in English and Spanish (FR-028, SC-008) |
| `qrscanner.test.mjs` | Frame scaling and the 60 s give-up clock |

## 2. Manual checks, desktop

| # | Steps | Expected |
|---|---|---|
| Q-1 | Desktop, mouse, camera unplugged or blocked. Press the menu's middle button. | Label reads **Paste / Scan**. The paste panel opens with the field focused. No camera prompt appears (SC-002). |
| Q-2 | Copy a testnet address. Press **Paste from clipboard**. | Send opens with the address filled in, in 2 actions and under 5 s (SC-001). |
| Q-3 | Copy a `digibyte:` URI with an amount. Press Ctrl+V in the panel. | Send opens with the address and amount. |
| Q-4 | Paste a Digi-ID link, a room invite link, a multisig proposal and a redeem script, one at a time. | Each lands on the same screen a camera scan would open (FR-010). |
| Q-5 | Paste `"  dgb1…  "` with quotes and line breaks. | Treated as the bare address (FR-009). |
| Q-6 | Paste `hello world`. | Nothing opens. `scan.unknown` is shown and the text stays editable. |
| Q-7 | Paste a 12-word phrase into the **menu's** panel. | The field is cleared at once, `scan.secret_cleared` shows, and nothing opens (FR-027). |
| Q-8 | On **Recover** (private key), open the scan button and paste a testnet WIF. | The key appears in Recover's field. Reopen the panel: its field is empty. |
| Q-9 | Win+Shift+S (or ⌘⇧⌃4) a QR code of an address, then Ctrl+V in the panel. | Send opens with the address, without saving a file, in under 10 s (SC-004). |
| Q-10 | Drag a QR PNG onto the panel. Then drag a `.txt` file, then an image over 10 MB. | The PNG works. The others show `scan.not_image` and `scan.image_too_large`. |
| Q-11 | **Scan from screen**, pick the window showing a QR code. | The code is read and Send opens. The browser's sharing indicator disappears at once. Repeat, cancel the prompt: `scan.screen_cancelled`, panel still usable. Repeat on a window with no code: it stops after about 60 s with `scan.screen_timeout`. |
| Q-12 | Installed PWA (Chrome or Edge): open a page with `<a href="web+digibyte:dgb1…?amount=1">` and click it. | The wallet opens `/pay` and continues as in Q-14. **Check that the handler URL accepted `%s` after `#`**: DevTools → Application → Manifest shows no protocol handler error. If it fails, remove the handler (FR-023) and note it in the PR. |
| Q-13 | Receive, set an amount, then **Copy payment link**. | The copied text is `http://localhost:5251/pay#digibyte:…?amount=…`. |
| Q-14 | Open that link in the same browser, wallet unlocked. Repeat with the wallet locked. Then reload Send. | Send opens with the address and amount, and the address bar shows only `/send` at every step. When locked, Unlock comes first, then Send with the values filled in. After the reload, Send is empty (FR-021). |
| Q-14a | On Send, press its scan button and paste a payment link copied from Receive (with a note set on Request). | The address and amount are filled in. The memo stays empty, because no note is carried into it (FR-011a). |
| Q-15 | Open the link in the profile with **no** wallet. | Read-only page: address, amount, label, QR code, Copy address, Copy amount, Set up this wallet. No send control (FR-022). |
| Q-16 | Edit the link: change one address character; set `amount=-1`; set `amount=1,5`; change the host to `localhost:5253`. | Each shows "Not a valid payment link" or, from the paste panel, `scan.unknown`. Nothing is filled in (SC-006). |
| Q-17 | DevTools → Network, preserve log. Repeat Q-2, Q-9, Q-11 and Q-14. | No request carries the pasted text, picture data or screen data. The request for `/pay` has no query and no payment data (SC-007). |
| Q-18 | Receive and Request at 1280 px wide. | The address and copy buttons come first, with a smaller QR code captioned "Scan with your phone" (FR-025). |
| Q-19 | Keyboard only: Tab to the menu button, Enter, type or paste, Enter, Escape. Screen reader on (NVDA or VoiceOver). | Everything is reachable. Status messages are announced. Focus returns to the menu button on close (FR-029). |
| Q-20 | Switch to Español and repeat Q-1, Q-7, Q-10 and Q-15. | All new text is in Spanish. The keyboard hint shows ⌘V on a Mac. |
| Q-21 | DevTools → Performance on a mid-range laptop: record a paste (Q-2), a 10 MB picture drop (Q-10) and 30 s of Scan from screen (Q-11). | From paste to the new screen under 300 ms after the clipboard read. Picture decoded under 1.5 s. Screen sampling uses under 30% of one CPU core. |

## 3. Manual checks, phone (regression)

| # | Steps | Expected |
|---|---|---|
| P-1 | Phone with a camera: press **Scan**. | The camera opens as today. **Paste instead** is offered. |
| P-2 | Choose **Paste instead**, close, and press Scan again. | The paste panel opens first (remembered, FR-004). Choose **Use camera** to switch back. |
| P-3 | Receive at phone width. | The layout is unchanged from today. |
| P-4 | Open a payment link on the phone with the wallet installed. | Same as Q-14. |

## 4. Done when

- Every automated test in section 1 passes.
- Q-1 to Q-21 (including Q-14a) and P-1 to P-4 behave as listed. Q-11 may be marked "not shipped" if Scan from screen is deferred (clarification Q4), and the release still ships.
- `docs/PROCESS_FLOWS.md`, `docs/ARCHITECTURE.md` and `CHANGELOG.md` are updated. The Spanish security text is approved by a native speaker.

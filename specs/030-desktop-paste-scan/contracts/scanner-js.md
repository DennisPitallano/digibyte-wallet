# Contract: `window.qrScanner` Additions (JS ↔ .NET)

File: `src/DigiByte.Web/wwwroot/js/qrscanner.js`. Existing members (`start`, `stop`, `isSupported`, `decodeImage`) keep their signatures. `decodeImage` stays for compatibility but is no longer used by the panel.

| Member | Signature | Returns / calls back | Notes |
|---|---|---|---|
| `deviceFacts` | `async () => DeviceFacts` | `{ finePointer, hasCamera: true\|false\|null, canCaptureScreen, canReadClipboardText, canReadClipboardImages }` | Never prompts. `enumerateDevices` failure → `hasCamera: null`. |
| `readClipboard` | `async () => { kind: 'text', text } \| { kind: 'code', text } \| { kind: 'nocode' } \| { kind: 'empty' } \| { kind: 'refused' }` | Prefers an image item (decoded here; only the result text is returned), then text | Must be called from the button's click (user activation). |
| `attachPanel` | `(panelEl, dotnetRef) => void` | Calls `OnPanelPaste(kind, text)` and `OnPanelDrop(kind, text)` with the same `kind` values plus `'notimage'` and `'toolarge'` | Adds `paste`, `dragenter`, `dragover`, `dragleave` and `drop` listeners to `panelEl`. Text pastes inside the textarea are left to the field; image pastes anywhere in the panel are intercepted. Calls `OnDragState(bool)` for the highlight. |
| `detachPanel` | `(panelEl) => void` | | Removes the listeners. Called on close and on Dispose. |
| `decodeBlob` | `async (blob) => string \| null` | Decoded text or null | Rejects non-`image/*` and anything over 10 MB before decoding. Downscales so the longer side is at most 2048 px. Uses jsQR `attemptBoth`. |
| `decodeInputFile` | `async (inputEl) => string \| null \| 'notimage' \| 'toolarge'` | | For the existing Upload image `InputFile`. Reads `inputEl.files[0]` in JS. |
| `startScreen` | `async (dotnetRef) => 'started' \| 'cancelled' \| 'unsupported'` | Later `OnScreenResult(text)` or `OnScreenEnded(reason)` with reason `'stopped' \| 'timeout' \| 'ended'` | `getDisplayMedia({ video: { frameRate: 5 }, audio: false })`. Samples every 250 ms. Scales to at most 1920 px. Stops all tracks before calling back. |
| `stopScreen` | `() => void` | | Idempotent. Called by Stop, close, Dispose and navigation. |
| `_fitWithin` | `(w, h, max) => { w, h }` | | Pure; covered by `qrscanner.test.mjs`. |
| `_giveUpClock` | `(startMs, nowMs, limitMs = 60000) => boolean` | | Pure; covered by `qrscanner.test.mjs`. |

## .NET side (`QrScannerModal`)

`[JSInvokable]` methods: `OnPanelPaste(string kind, string? text)`, `OnPanelDrop(string kind, string? text)`, `OnDragState(bool over)`, `OnScreenResult(string text)`, `OnScreenEnded(string reason)`, plus the existing `OnScanResult` and `OnScanError`.

Every text value received goes through `PastedCode.Clean` before anything else. The component's `DisposeAsync` calls `qrScanner.stop()`, `qrScanner.stopScreen()` and `qrScanner.detachPanel()`, and disposes the `DotNetObjectReference`.

## Guarantees

- No member sends data over the network, writes to storage, or logs pasted or decoded content. `console.error` may log error **names** only, never values.
- Picture bytes and screen frames never cross into .NET. Only decoded text does.
- After `stopScreen()`, or any `OnScreenEnded` callback, no `MediaStreamTrack` from `getDisplayMedia` is left in the `live` state.
